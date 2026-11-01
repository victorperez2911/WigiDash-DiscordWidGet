using System.Text.Json.Nodes;
using OpenWigi.Sdk;
using WigiDash.DiscordWidget.Rendering;
using WigiDash.DiscordWidget.Widgets;

[assembly: OpenWigiWidget(typeof(PushToTalkWidget))]
[assembly: OpenWigiWidget(typeof(PushToMuteWidget))]

namespace WigiDash.DiscordWidget.Widgets;

/// <summary>Segurar no visor abre o microfone; soltar muta de novo.</summary>
public sealed class PushToTalkWidget : DiscordWidgetBase
{
    public override WidgetManifest Manifest { get; } = CreateManifest(
        new Guid("1cae3be2-8320-49ec-baea-3d616df8150e"),
        "Discord: Push to Talk",
        "Enquanto o dedo está no widget o microfone fica aberto; ao soltar, muta. Use com o microfone mutado.",
        iconButton: true);

    protected override ButtonVisual Preview => new(Glyphs.Microphone, "Push to Talk") { GlyphBadge = Glyphs.Touch };

    protected override DiscordButtonInstance CreateInstance(IWidgetContext context) => new PushToInstance(context, talk: true);
}

/// <summary>Segurar no visor muta; soltar volta ao estado de antes.</summary>
public sealed class PushToMuteWidget : DiscordWidgetBase
{
    public override WidgetManifest Manifest { get; } = CreateManifest(
        new Guid("0945c7cd-43de-4c5d-888a-21d0c775694c"),
        "Discord: Push to Mute",
        "Enquanto o dedo está no widget o microfone fica mutado; ao soltar, volta como estava.",
        iconButton: true);

    protected override ButtonVisual Preview => new(Glyphs.MicrophoneOff, "Push to Mute") { GlyphBadge = Glyphs.Touch };

    protected override DiscordButtonInstance CreateInstance(IWidgetContext context) => new PushToInstance(context, talk: false);
}

/// <summary>
/// Push to Talk (<paramref name="talk"/> = true) e Push to Mute. Usa o PressStarted/PressEnded do SDK, que
/// chegam mesmo quando o toque vira long-press ou swipe.
/// </summary>
public sealed class PushToInstance(IWidgetContext context, bool talk) : DiscordButtonInstance(context)
{
    private bool _held;
    private bool _muteBefore;

    protected override string Glyph => talk ? Glyphs.Microphone : Glyphs.MicrophoneOff;
    protected override string Label => talk ? "Push to Talk" : "Push to Mute";

    protected override ButtonVisual GetVisual()
    {
        if (_held)
            return talk
                ? new ButtonVisual(Glyphs.Microphone, "Falando") { Accent = ButtonRenderer.Success, GlyphBadge = Glyphs.Touch }
                : new ButtonVisual(Glyphs.MicrophoneOff, "Mutado") { Accent = ButtonRenderer.Danger, GlyphBadge = Glyphs.Touch };
        var muted = Session.Voice?.Mute ?? false;
        return new ButtonVisual(Glyph, talk ? "Segure para falar" : "Segure para mutar")
        {
            GlyphBadge = Glyphs.Touch,
            Detail = muted ? "Microfone mutado" : "Microfone ligado",
        };
    }

    protected override async Task OnReadyTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        switch (touch.Kind)
        {
            case TouchKind.PressStarted when !_held:
                _held = true;
                _muteBefore = Session.Voice?.Mute ?? talk;
                Refresh();
                await Session.SetVoiceSettingsAsync(new JsonObject { ["mute"] = !talk }, cancellationToken).ConfigureAwait(false);
                break;
            case TouchKind.PressEnded when _held:
                _held = false;
                Refresh();
                // Push to Talk sempre volta mutado; Push to Mute volta como estava antes do toque.
                await Session.SetVoiceSettingsAsync(new JsonObject { ["mute"] = talk || _muteBefore }, cancellationToken)
                    .ConfigureAwait(false);
                break;
        }
    }
}
