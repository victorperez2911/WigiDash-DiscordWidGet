using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WigiDash.DiscordWidget.Discord;

/// <summary>Em que pé está a conexão com o Discord.</summary>
public enum DiscordState
{
    /// <summary>Nenhum widget do Discord na tela.</summary>
    Stopped,
    Connecting,
    /// <summary>Nenhum pipe discord-ipc respondeu.</summary>
    DiscordNotRunning,
    /// <summary>Conectado, mas sem token: falta a pessoa autorizar (tela de configurações ou toque no widget).</summary>
    NeedsAuthorization,
    /// <summary>Popup aberto no Discord, esperando o clique em Autorizar.</summary>
    Authorizing,
    Ready,
    /// <summary>Erro; tenta de novo sozinho, com espera crescente.</summary>
    Failed,
}

/// <summary>
/// A conexão única com o Discord, dividida por todos os widgets do plugin (eles carregam no mesmo assembly).
/// Abre no primeiro <see cref="Acquire"/> e fecha quando o último widget sai. Reconecta sozinha quando o
/// Discord fecha ou reinicia, e renova o token quando vence.
/// </summary>
public sealed class DiscordSession
{
    private static readonly TimeSpan MinRetry = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxRetry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NotRunningRetry = TimeSpan.FromSeconds(5);

    private readonly AccountStore _store;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private ILogger _logger = NullLogger.Instance;
    private int _leases;
    private CancellationTokenSource? _runCts;
    private Task _loop = Task.CompletedTask;
    private CancellationTokenSource _connectionCts = new();
    private TaskCompletionSource _authorizationRequested = NewSignal();
    private volatile RpcClient? _rpc;
    private readonly SemaphoreSlim _channelLock = new(1, 1);
    private string? _watchedChannelId;

    private static readonly string[] VoiceStateEvents = ["VOICE_STATE_CREATE", "VOICE_STATE_UPDATE", "VOICE_STATE_DELETE"];

    /// <summary>Eventos de que só alguns widgets precisam; se o Discord recusar um, o resto segue.</summary>
    private static readonly string[] OptionalEvents = ["NOTIFICATION_CREATE", "VIDEO_STATE_UPDATE", "SCREENSHARE_STATE_UPDATE"];

    private const int MaxNotifications = 50;
    private readonly List<DiscordNotification> _notifications = [];
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly string _pipePrefix;

    public DiscordSession(AccountStore store, TimeProvider time, string pipePrefix = IpcConnection.DefaultPipePrefix)
    {
        _store = store;
        _time = time;
        _pipePrefix = pipePrefix;
    }

    public static DiscordSession Shared { get; } = new(AccountStore.Default, TimeProvider.System);

    public DiscordState State { get; private set; } = DiscordState.Stopped;

    /// <summary>Detalhe do estado para mostrar (erro, motivo da recusa).</summary>
    public string? Message { get; private set; }

    /// <summary>Nome de quem está logado no Discord (do READY).</summary>
    public string? UserName { get; private set; }

    public VoiceSettings? Voice { get; private set; }

    /// <summary>A call em que você está; <c>null</c> fora de call.</summary>
    public VoiceChannelInfo? VoiceChannel { get; private set; }

    /// <summary>Câmera ligada na call. <c>null</c> até o primeiro VIDEO_STATE_UPDATE.</summary>
    public bool? VideoActive { get; private set; }

    /// <summary>Transmissão de tela ligada. <c>null</c> até o primeiro SCREENSHARE_STATE_UPDATE.</summary>
    public bool? ScreenshareActive { get; private set; }

    /// <summary>Notificações recebidas desde a conexão (ou desde <see cref="ClearNotifications"/>), mais antiga primeiro.</summary>
    public IReadOnlyList<DiscordNotification> Notifications
    {
        get
        {
            lock (_notifications)
                return _notifications.ToList();
        }
    }

    public string ClientId => _store.ClientId;

    public bool HasSavedToken => _store.LoadToken() is not null;

    /// <summary>Qualquer mudança de estado, usuário ou voz. Chega em thread de fundo.</summary>
    public event Action? Changed;

    /// <summary>Evento DISPATCH do Discord (nome, data), para widgets que fazem SUBSCRIBE próprio.</summary>
    public event Action<string, JsonElement>? EventReceived;

