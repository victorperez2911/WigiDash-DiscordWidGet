using System.Text.Json.Nodes;
using OpenWigi.Sdk;
using WigiDash.DiscordWidget.Rendering;
using WigiDash.DiscordWidget.Widgets;

[assembly: OpenWigiWidget(typeof(DeafenWidget))]

namespace WigiDash.DiscordWidget.Widgets;

/// <summary>Liga e desliga o áudio de saída no Discord (SET_VOICE_SETTINGS deaf). Ensurdecer também muta.</summary>
public sealed class DeafenWidget : DiscordWidgetBase
{
    public override WidgetManifest Manifest { get; } = CreateManifest(
        new Guid("82647130-6083-4199-8391-ad5cfe62fbd7"),
        "Discord: Deafen",
        "Toque liga e desliga o áudio do Discord (ensurdecer). Fica vermelho quando está ensurdecido.");

    protected override ButtonVisual Preview => new(Glyphs.Headphone, "Deafen");

    protected override DiscordButtonInstance CreateInstance(IWidgetContext context) => new DeafenInstance(context);
}

public sealed class DeafenInstance(IWidgetContext context) : DiscordButtonInstance(context)
{
    protected override string Glyph => Glyphs.Headphone;
    protected override string Label => "Deafen";

    protected override ButtonVisual GetVisual() => Session.Voice is { Deaf: true }
        ? new ButtonVisual(Glyphs.Headphone, "Ensurdecido") { Accent = ButtonRenderer.Danger, Slashed = true }
        : new ButtonVisual(Glyphs.Headphone, "Áudio ligado");

    protected override async Task OnReadyTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        if (touch.Kind != TouchKind.Tap || Session.Voice is not { } voice)
            return;
        await Session.SetVoiceSettingsAsync(new JsonObject { ["deaf"] = !voice.Deaf }, cancellationToken).ConfigureAwait(false);
    }
}
