using System.Text.Json.Nodes;
using OpenWigi.Sdk;
using WigiDash.DiscordWidget.Discord;
using WigiDash.DiscordWidget.Rendering;
using WigiDash.DiscordWidget.Settings;
using WigiDash.DiscordWidget.Widgets;

[assembly: OpenWigiWidget(typeof(SetAudioDeviceWidget))]
[assembly: OpenWigiWidget(typeof(VolumeControlWidget))]
[assembly: OpenWigiWidget(typeof(UserVolumeWidget))]

namespace WigiDash.DiscordWidget.Widgets;

public enum AudioSide
{
    Input,
    Output,
    Both,
}

public enum VolumeAction
{
    /// <summary>Soma o passo (negativo abaixa). No 2x1, metade esquerda abaixa e direita aumenta.</summary>
    Adjust,
    /// <summary>Define um valor fixo.</summary>
    Set,
    ToggleMute,
    Mute,
    Unmute,
}

internal static class VolumeTouch
{
    /// <summary>
    /// Quanto o toque soma ao volume: metade esquerda abaixa e a direita aumenta (o tile desenha o "−" e o "+"
    /// dos lados do ícone); swipe para cima/baixo também.
    /// </summary>
    public static int? Delta(TouchEvent touch, PixelSize size, int step)
    {
        var magnitude = Math.Abs(step);
        return touch.Kind switch
        {
            TouchKind.SwipeUp => magnitude,
            TouchKind.SwipeDown => -magnitude,
            TouchKind.Tap => touch.X < size.Width / 2 ? -magnitude : magnitude,
            _ => null,
        };
    }

    /// <summary>"−" e "+" dos lados do ícone, em qualquer tamanho.</summary>
    public static AdjustHints Hints => AdjustHints.Horizontal;
}

public sealed record AudioDeviceOptions
{
    public AudioSide Side { get; init; } = AudioSide.Input;
    public string? InputId { get; init; }
    public string? InputName { get; init; }
    public string? OutputId { get; init; }
    public string? OutputName { get; init; }
}

/// <summary>Troca o microfone e/ou a saída do Discord (SET_VOICE_SETTINGS input/output device_id).</summary>
public sealed class SetAudioDeviceWidget : DiscordWidgetBase
{
    public override WidgetManifest Manifest { get; } = CreateManifest(
        new Guid("127b7877-9b6f-4500-a376-ba4a38b6a9ee"),
        "Discord: Set Audio Device",
        "Toque troca o dispositivo de entrada e/ou saída do Discord para o escolhido. Fica azul quando já está nele.");

    protected override ButtonVisual Preview => new(Glyphs.Speakers, "Audio Device");

    protected override DiscordButtonInstance CreateInstance(IWidgetContext context) => new SetAudioDeviceInstance(context);
}

public sealed class SetAudioDeviceInstance(IWidgetContext context) : DiscordOptionsInstance<AudioDeviceOptions>(context)
{
    protected override string Glyph => Options.Side == AudioSide.Input ? Glyphs.Microphone : Glyphs.Speakers;
    protected override string Label => "Audio Device";

    public override Type? OptionsComponent => typeof(AudioDeviceOptionsPanel);

    private bool WantsInput => Options.Side is AudioSide.Input or AudioSide.Both && Options.InputId is not null;
    private bool WantsOutput => Options.Side is AudioSide.Output or AudioSide.Both && Options.OutputId is not null;

    protected override ButtonVisual GetVisual()
    {
        if (!WantsInput && !WantsOutput)
            return new ButtonVisual(Glyph, Label) { Detail = "Escolha o dispositivo", Dimmed = true };

        var voice = Session.Voice;
        var active = (!WantsInput || voice?.Input.DeviceId == Options.InputId)
                     && (!WantsOutput || voice?.Output.DeviceId == Options.OutputId);
        var name = Options.Side switch
        {
            AudioSide.Input => Options.InputName,
            AudioSide.Output => Options.OutputName,
            _ => $"{Options.InputName} / {Options.OutputName}",
        };
        return new ButtonVisual(Glyph, name ?? Label)
        {
            Accent = active ? ButtonRenderer.Success : null,
            Detail = active ? "Em uso" : "Toque para usar",
        };
    }

    protected override async Task OnReadyTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        if (touch.Kind != TouchKind.Tap || (!WantsInput && !WantsOutput))
            return;
        var args = new JsonObject();
        if (WantsInput)
            args["input"] = new JsonObject { ["device_id"] = Options.InputId };
        if (WantsOutput)
            args["output"] = new JsonObject { ["device_id"] = Options.OutputId };
        await ConfirmAsync(() => Session.SetVoiceSettingsAsync(args, cancellationToken)).ConfigureAwait(false);
    }
}

public sealed record VolumeOptions
{
    /// <summary>Entrada (0-100) ou saída (0-200). <see cref="AudioSide.Both"/> não se aplica.</summary>
    public AudioSide Side { get; init; } = AudioSide.Output;
    public VolumeAction Action { get; init; } = VolumeAction.Adjust;
    public int Step { get; init; } = 10;
    public int Value { get; init; } = 100;

    /// <summary>Deixa a saída passar de 100% (o Discord vai até 200%). Desligado, para em 100%.</summary>
    public bool AllowBoost { get; init; }
}

/// <summary>Volume de entrada ou saída do Discord (SET_VOICE_SETTINGS volume).</summary>
public sealed class VolumeControlWidget : DiscordWidgetBase
{
    public override WidgetManifest Manifest { get; } = CreateManifest(
        new Guid("a05f82ab-8865-4fa9-92c6-12a75e0d9f4a"),
        "Discord: Volume Control",
        "Ajusta o volume do microfone ou da saída do Discord. No 2x1, esquerda abaixa e direita aumenta; swipe também.");

