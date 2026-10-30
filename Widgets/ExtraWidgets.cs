using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using OpenWigi.Sdk;
using WigiDash.DiscordWidget.Discord;
using WigiDash.DiscordWidget.Rendering;
using WigiDash.DiscordWidget.Settings;
using WigiDash.DiscordWidget.Widgets;

[assembly: OpenWigiWidget(typeof(SoundboardWidget))]
[assembly: OpenWigiWidget(typeof(NotificationsWidget))]
[assembly: OpenWigiWidget(typeof(ServerStatsWidget))]
[assembly: OpenWigiWidget(typeof(VideoToggleWidget))]
[assembly: OpenWigiWidget(typeof(StreamToggleWidget))]

namespace WigiDash.DiscordWidget.Widgets;

public sealed record SoundboardOptions
{
    public string? SoundId { get; init; }
    public string? SoundName { get; init; }
    public string? GuildId { get; init; }
    public string? Emoji { get; init; }
    public string? EmojiId { get; init; }
}

/// <summary>Toca um som do soundboard na call (PLAY_SOUNDBOARD_SOUND).</summary>
public sealed class SoundboardWidget : DiscordWidgetBase
{
    public override WidgetManifest Manifest { get; } = CreateManifest(
        new Guid("ac4d6e7c-a5d1-4403-86ca-98908c57b0a9"),
        "Discord: Soundboard",
        "Toque toca o som escolhido do soundboard na call em que você está.");

    protected override ButtonVisual Preview => new(Glyphs.Music, "Soundboard");

    protected override DiscordButtonInstance CreateInstance(IWidgetContext context) => new SoundboardInstance(context);
}

public sealed class SoundboardInstance(IWidgetContext context) : DiscordOptionsInstance<SoundboardOptions>(context)
{
    protected override string Glyph => Glyphs.Music;
    protected override string Label => "Soundboard";

    /// <summary>Duração usada quando não deu para ler a do arquivo.</summary>
    private static readonly TimeSpan FallbackDuration = TimeSpan.FromSeconds(2);

    protected internal override async ValueTask LoadSettingsAsync(CancellationToken cancellationToken)
    {
        await base.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        PrefetchDuration();
    }

    public override async Task SaveOptionsAsync(SoundboardOptions options)
    {
        await base.SaveOptionsAsync(options).ConfigureAwait(false);
        PrefetchDuration();
    }

    /// <summary>Baixa a duração antes do primeiro toque, para a barra já começar certa.</summary>
    private void PrefetchDuration()
    {
        if (Options.SoundId is { } id)
            _ = SoundDuration.GetAsync(id);
    }

    public override Type? OptionsComponent => typeof(SoundboardOptionsPanel);

    protected override ButtonVisual GetVisual()
    {
        if (Options.SoundId is null)
            return new ButtonVisual(Glyph, Label) { Detail = "Escolha o som", Dimmed = true };
        var image = SoundboardSound.EmojiUrl(Options.Emoji, Options.EmojiId);
        var name = Options.SoundName ?? Label;
        // Ensurdecido, o Discord não toca o soundboard: o botão fica apagado.
        if (Session.Voice is { Deaf: true })
            return new ButtonVisual(Glyph, name) { ImageUrl = image, Detail = "Ensurdecido", Dimmed = true };
        return Session.VoiceChannel is null
            ? new ButtonVisual(Glyph, name) { ImageUrl = image, Detail = "Entre numa call", Dimmed = true }
            : new ButtonVisual(Glyph, name) { ImageUrl = image };
    }

    protected override async Task OnReadyTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        if (touch.Kind != TouchKind.Tap || Options.SoundId is not { } id || Session.VoiceChannel is null || Session.Voice is { Deaf: true })
            return;
        await Session.PlaySoundboardSoundAsync(new SoundboardSound(id, Options.SoundName ?? id, Options.GuildId, Options.Emoji),
            cancellationToken).ConfigureAwait(false);
        // Tocando: verde, com a barra esvaziando pela duração do som.
        ShowFeedback(true, await SoundDuration.GetAsync(id, cancellationToken).ConfigureAwait(false) ?? FallbackDuration);
    }

    /// <summary>Ex.: "Invalid Sound" para som de outro servidor sem Nitro (visto no teste de 30/10).</summary>
    protected override string FailureText(Exception error) =>
        error is DiscordRpcException && !SoundboardSound.IsDefaultGuild(Options.GuildId) ? "Som de outro servidor" : "Não tocou";
}

