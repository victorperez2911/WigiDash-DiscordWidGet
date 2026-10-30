using System.Text.Json.Nodes;
using OpenWigi.Sdk;
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

    private bool IsInChannel => IsVoice && Options.ChannelId is { } id && Session.VoiceChannel?.Id == id;

    protected override ButtonVisual GetVisual()
    {
        if (Options.ChannelId is null)
            return new ButtonVisual(Glyph, Label) { Detail = "Escolha o canal", Dimmed = true };

        var name = IsVoice ? Options.ChannelName ?? "?" : "#" + Options.ChannelName;
        if (!IsVoice)
            return new ButtonVisual(Glyph, name) { Detail = Options.GuildName };

        if (IsInChannel)
        {
            var count = Session.VoiceChannel!.Users.Count;
            return new ButtonVisual(Glyphs.Phone, name)
            {
                Accent = ButtonRenderer.Success,
                Detail = count == 1 ? "1 pessoa na call" : $"{count} pessoas na call",
            };
        }
        return new ButtonVisual(Glyphs.Phone, name) { Detail = Options.GuildName };
    }

    protected override async Task OnReadyTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        if (touch.Kind != TouchKind.Tap || Options.ChannelId is not { } channelId)
            return;
        if (!IsVoice)
        {
            await Session.SendAsync("SELECT_TEXT_CHANNEL", new JsonObject { ["channel_id"] = channelId }, cancellationToken)
                .ConfigureAwait(false);
            return;
        }
        await Session.SelectVoiceChannelAsync(IsInChannel ? null : channelId, cancellationToken).ConfigureAwait(false);
    }
}
