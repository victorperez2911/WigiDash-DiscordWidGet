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
                _rpc = rpc;
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