public enum NotificationPress
{
    Nothing,
    /// <summary>Abre o canal da notificação mostrada.</summary>
    Open,
    Clear,
    /// <summary>Passa para a próxima, da mais antiga para a mais nova.</summary>
    CycleOldest,
    /// <summary>Passa para a próxima, da mais nova para a mais antiga.</summary>
    CycleRecent,
}

public sealed record NotificationsOptions
{
    public NotificationPress OnPress { get; init; } = NotificationPress.Open;
}

/// <summary>Conta as notificações do Discord (NOTIFICATION_CREATE) e mostra a mais recente.</summary>
public sealed class NotificationsWidget : DiscordWidgetBase
{
    public override WidgetManifest Manifest { get; } = CreateManifest(
        new Guid("0ff2ec59-c7dc-451b-b7cd-354d1480f1c9"),
        "Discord: Notifications",
        "Mostra quantas notificações chegaram e a última. O toque abre, limpa ou passa pelas notificações.");

    protected override ButtonVisual Preview => new(Glyphs.Ringer, "Notificações");

    protected override DiscordButtonInstance CreateInstance(IWidgetContext context) => new NotificationsInstance(context);
}

public sealed class NotificationsInstance(IWidgetContext context) : DiscordOptionsInstance<NotificationsOptions>(context)
{
    /// <summary>Posição mostrada ao passar pelas notificações; <c>null</c> = a mais recente.</summary>
    private int? _index;

    protected override string Glyph => Glyphs.Ringer;
    protected override string Label => "Notificações";

    public override Type? OptionsComponent => typeof(NotificationsOptionsPanel);

    private DiscordNotification? Shown(IReadOnlyList<DiscordNotification> list) =>
        list.Count == 0 ? null : list[Math.Clamp(_index ?? list.Count - 1, 0, list.Count - 1)];

    protected override ButtonVisual GetVisual()
    {
        var list = Session.Notifications;
        if (Shown(list) is not { } shown)
            return new ButtonVisual(Glyph, "Nenhuma notificação");
        var position = _index is { } i ? $"{Math.Clamp(i, 0, list.Count - 1) + 1}/{list.Count}" : $"{list.Count}";
        return new ButtonVisual(Glyph, $"{position}  {shown.Title}")
        {
            Accent = ButtonRenderer.Success,
            Detail = shown.Body,
        };
    }

    protected override async Task OnReadyTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        if (touch.Kind != TouchKind.Tap)
            return;
        var list = Session.Notifications;
        switch (Options.OnPress)
        {
            case NotificationPress.Open when Shown(list)?.ChannelId is { } channelId:
                await ConfirmAsync(() => Session.SendAsync("SELECT_TEXT_CHANNEL", new JsonObject { ["channel_id"] = channelId },
                    cancellationToken)).ConfigureAwait(false);
                break;
            case NotificationPress.Clear:
                _index = null;
                Session.ClearNotifications();
                ShowFeedback(true);
                break;
            case NotificationPress.CycleOldest when list.Count > 0:
                _index = _index is { } i && i + 1 < list.Count ? i + 1 : 0;
                Refresh();
                break;
            case NotificationPress.CycleRecent when list.Count > 0:
                _index = _index is { } j && j > 0 ? j - 1 : list.Count - 1;
                Refresh();
                break;
        }
    }
}

public sealed record ServerStatsOptions
{
    public string? GuildId { get; init; }
    public string? GuildName { get; init; }
}

/// <summary>Membros online e total de um servidor, relidos a cada minuto.</summary>
public sealed class ServerStatsWidget : DiscordWidgetBase
{
    public override WidgetManifest Manifest { get; } = CreateManifest(
        new Guid("b6dead57-63be-4e7c-8cd5-0782b6264573"),
        "Discord: Server Stats",
        "Mostra quantos membros de um servidor estão online e o total, atualizado a cada minuto.");

    protected override ButtonVisual Preview => new(Glyphs.People, "Server Stats");

    protected override DiscordButtonInstance CreateInstance(IWidgetContext context) => new ServerStatsInstance(context);
}

