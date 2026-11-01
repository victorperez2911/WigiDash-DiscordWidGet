using System.Text.Json.Nodes;
using OpenWigi.Sdk;
using WigiDash.DiscordWidget.Rendering;
using WigiDash.DiscordWidget.Widgets;

[assembly: OpenWigiWidget(typeof(SmartMuteWidget))]

namespace WigiDash.DiscordWidget.Widgets;

/// <summary>
/// Mute e push to talk/mute num tile só (ideia do Victor, nem o plugin da Elgato tem): toque rápido alterna o
/// mute e fica; segurar inverte o estado só enquanto o dedo está na tela.
/// </summary>
public sealed class SmartMuteWidget : DiscordWidgetBase
{
    public override WidgetManifest Manifest { get; } = CreateManifest(
        new Guid("248cba1d-a161-4b9a-9f6d-9f814a897a37"),
        "Discord: Mute inteligente",
        "Toque alterna o mute. Segurar inverte só enquanto o dedo está na tela: falando, segurar muta; mutado, segurar abre o microfone.",
        iconButton: true);

    protected override ButtonVisual Preview => new(Glyphs.Microphone, "Mute inteligente") { GlyphBadge = Glyphs.Touch };

    protected override DiscordButtonInstance CreateInstance(IWidgetContext context) => new SmartMuteInstance(context);
}

public sealed class SmartMuteInstance(IWidgetContext context) : DiscordButtonInstance(context)
{
    /// <summary>Estado do mute antes de segurar; <c>null</c> quando não está segurando.</summary>
    private bool? _heldFrom;

    protected override string Glyph => Glyphs.Microphone;
    protected override string Label => "Mute inteligente";

    protected override ButtonVisual GetVisual()
    {
        var muted = Session.Voice?.Mute ?? false;
        var glyph = muted ? Glyphs.MicrophoneOff : Glyphs.Microphone;
        if (_heldFrom is { } before)
        {
            return before
                ? new ButtonVisual(Glyphs.Microphone, "Falando") { Accent = ButtonRenderer.Success, GlyphBadge = Glyphs.Touch, Detail = "Solte para mutar" }
                : new ButtonVisual(Glyphs.MicrophoneOff, "Mutado") { Accent = ButtonRenderer.Danger, GlyphBadge = Glyphs.Touch, Detail = "Solte para falar" };
        }
        return muted
            ? new ButtonVisual(glyph, "Mutado") { Accent = ButtonRenderer.Danger, GlyphBadge = Glyphs.Touch, Detail = "Segure para falar" }
            : new ButtonVisual(glyph, "Microfone ligado") { GlyphBadge = Glyphs.Touch, Detail = "Segure para mutar" };
    }

    protected override async Task OnReadyTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        if (Session.Voice is not { } voice)
            return;
        switch (touch.Kind)
        {
            case TouchKind.Tap when _heldFrom is null:
                await Session.SetVoiceSettingsAsync(new JsonObject { ["mute"] = !voice.Mute }, cancellationToken).ConfigureAwait(false);
                break;
            case TouchKind.LongPress when _heldFrom is null:
                _heldFrom = voice.Mute;
                Refresh();
                await Session.SetVoiceSettingsAsync(new JsonObject { ["mute"] = !voice.Mute }, cancellationToken).ConfigureAwait(false);
                break;
            case TouchKind.PressEnded when _heldFrom is { } before:
                _heldFrom = null;
                Refresh();
                await Session.SetVoiceSettingsAsync(new JsonObject { ["mute"] = before }, cancellationToken).ConfigureAwait(false);
                break;
        }
    }
}