    /// <summary>Usa o logger do host. Basta a primeira chamada.</summary>
    public void UseLogger(ILoggerFactory factory)
    {
        if (_logger == NullLogger.Instance)
            _logger = factory.CreateLogger("Discord");
    }

    /// <summary>Um widget passou a usar a sessão. Descartar o retorno libera.</summary>
    public IDisposable Acquire()
    {
        lock (_lock)
        {
            if (_leases++ == 0)
            {
                _runCts = new CancellationTokenSource();
                var token = _runCts.Token;
                var previous = _loop;
                _loop = Task.Run(async () =>
                {
                    await previous.ConfigureAwait(false);
                    await RunAsync(token).ConfigureAwait(false);
                });
            }
        }
        return new Lease(this);
    }

    private void Release()
    {
        lock (_lock)
        {
            if (--_leases == 0)
            {
                _runCts?.Cancel();
                _runCts?.Dispose();
                _runCts = null;
            }
        }
    }

    /// <summary>Pede o popup de autorização no Discord (botão na tela de configurações ou toque no widget).</summary>
    public void RequestAuthorization() => _authorizationRequested.TrySetResult();

    /// <summary>Esquece o token e reconecta; o próximo passo é autorizar de novo.</summary>
    public void SignOut()
    {
        _store.SaveToken(null);
        Reconnect();
    }

    /// <summary>Troca o app do Developer Portal. Token de outro app é descartado.</summary>
    public void SetClientId(string clientId)
    {
        clientId = clientId.Trim();
        if (clientId.Length == 0 || clientId == _store.ClientId)
            return;
        _store.ClientId = clientId;
        Reconnect();
    }

    /// <summary>Tenta conectar agora, sem esperar a próxima tentativa.</summary>
    public void Reconnect() => _connectionCts.Cancel();

    /// <summary>Manda um comando RPC. Só funciona com a sessão <see cref="DiscordState.Ready"/>.</summary>
    public Task<JsonElement> SendAsync(string command, JsonObject? args = null, CancellationToken cancellationToken = default)
    {
        var rpc = _rpc;
        if (rpc is null || State != DiscordState.Ready)
            throw new InvalidOperationException("Discord não está conectado.");
        return rpc.SendAsync(command, args, cancellationToken: cancellationToken);
    }

    /// <summary>SET_VOICE_SETTINGS; a resposta já atualiza <see cref="Voice"/> sem esperar o evento.</summary>
    public async Task SetVoiceSettingsAsync(JsonObject args, CancellationToken cancellationToken = default)
    {
        var data = await SendAsync("SET_VOICE_SETTINGS", args, cancellationToken).ConfigureAwait(false);
        if (data.ValueKind == JsonValueKind.Object)
            UpdateVoice(data);
    }

    public Task SubscribeAsync(string evt, JsonObject? args = null, CancellationToken cancellationToken = default)
    {
        var rpc = _rpc ?? throw new InvalidOperationException("Discord não está conectado.");
        return rpc.SubscribeAsync(evt, args, cancellationToken);
    }

    public async Task<IReadOnlyList<Guild>> GetGuildsAsync(CancellationToken cancellationToken = default) =>
        GuildParsing.ParseGuilds(await SendAsync("GET_GUILDS", cancellationToken: cancellationToken).ConfigureAwait(false));

