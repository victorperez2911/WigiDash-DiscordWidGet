using System.Text.Json.Nodes;
using OpenWigi.Sdk;
using WigiDash.DiscordWidget.Discord;
using WigiDash.DiscordWidget.Rendering;
using WigiDash.DiscordWidget.Settings;
using WigiDash.DiscordWidget.Widgets;

[assembly: OpenWigiWidget(typeof(VoiceChannelWidget))]
[assembly: OpenWigiWidget(typeof(TextChannelWidget))]

namespace WigiDash.DiscordWidget.Widgets;

/// <summary>Servidor e canal escolhidos na tela de configurações (nomes guardados para desenhar offline).</summary>
public sealed record ChannelOptions
{
    public string? GuildId { get; init; }
    public string? GuildName { get; init; }
    public string? ChannelId { get; init; }
    public string? ChannelName { get; init; }
}

/// <summary>Entra e sai de um canal de voz (SELECT_VOICE_CHANNEL); mostra quantos estão na call.</summary>
public sealed class VoiceChannelWidget : DiscordWidgetBase
{
    public override WidgetManifest Manifest { get; } = CreateManifest(
        new Guid("086178a6-2acc-485a-864b-d33e5ca30c41"),
        "Discord: Voice Channel",
        "Toque entra no canal de voz escolhido; tocar de novo sai da call. Fica verde enquanto você está nele.");

    protected override ButtonVisual Preview => new(Glyphs.Phone, "Voice Channel");

    protected override DiscordButtonInstance CreateInstance(IWidgetContext context) => new ChannelInstance(context, voice: true);
}

/// <summary>Abre um canal de texto no Discord (SELECT_TEXT_CHANNEL).</summary>
public sealed class TextChannelWidget : DiscordWidgetBase
{
    public override WidgetManifest Manifest { get; } = CreateManifest(
        new Guid("03d9439f-fc1f-4f0d-86e3-6e3953e33bb4"),
        "Discord: Text Channel",
        "Toque abre o canal de texto escolhido na janela do Discord.");

    protected override ButtonVisual Preview => new(Glyphs.Message, "Text Channel");

    protected override DiscordButtonInstance CreateInstance(IWidgetContext context) => new ChannelInstance(context, voice: false);
}

public sealed class ChannelInstance(IWidgetContext context, bool voice) : DiscordOptionsInstance<ChannelOptions>(context)
{
    public bool IsVoice { get; } = voice;

    protected override string Glyph => IsVoice ? Glyphs.Phone : Glyphs.Message;
    protected override string Label => IsVoice ? "Voice Channel" : "Text Channel";

    public override Type? OptionsComponent => typeof(ChannelOptionsPanel);

    private static readonly TimeSpan JoinPulse = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan PulseFrame = TimeSpan.FromMilliseconds(400);
    private IDisposable? _watch;
    private string? _watchedId;
    private int _lastCount = -1;
    private DateTimeOffset _pulseUntil;
    private bool _guildRequested;

    private bool IsInChannel => IsVoice && Options.ChannelId is { } id && Session.VoiceChannel?.Id == id;

    protected internal override async ValueTask LoadSettingsAsync(CancellationToken cancellationToken)
    {
        await base.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        UpdateWatch();
    }

    public override async Task SaveOptionsAsync(ChannelOptions options)
    {
        await base.SaveOptionsAsync(options).ConfigureAwait(false);
        UpdateWatch();
    }

    /// <summary>Acompanha quem está no canal escolhido, para mostrar os avatares mesmo antes de entrar.</summary>
    private void UpdateWatch()
    {
        var id = IsVoice ? Options.ChannelId : null;
        if (id == _watchedId)
            return;
        _watch?.Dispose();
        _watch = id is null ? null : Session.WatchChannel(id);
        _watchedId = id;
    }

    protected override void OnDisposing() => _watch?.Dispose();

    protected override ButtonVisual GetVisual()
    {
        if (Options.ChannelId is not { } channelId)
            return new ButtonVisual(Glyph, Label) { Detail = "Escolha o canal", Dimmed = true };

        if (!IsVoice)
            return new ButtonVisual(Glyph, "#" + Options.ChannelName) { Detail = Options.GuildName };

        // Mesmo padrão das imagens do plugin da Elgato: fundo azul do Discord fora da call e verde dentro,
        // avatares de quem está no canal, contador no canto e anel de quem está falando. Vazio, o logo do servidor.
        var name = Options.ChannelName ?? "?";
        var info = Session.GetChannel(channelId);
        var users = info?.Users ?? [];
        var joined = IsInChannel;
        WatchArrivals(info is null ? -1 : users.Count, joined);
        var accent = joined ? ButtonRenderer.Success : ButtonRenderer.DiscordBlurple;
        if (users.Count == 0)
            return new ButtonVisual(Glyphs.Phone, name) { Accent = accent, Detail = "Canal vazio", ImageUrl = GuildIcon() };

        // Alguém entrou e você não está: pisca mais claro por uns segundos para chamar a atenção.
        var now = Context.Host.Time.GetUtcNow();
        if (!joined && now < _pulseUntil && (now - (_pulseUntil - JoinPulse)) / PulseFrame % 2 < 1)
            accent = ButtonRenderer.Lighter(ButtonRenderer.DiscordBlurple);
        return new ButtonVisual(Glyphs.Phone, name)
        {
            Accent = accent,
            Faces = users.Select(u => new AvatarFace(u.AvatarUrl, u.Name, Session.IsSpeaking(u.Id))).ToList(),
            Badge = users.Count,
            Detail = joined ? null : "Toque para entrar",
        };
    }

    /// <summary>Quando a contagem sobe com você fora do canal, liga o pulso (a primeira leitura não conta).</summary>
    private void WatchArrivals(int count, bool joined)
    {
        if (count < 0)
            return;
        if (_lastCount >= 0 && count > _lastCount && !joined)
        {
            _pulseUntil = Context.Host.Time.GetUtcNow() + JoinPulse;
            _ = PulseAsync();
        }
        _lastCount = count;
    }

    private async Task PulseAsync()
    {
        try
        {
            while (Context.Host.Time.GetUtcNow() < _pulseUntil)
            {
                await Task.Delay(PulseFrame, Context.Host.Time, Context.Stopping).ConfigureAwait(false);
                Refresh();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Logo do servidor; na primeira vez pede a lista de servidores ao Discord.</summary>
    private string? GuildIcon()
    {
        if (Options.GuildId is not { } guildId)
            return null;
        if (Session.FindGuild(guildId) is { } guild)
            return guild.IconUrl;
        if (!_guildRequested && Session.State == DiscordState.Ready)
        {
            _guildRequested = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Session.GetGuildsAsync().ConfigureAwait(false);
                    Refresh();
                }
                catch (Exception ex) when (ex is DiscordRpcException or InvalidOperationException or TimeoutException)
                {
                    _guildRequested = false;
                }
            });
        }
        return null;
    }

    protected override async Task OnReadyTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        if (touch.Kind != TouchKind.Tap || Options.ChannelId is not { } channelId)
            return;
        if (!IsVoice)
        {
            await ConfirmAsync(() => Session.SendAsync("SELECT_TEXT_CHANNEL", new JsonObject { ["channel_id"] = channelId },
                cancellationToken)).ConfigureAwait(false);
            return;
        }
        await Session.SelectVoiceChannelAsync(IsInChannel ? null : channelId, cancellationToken).ConfigureAwait(false);
    }
}