    protected override ButtonVisual Preview => new(Glyphs.Volume, "Volume") { Level = 0.5 };

    protected override DiscordButtonInstance CreateInstance(IWidgetContext context) => new VolumeControlInstance(context);
}

public sealed class VolumeControlInstance(IWidgetContext context) : DiscordOptionsInstance<VolumeOptions>(context)
{
    private bool IsInput => Options.Side == AudioSide.Input;
    private int Max => IsInput || !Options.AllowBoost ? 100 : 200;

    protected override string Glyph => IsInput ? Glyphs.Microphone : Glyphs.Volume;
    protected override string Label => IsInput ? "Volume do microfone" : "Volume de saída";

    public override Type? OptionsComponent => typeof(VolumeOptionsPanel);

    protected override ButtonVisual GetVisual()
    {
        var volume = (IsInput ? Session.Voice?.Input.Volume : Session.Voice?.Output.Volume) ?? 0;
        return new ButtonVisual(Glyph, $"{Math.Round(volume)}%")
        {
            Detail = Options.Action == VolumeAction.Set ? $"{Label}: {Options.Value}%" : Label,
            Level = volume / Max,
            Hints = Options.Action == VolumeAction.Set ? AdjustHints.None : VolumeTouch.Hints,
        };
    }

    protected override async Task OnReadyTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        if (Session.Voice is not { } voice)
            return;
        var current = IsInput ? voice.Input.Volume : voice.Output.Volume;
        double target;
        if (Options.Action == VolumeAction.Set && touch.Kind == TouchKind.Tap)
            target = Options.Value;
        else if (VolumeTouch.Delta(touch, Context.PixelSize, Options.Action == VolumeAction.Set ? 10 : Options.Step) is { } delta)
            target = current + delta;
        else
            return;

        // Acima do limite (ex.: estava em 150% antes de desligar o "até 200%"), só deixa abaixar.
        target = Math.Clamp(target, 0, Math.Max(Max, Math.Min(current, target)));
        var key = IsInput ? "input" : "output";
        await Session.SetVoiceSettingsAsync(new JsonObject { [key] = new JsonObject { ["volume"] = target } }, cancellationToken)
            .ConfigureAwait(false);
    }
}

public sealed record UserVolumeOptions
{
    public string? UserId { get; init; }
    public string? UserName { get; init; }
    public VolumeAction Action { get; init; } = VolumeAction.Adjust;
    public int Step { get; init; } = 10;
    public int Value { get; init; } = 100;
}

/// <summary>Volume e mute local de uma pessoa da call (SET_USER_VOICE_SETTINGS).</summary>
public sealed class UserVolumeWidget : DiscordWidgetBase
{
    public override WidgetManifest Manifest { get; } = CreateManifest(
        new Guid("40bfc526-55df-4e0b-ae27-bc14a0cab821"),
        "Discord: User Volume Control",
        "Ajusta o volume ou muta, só para você, uma pessoa que está na call.");

    protected override ButtonVisual Preview => new(Glyphs.People, "User Volume") { Level = 0.5 };

    protected override DiscordButtonInstance CreateInstance(IWidgetContext context) => new UserVolumeInstance(context);
}

public sealed class UserVolumeInstance(IWidgetContext context) : DiscordOptionsInstance<UserVolumeOptions>(context)
{
    protected override string Glyph => Glyphs.People;
    protected override string Label => "User Volume";

    public override Type? OptionsComponent => typeof(UserVolumeOptionsPanel);

    private VoiceUser? User => Options.UserId is { } id ? Session.VoiceChannel?.Users.FirstOrDefault(u => u.Id == id) : null;

    protected override ButtonVisual GetVisual()
    {
        if (Options.UserId is null)
            return new ButtonVisual(Glyph, Label) { Detail = "Escolha a pessoa", Dimmed = true };
        if (User is not { } user)
            return new ButtonVisual(Glyph, Options.UserName ?? Label) { Detail = "Fora da call", Dimmed = true };
        if (user.LocallyMuted)
            return new ButtonVisual(Glyphs.Mute, user.Name) { Accent = ButtonRenderer.Danger, Detail = "Mutado para você" };
        return new ButtonVisual(Glyph, user.Name)
        {
            Detail = $"{Math.Round(user.Volume)}%",
            Level = user.Volume / 200,
            Hints = Options.Action == VolumeAction.Adjust ? VolumeTouch.Hints : AdjustHints.None,
            Faces = [new AvatarFace(user.AvatarUrl, user.Name, Session.IsSpeaking(user.Id))],
        };
    }

    protected override async Task OnReadyTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        if (User is not { } user)
            return;
        switch (Options.Action)
        {
            case VolumeAction.ToggleMute or VolumeAction.Mute or VolumeAction.Unmute when touch.Kind == TouchKind.Tap:
                var mute = Options.Action switch
                {
                    VolumeAction.Mute => true,
                    VolumeAction.Unmute => false,
                    _ => !user.LocallyMuted,
                };
                await Session.SetUserVoiceSettingsAsync(user.Id, null, mute, cancellationToken).ConfigureAwait(false);
                break;
            case VolumeAction.Set when touch.Kind == TouchKind.Tap:
                await Session.SetUserVoiceSettingsAsync(user.Id, Options.Value, null, cancellationToken).ConfigureAwait(false);
                break;
            default:
                var step = Options.Action == VolumeAction.Adjust ? Options.Step : 10;
                if (VolumeTouch.Delta(touch, Context.PixelSize, step) is { } delta)
                    await Session.SetUserVoiceSettingsAsync(user.Id, user.Volume + delta, null, cancellationToken).ConfigureAwait(false);
                break;
        }
    }
}