    public async Task<IReadOnlyList<Channel>> GetChannelsAsync(string guildId, CancellationToken cancellationToken = default) =>
        GuildParsing.ParseChannels(await SendAsync("GET_CHANNELS", new JsonObject { ["guild_id"] = guildId }, cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Entra na call (<paramref name="channelId"/>) ou sai dela (<c>null</c>).</summary>
    public async Task SelectVoiceChannelAsync(string? channelId, CancellationToken cancellationToken = default)
    {
        await SendAsync("SELECT_VOICE_CHANNEL", new JsonObject { ["channel_id"] = channelId, ["force"] = true }, cancellationToken)
            .ConfigureAwait(false);
        if (_rpc is { } rpc)
            await RefreshVoiceChannelAsync(rpc, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Volume (0-200) e/ou mute local de alguém na call.</summary>
    public async Task SetUserVoiceSettingsAsync(string userId, double? volume, bool? mute, CancellationToken cancellationToken = default)
    {
        var args = new JsonObject { ["user_id"] = userId };
        if (volume is { } v)
            args["volume"] = Math.Clamp(v, 0, 200);
        if (mute is { } m)
            args["mute"] = m;
        await SendAsync("SET_USER_VOICE_SETTINGS", args, cancellationToken).ConfigureAwait(false);
        if (_rpc is { } rpc)
            await RefreshVoiceChannelAsync(rpc, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Relê a call atual e passa a ouvir quem entra, sai ou muda nela (VOICE_STATE_* são por canal).
    /// </summary>
    private async Task RefreshVoiceChannelAsync(RpcClient rpc, CancellationToken cancellationToken)
    {
        await _channelLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var channel = VoiceChannelInfo.Parse(await rpc.SendAsync("GET_SELECTED_VOICE_CHANNEL", cancellationToken: cancellationToken)
                .ConfigureAwait(false));
            if (channel?.Id != _watchedChannelId)
            {
                if (_watchedChannelId is { } old)
                {
                    foreach (var evt in VoiceStateEvents)
                        await rpc.UnsubscribeAsync(evt, new JsonObject { ["channel_id"] = old }, cancellationToken).ConfigureAwait(false);
                }
                _watchedChannelId = channel?.Id;
                if (channel is not null)
                {
                    foreach (var evt in VoiceStateEvents)
                        await rpc.SubscribeAsync(evt, new JsonObject { ["channel_id"] = channel.Id }, cancellationToken).ConfigureAwait(false);
                }
            }
            VoiceChannel = channel;
            RaiseChanged();
        }
        finally
        {
            _channelLock.Release();
        }
    }

    public void ClearNotifications()
    {
        lock (_notifications)
            _notifications.Clear();
        RaiseChanged();
    }

    /// <summary>Liga ou desliga a câmera na call (TOGGLE_VIDEO).</summary>
    public async Task ToggleVideoAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync("TOGGLE_VIDEO", cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Liga ou desliga a transmissão de tela (TOGGLE_SCREENSHARE).</summary>
    public async Task ToggleScreenshareAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync("TOGGLE_SCREENSHARE", cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sons do soundboard que você pode tocar (GET_SOUNDBOARD_SOUNDS).</summary>
    public async Task<IReadOnlyList<SoundboardSound>> GetSoundboardSoundsAsync(CancellationToken cancellationToken = default) =>
        SoundboardSound.ParseList(await SendAsync("GET_SOUNDBOARD_SOUNDS", cancellationToken: cancellationToken).ConfigureAwait(false));

    public Task PlaySoundboardSoundAsync(SoundboardSound sound, CancellationToken cancellationToken = default)
    {
        var args = new JsonObject { ["sound_id"] = sound.Id };
        if (sound.GuildId is { } guildId)
            args["guild_id"] = guildId;
        return SendAsync("PLAY_SOUNDBOARD_SOUND", args, cancellationToken);
    }

    /// <summary>
    /// Membros e online de um servidor, pela API REST com o token do usuário
    /// (<c>users/@me/guilds?with_counts=true</c>, scope guilds). <c>null</c> se você não está nele.
    /// </summary>
    public async Task<GuildCounts?> GetGuildCountsAsync(string guildId, CancellationToken cancellationToken = default)
    {
        if (State != DiscordState.Ready || _store.LoadToken() is not { } token)
            throw new InvalidOperationException("Discord não está conectado.");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/users/@me/guilds?with_counts=true");
        request.Headers.Authorization = new("Bearer", token.AccessToken);
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return GuildCounts.Find(json.RootElement, guildId);
    }

    /// <summary>Base da API REST; os testes apontam para um servidor local.</summary>
    internal string ApiBase { get; set; } = "https://discord.com/api/v10";

    private async Task TrySubscribeAsync(RpcClient rpc, string evt, CancellationToken ct)
    {
        try
        {
            await rpc.SubscribeAsync(evt, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (DiscordRpcException ex)
        {
            _logger.LogInformation("Discord: evento {Event} indisponível ({Message})", evt, ex.Message);
        }
    }

    private static bool? ReadActive(JsonElement data)
    {
        foreach (var name in new[] { "active", "enabled", "state" })
        {
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value)
                && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return value.GetBoolean();
        }
        return null;
    }

    private void RefreshVoiceChannelInBackground()
    {
        if (_rpc is not { } rpc)
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshVoiceChannelAsync(rpc, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is DiscordRpcException or IOException or TimeoutException or ObjectDisposedException)
            {
                _logger.LogDebug(ex, "Discord: não deu para reler a call");
            }
        });
    }

    private async Task RunAsync(CancellationToken stopping)
    {
        var retry = MinRetry;
        while (!stopping.IsCancellationRequested)
        {
            CancellationTokenSource connection;
            lock (_lock)
            {
                if (_connectionCts.IsCancellationRequested)
                {
                    _connectionCts.Dispose();
                    _connectionCts = new CancellationTokenSource();
                }
                connection = _connectionCts;
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stopping, connection.Token);
            var ct = linked.Token;

            RpcClient? rpc = null;
            try
            {
                SetState(DiscordState.Connecting);
                rpc = await RpcClient.ConnectAsync(_store.ClientId, ct, _pipePrefix).ConfigureAwait(false);
                rpc.EventReceived += OnEvent;
                UserName = ReadUserName(rpc.Ready);
                _logger.LogInformation("Discord: conectado ao pipe como {User}", UserName);

                await AuthenticateAsync(rpc, ct).ConfigureAwait(false);

                Voice = VoiceSettings.Parse(await rpc.SendAsync("GET_VOICE_SETTINGS", cancellationToken: ct).ConfigureAwait(false));
                await rpc.SubscribeAsync("VOICE_SETTINGS_UPDATE", cancellationToken: ct).ConfigureAwait(false);
                await rpc.SubscribeAsync("VOICE_CHANNEL_SELECT", cancellationToken: ct).ConfigureAwait(false);
                foreach (var evt in OptionalEvents)
                    await TrySubscribeAsync(rpc, evt, ct).ConfigureAwait(false);
                _rpc = rpc;
                await RefreshVoiceChannelAsync(rpc, ct).ConfigureAwait(false);
                retry = MinRetry;
                SetState(DiscordState.Ready);

                var closed = await rpc.Closed.WaitAsync(ct).ConfigureAwait(false);
                _logger.LogInformation("Discord: conexão caiu ({Reason})", closed?.Message ?? "fechada");
                SetState(DiscordState.Connecting, closed?.Message);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException) when (connection.IsCancellationRequested)
            {
                // Reconnect/SignOut/SetClientId: conecta de novo na hora.
                retry = TimeSpan.Zero;
            }
            catch (DiscordNotRunningException)
            {
                SetState(DiscordState.DiscordNotRunning);
                retry = NotRunningRetry;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Discord: falha na sessão");
                SetState(DiscordState.Failed, ex.Message);
                retry = retry == TimeSpan.Zero ? MinRetry : TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, MaxRetry.Ticks));
            }
            finally
            {
                _rpc = null;
                Voice = null;
                VoiceChannel = null;
                VideoActive = null;
                ScreenshareActive = null;
                _watchedChannelId = null;
                if (rpc is not null)
                {
                    rpc.EventReceived -= OnEvent;
                    await rpc.DisposeAsync().ConfigureAwait(false);
                }
            }

            if (retry > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(retry, _time, connection.Token).WaitAsync(stopping).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
                {
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        UserName = null;
        SetState(DiscordState.Stopped);
    }

    /// <summary>
    /// Token salvo (renovado se vencido) ou, sem token, espera o pedido de autorização e abre o popup.
    /// Token recusado pelo Discord é renovado uma vez; se ainda falhar, volta a pedir autorização.
    /// </summary>
    private async Task AuthenticateAsync(RpcClient rpc, CancellationToken ct)
    {
        var clientId = _store.ClientId;
        var refreshed = false;
        while (true)
        {
            var token = _store.LoadToken();
            if (token is null)
            {
                token = await AuthorizeAsync(rpc, clientId, ct).ConfigureAwait(false);
                refreshed = true;
            }
            else if (token.IsExpired(_time.GetUtcNow()))
            {
                token = await TryRefreshAsync(clientId, token, ct).ConfigureAwait(false);
                refreshed = true;
                if (token is null)
                    continue;
            }

            try
            {
                await DiscordAuth.AuthenticateAsync(rpc, token, ct).ConfigureAwait(false);
                return;
            }
            catch (DiscordRpcException ex)
            {
                _logger.LogWarning("Discord: AUTHENTICATE recusado ({Code}: {Message})", ex.Code, ex.Message);
                if (!refreshed)
                {
                    refreshed = true;
                    if (await TryRefreshAsync(clientId, token, ct).ConfigureAwait(false) is not null)
                        continue;
                }
                _store.SaveToken(null);
            }
        }
    }

    private async Task<DiscordToken> AuthorizeAsync(RpcClient rpc, string clientId, CancellationToken ct)
    {
        while (true)
        {
            SetState(DiscordState.NeedsAuthorization, Message);
            var request = _authorizationRequested.Task;
            var finished = await Task.WhenAny(request, rpc.Closed).WaitAsync(ct).ConfigureAwait(false);
            if (finished != request)
                throw new IOException("O Discord fechou a conexão antes da autorização.");
            _authorizationRequested = NewSignal();

            SetState(DiscordState.Authorizing);
            try
            {
                var token = await DiscordAuth.AuthorizeAsync(rpc, clientId, _time, ct).ConfigureAwait(false);
                _store.SaveToken(token);
                _logger.LogInformation("Discord: autorizado (scopes: {Scope})", token.Scope);
                return token;
            }
            catch (Exception ex) when (ex is DiscordRpcException or DiscordAuthException or TimeoutException)
            {
                _logger.LogWarning("Discord: autorização não concluída: {Message}", ex.Message);
                Message = ex is TimeoutException ? "Ninguém clicou em Autorizar a tempo." : ex.Message;
            }
        }
    }

    private async Task<DiscordToken?> TryRefreshAsync(string clientId, DiscordToken token, CancellationToken ct)
    {
        try
        {
            var renewed = await DiscordAuth.RefreshAsync(clientId, token, _time, ct).ConfigureAwait(false);
            _store.SaveToken(renewed);
            _logger.LogInformation("Discord: token renovado");
            return renewed;
        }
        catch (DiscordAuthException ex) when (ex.IsInvalidGrant || ex.Status is 400 or 401)
        {
            _logger.LogWarning("Discord: refresh recusado ({Message}); precisa autorizar de novo", ex.Message);
            _store.SaveToken(null);
            return null;
        }
    }

    private void OnEvent(string evt, JsonElement data)
    {
        if (evt == "VOICE_SETTINGS_UPDATE")
            UpdateVoice(data);
        else if (evt is "VOICE_CHANNEL_SELECT" or "VOICE_STATE_CREATE" or "VOICE_STATE_UPDATE" or "VOICE_STATE_DELETE")
            RefreshVoiceChannelInBackground();
        else if (evt == "VIDEO_STATE_UPDATE")
        {
            VideoActive = ReadActive(data);
            RaiseChanged();
        }
        else if (evt == "SCREENSHARE_STATE_UPDATE")
        {
            ScreenshareActive = ReadActive(data);
            RaiseChanged();
        }
        else if (evt == "NOTIFICATION_CREATE")
        {
            lock (_notifications)
            {
                _notifications.Add(DiscordNotification.Parse(data, _time.GetUtcNow()));
                if (_notifications.Count > MaxNotifications)
                    _notifications.RemoveAt(0);
            }
            RaiseChanged();
        }
        try
        {
            EventReceived?.Invoke(evt, data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discord: erro tratando o evento {Event}", evt);
        }
    }

    private void UpdateVoice(JsonElement data)
    {
        Voice = VoiceSettings.Parse(data).MergeDevices(Voice);
        RaiseChanged();
    }

    private void SetState(DiscordState state, string? message = null)
    {
        State = state;
        Message = message;
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discord: erro avisando os widgets");
        }
    }

    private static string? ReadUserName(JsonElement ready)
    {
        if (!ready.TryGetProperty("user", out var user))
            return null;
        if (user.TryGetProperty("global_name", out var global) && global.ValueKind == JsonValueKind.String)
            return global.GetString();
        return user.TryGetProperty("username", out var name) ? name.GetString() : null;
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Lease(DiscordSession session) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                session.Release();
        }
    }
}
