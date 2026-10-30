using System.Text.Json.Nodes;
using OpenWigi.Sdk;
using WigiDash.DiscordWidget.Discord;
using WigiDash.DiscordWidget.Rendering;
using WigiDash.DiscordWidget.Widgets;

[assembly: OpenWigiWidget(typeof(VoiceInputModeWidget))]

namespace WigiDash.DiscordWidget.Widgets;

/// <summary>Alterna o modo de entrada do Discord entre detecção de voz e push to talk (SET_VOICE_SETTINGS mode).</summary>
public sealed class VoiceInputModeWidget : DiscordWidgetBase
{
    public override WidgetManifest Manifest { get; } = CreateManifest(
        new Guid("8f9881f9-2671-4acf-a4b1-26d74be5f64c"),
        "Discord: Voice Input Mode",
        "Toque alterna o modo de entrada de voz do Discord entre detecção de voz e push to talk.");

    protected override ButtonVisual Preview => new(Glyphs.Switch, "Modo de voz");

    protected override DiscordButtonInstance CreateInstance(IWidgetContext context) => new VoiceInputModeInstance(context);
}

public sealed class VoiceInputModeInstance(IWidgetContext context) : DiscordButtonInstance(context)
{
    protected override string Glyph => Glyphs.Switch;
    protected override string Label => "Modo de voz";

    protected override ButtonVisual GetVisual() => Session.Voice?.Mode == VoiceMode.PushToTalk
        ? new ButtonVisual(Glyphs.Switch, "Push to Talk") { Accent = ButtonRenderer.Success, Detail = "Modo de voz" }
        : new ButtonVisual(Glyphs.Switch, "Detecção de voz") { Detail = "Modo de voz" };

    protected override async Task OnReadyTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        if (touch.Kind != TouchKind.Tap || Session.Voice is not { } voice)
            return;
        var next = voice.Mode == VoiceMode.PushToTalk ? "VOICE_ACTIVITY" : "PUSH_TO_TALK";
        await Session.SetVoiceSettingsAsync(new JsonObject { ["mode"] = new JsonObject { ["type"] = next } }, cancellationToken)
            .ConfigureAwait(false);
    }
}