public sealed class ServerStatsInstance : DiscordOptionsInstance<ServerStatsOptions>
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);
    private readonly CancellationTokenSource _stop = new();
    private GuildCounts? _counts;
    private bool _asleep;

    public ServerStatsInstance(IWidgetContext context) : base(context)
    {
        _ = Task.Run(PollAsync);
    }

    protected override string Glyph => Glyphs.People;
    protected override string Label => "Server Stats";

    public override Type? OptionsComponent => typeof(ServerStatsOptionsPanel);

    protected override ButtonVisual GetVisual()
    {
        if (Options.GuildId is null)
            return new ButtonVisual(Glyph, Label) { Detail = "Escolha o servidor", Dimmed = true };
        if (_counts is not { } counts)
            return new ButtonVisual(Glyph, Options.GuildName ?? Label) { Detail = "Carregando...", Dimmed = true };
        return new ButtonVisual(Glyph, $"{counts.Online:N0} online") { Detail = $"{counts.Members:N0} membros · {counts.Name}" };
    }

    protected override async Task OnReadyTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        if (touch.Kind != TouchKind.Tap || Options.GuildId is not { } guildId)
            return;
        _counts = await Session.GetGuildCountsAsync(guildId, cancellationToken).ConfigureAwait(false);
        ShowFeedback(true);
    }

    public override async Task SaveOptionsAsync(ServerStatsOptions options)
    {
        _counts = null;
        await base.SaveOptionsAsync(options).ConfigureAwait(false);
        await RefreshCountsAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Relê a cada minuto; enquanto não tem número (conectando, servidor novo), tenta a cada 5 s.</summary>
    private async Task PollAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                if (!_asleep)
                    await RefreshCountsAsync(_stop.Token).ConfigureAwait(false);
                await Task.Delay(_counts is null ? RetryInterval : Interval, Context.Host.Time, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshCountsAsync(CancellationToken cancellationToken)
    {
        if (Options.GuildId is not { } guildId || Session.State != DiscordState.Ready)
            return;
        try
        {
            _counts = await Session.GetGuildCountsAsync(guildId, cancellationToken).ConfigureAwait(false);
            Refresh();
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            Context.Logger.LogDebug(ex, "Discord: não deu para ler as contagens do servidor");
        }
    }

    public override ValueTask OnSleepAsync(CancellationToken cancellationToken)
    {
        _asleep = true;
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnWakeAsync(CancellationToken cancellationToken)
    {
        _asleep = false;
        return ValueTask.CompletedTask;
    }

    protected override void OnDisposing()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}

/// <summary>Liga e desliga a câmera na call (TOGGLE_VIDEO).</summary>
public sealed class VideoToggleWidget : DiscordWidgetBase
{
    public override WidgetManifest Manifest { get; } = CreateManifest(
        new Guid("e4e6df7a-a54e-468e-8fbd-7210d4cc4feb"),
        "Discord: Video Toggle",
        "Toque liga e desliga a câmera na call do Discord.");

    protected override ButtonVisual Preview => new(Glyphs.Video, "Câmera");

    protected override DiscordButtonInstance CreateInstance(IWidgetContext context) => new MediaToggleInstance(context, video: true);
}

/// <summary>Liga e desliga a transmissão de tela na call (TOGGLE_SCREENSHARE).</summary>
public sealed class StreamToggleWidget : DiscordWidgetBase
{
    public override WidgetManifest Manifest { get; } = CreateManifest(
        new Guid("5211b265-238c-4f0b-98ea-c31aff35d73f"),
        "Discord: Stream Toggle",
        "Toque liga e desliga a transmissão de tela na call do Discord.");

    protected override ButtonVisual Preview => new(Glyphs.Share, "Transmitir");

    protected override DiscordButtonInstance CreateInstance(IWidgetContext context) => new MediaToggleInstance(context, video: false);
}

public sealed class MediaToggleInstance(IWidgetContext context, bool video) : DiscordButtonInstance(context)
{
    protected override string Glyph => video ? Glyphs.Video : Glyphs.Share;
    protected override string Label => video ? "Câmera" : "Transmitir";

    protected override ButtonVisual GetVisual()
    {
        if (Session.VoiceChannel is null)
            return new ButtonVisual(Glyph, Label) { Detail = "Entre numa call", Dimmed = true };
        var active = video ? Session.VideoActive : Session.ScreenshareActive;
        return active switch
        {
            true => new ButtonVisual(Glyph, video ? "Câmera ligada" : "Transmitindo") { Accent = ButtonRenderer.Success },
            false => new ButtonVisual(video ? Glyphs.VideoOff : Glyph, video ? "Câmera desligada" : "Sem transmissão"),
            null => new ButtonVisual(Glyph, Label) { Detail = "Toque para alternar" },
        };
    }

    protected override async Task OnReadyTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        if (touch.Kind != TouchKind.Tap || Session.VoiceChannel is null)
            return;
        if (video)
            await Session.ToggleVideoAsync(cancellationToken).ConfigureAwait(false);
        else
            await Session.ToggleScreenshareAsync(cancellationToken).ConfigureAwait(false);
    }
}
