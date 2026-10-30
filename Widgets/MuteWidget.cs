using System.Text.Json.Nodes;
using OpenWigi.Sdk;
using WigiDash.DiscordWidget.Rendering;
using WigiDash.DiscordWidget.Widgets;

[assembly: OpenWigiWidget(typeof(MuteWidget))]

namespace WigiDash.DiscordWidget.Widgets;

/// <summary>Liga e desliga o microfone no Discord (SET_VOICE_SETTINGS mute).</summary>
public sealed class MuteWidget : DiscordWidgetBase
{
    public override WidgetManifest Manifest { get; } = CreateManifest(
        new Guid("c0488238-0f38-442d-945e-2df310e0f93a"),
        "Discord: Mute",
        "Toque liga e desliga o microfone no Discord. Fica vermelho quando está mutado.");

    protected override ButtonVisual Preview => new(Glyphs.Microphone, "Mute");

    protected override DiscordButtonInstance CreateInstance(IWidgetContext context) => new MuteInstance(context);
}

public sealed class MuteInstance(IWidgetContext context) : DiscordButtonInstance(context)
{
    protected override string Glyph => Glyphs.Microphone;
    protected override string Label => "Mute";

    protected override ButtonVisual GetVisual() => Session.Voice is { Mute: true }
        ? new ButtonVisual(Glyphs.MicrophoneOff, "Mutado") { Accent = ButtonRenderer.Danger }
        : new ButtonVisual(Glyphs.Microphone, "Microfone ligado");

    protected override async Task OnReadyTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        if (touch.Kind != TouchKind.Tap || Session.Voice is not { } voice)
            return;
        await Session.SetVoiceSettingsAsync(new JsonObject { ["mute"] = !voice.Mute }, cancellationToken).ConfigureAwait(false);
    }
}
