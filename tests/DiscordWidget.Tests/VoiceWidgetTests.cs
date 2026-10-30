using System.Text.Json.Nodes;
using OpenWigi.Sdk;
using WigiDash.DiscordWidget.Discord;
using WigiDash.DiscordWidget.Widgets;

namespace DiscordWidget.Tests;

/// <summary>
/// Widgets de voz contra o Discord simulado. Ficam numa coleção só porque trocam a sessão global
/// (<see cref="DiscordButtonInstance.SessionOverride"/>).
/// </summary>
[Collection(nameof(SessionOverrideCollection))]
public sealed class VoiceWidgetTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "discordwidget-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeDiscord _discord = new();
    private DiscordSession _session = null!;
    private VoiceState _voice = new();

    private sealed class VoiceState
    {
        public bool Mute;
        public bool Deaf;
        public string Mode = "VOICE_ACTIVITY";

        public JsonNode ToJson()
        {
            var json = Json.VoiceSettings(Mute, Deaf);
            json["mode"]!["type"] = Mode;
            return json;
        }
    }

    public async Task InitializeAsync()
    {
        _discord.Handler = (cmd, args) =>
        {
            if (cmd == "SET_VOICE_SETTINGS")
            {
                if (args["mute"] is { } mute) _voice.Mute = (bool)mute;
                if (args["deaf"] is { } deaf) _voice.Deaf = (bool)deaf;
                if (args["mode"]?["type"] is { } mode) _voice.Mode = (string)mode!;
            }
            return cmd is "GET_VOICE_SETTINGS" or "SET_VOICE_SETTINGS" ? _voice.ToJson() : new JsonObject();
        };
        var store = new AccountStore(Path.Combine(_dir, "account.json"));
        store.SaveToken(new DiscordToken("access", "refresh", DateTimeOffset.UtcNow.AddDays(1), "rpc"));
        _session = new DiscordSession(store, TimeProvider.System, _discord.Prefix);
        DiscordButtonInstance.SessionOverride = _session;
        await Task.CompletedTask;
    }

    private async Task<T> CreateAsync<T>(DiscordWidgetBase widget) where T : DiscordButtonInstance
    {
        var instance = (T)await widget.CreateInstanceAsync(new FakeContext(WidgetSize.Small), CancellationToken.None);
        await FakeDiscord.WaitUntilAsync(() => _session.State == DiscordState.Ready);
        return instance;
    }

    private static ValueTask Touch(IWidgetInstance instance, TouchKind kind) =>
        instance.OnTouchAsync(new TouchEvent(kind, 10, 10, TimeSpan.Zero), CancellationToken.None);

    [Fact]
    public async Task Mute_alterna_o_microfone_e_fica_vermelho()
    {
        await using var mute = await CreateAsync<MuteInstance>(new MuteWidget());

        await Touch(mute, TouchKind.Tap);

        Assert.True(_voice.Mute);
        Assert.Equal("Mutado", mute.CurrentVisual().Label);
        Assert.NotNull(mute.CurrentVisual().Accent);

        await Touch(mute, TouchKind.Tap);
        Assert.False(_voice.Mute);
    }

    [Fact]
    public async Task Deafen_alterna_o_audio()
    {
        await using var deafen = await CreateAsync<DeafenInstance>(new DeafenWidget());

        await Touch(deafen, TouchKind.Tap);

        Assert.True(_voice.Deaf);
        Assert.Equal("Ensurdecido", deafen.CurrentVisual().Label);
    }

    [Fact]
    public async Task Push_to_talk_abre_ao_encostar_e_muta_ao_soltar()
    {
        _voice.Mute = true;
        await using var ptt = await CreateAsync<PushToInstance>(new PushToTalkWidget());

        await Touch(ptt, TouchKind.PressStarted);
        Assert.False(_voice.Mute);
        Assert.Equal("Falando", ptt.CurrentVisual().Label);

        // O long-press no meio não atrapalha: o que fecha é o PressEnded.
        await Touch(ptt, TouchKind.LongPress);
        Assert.False(_voice.Mute);

        await Touch(ptt, TouchKind.PressEnded);
        Assert.True(_voice.Mute);
    }

    [Fact]
    public async Task Push_to_mute_muta_ao_encostar_e_volta_como_estava()
    {
        await using var ptm = await CreateAsync<PushToInstance>(new PushToMuteWidget());

        await Touch(ptm, TouchKind.PressStarted);
        Assert.True(_voice.Mute);

        await Touch(ptm, TouchKind.PressEnded);
        Assert.False(_voice.Mute);
    }

    [Fact]
    public async Task Voice_input_mode_alterna_entre_deteccao_e_PTT()
    {
        await using var mode = await CreateAsync<VoiceInputModeInstance>(new VoiceInputModeWidget());

        await Touch(mode, TouchKind.Tap);
        Assert.Equal("PUSH_TO_TALK", _voice.Mode);
        Assert.Equal("Push to Talk", mode.CurrentVisual().Label);

        await Touch(mode, TouchKind.Tap);
        Assert.Equal("VOICE_ACTIVITY", _voice.Mode);
    }

    [Fact]
    public async Task Evento_do_Discord_pede_quadro_novo()
    {
        var context = new FakeContext(WidgetSize.Small);
        await using var mute = (MuteInstance)await new MuteWidget().CreateInstanceAsync(context, CancellationToken.None);
        await FakeDiscord.WaitUntilAsync(() => _session.State == DiscordState.Ready);
        var before = context.Renders;

        await _discord.SendEventAsync("VOICE_SETTINGS_UPDATE", Json.VoiceSettings(mute: true));

        await FakeDiscord.WaitUntilAsync(() => context.Renders > before && mute.CurrentVisual().Label == "Mutado");
    }

    public async Task DisposeAsync()
    {
        DiscordButtonInstance.SessionOverride = null;
        await FakeDiscord.WaitUntilAsync(() => _session.State == DiscordState.Stopped);
        await _discord.DisposeAsync();
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}

[CollectionDefinition(nameof(SessionOverrideCollection), DisableParallelization = true)]
public sealed class SessionOverrideCollection;
