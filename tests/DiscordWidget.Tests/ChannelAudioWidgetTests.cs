using System.Text.Json.Nodes;
using OpenWigi.Sdk;
using WigiDash.DiscordWidget.Discord;
using WigiDash.DiscordWidget.Rendering;
using WigiDash.DiscordWidget.Widgets;

namespace DiscordWidget.Tests;

/// <summary>Canais, dispositivos e volumes contra um Discord simulado com estado de call.</summary>
[Collection(nameof(SessionOverrideCollection))]
public sealed class ChannelAudioWidgetTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "discordwidget-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeDiscord _discord = new();
    private DiscordSession _session = null!;

    private string? _channelId;
    private string? _textChannelId;
    private string _inputDevice = "default";
    private double _outputVolume = 100;
    private double _friendVolume = 100;
    private bool _friendMuted;

    private JsonNode Voice()
    {
        var json = Json.VoiceSettings();
        json["input"]!["device_id"] = _inputDevice;
        json["output"]!["volume"] = _outputVolume;
        return json;
    }

    private JsonNode? SelectedChannel() => _channelId is null ? null : Channel(_channelId);

    /// <summary>O canal c1 com o Amigo e você, esteja você nele ou não (GET_CHANNEL).</summary>
    private JsonNode Channel(string id) => JsonNode.Parse($$"""
            {
              "id": "{{id}}", "name": "Geral", "guild_id": "g1",
              "voice_states": [
                { "nick": null, "mute": {{(_friendMuted ? "true" : "false")}}, "volume": {{_friendVolume}},
                  "voice_state": { "self_mute": false, "self_deaf": false },
                  "user": { "id": "u2", "username": "amigo", "global_name": "Amigo", "avatar": null } },
                { "mute": false, "volume": 100, "voice_state": { "self_mute": true },
                  "user": { "id": "u1", "username": "tester" } }
              ]
            }
            """)!;

    public Task InitializeAsync()
    {
        _discord.Handler = (cmd, args) =>
        {
            switch (cmd)
            {
                case "SET_VOICE_SETTINGS":
                    if (args["input"]?["device_id"] is { } device) _inputDevice = (string)device!;
                    if (args["output"]?["volume"] is { } volume) _outputVolume = (double)volume;
                    return Voice();
                case "GET_VOICE_SETTINGS":
                    return Voice();
                case "GET_SELECTED_VOICE_CHANNEL":
                    return SelectedChannel();
                case "GET_CHANNEL":
                    return Channel((string)args["channel_id"]!);
                case "SELECT_VOICE_CHANNEL":
                    _channelId = (string?)args["channel_id"];
                    return SelectedChannel();
                case "SELECT_TEXT_CHANNEL" when (string?)args["channel_id"] == "bad":
                    throw new FakeError(4005, "Invalid channel");
                case "SELECT_TEXT_CHANNEL":
                    _textChannelId = (string?)args["channel_id"];
                    return new JsonObject();
                case "SET_USER_VOICE_SETTINGS":
                    if (args["volume"] is { } v) _friendVolume = (double)v;
                    if (args["mute"] is { } m) _friendMuted = (bool)m;
                    return new JsonObject();
                case "GET_GUILDS":
                    return JsonNode.Parse("""{ "guilds": [ { "id": "g1", "name": "Servidor" } ] }""");
                case "GET_CHANNELS":
                    return JsonNode.Parse("""
                        { "channels": [ { "id": "c1", "name": "Geral", "type": 2 }, { "id": "t1", "name": "chat", "type": 0 } ] }
                        """);
                default:
                    return new JsonObject();
            }
        };
        var store = new AccountStore(Path.Combine(_dir, "account.json"));
        store.SaveToken(new DiscordToken("access", "refresh", DateTimeOffset.UtcNow.AddDays(1), "rpc"));
        _session = new DiscordSession(store, TimeProvider.System, _discord.Prefix);
        DiscordButtonInstance.SessionOverride = _session;
        return Task.CompletedTask;
    }

    private async Task<T> CreateAsync<T>(DiscordWidgetBase widget, WidgetSize? size = null) where T : DiscordButtonInstance
    {
        var instance = (T)await widget.CreateInstanceAsync(new FakeContext(size ?? WidgetSize.Small), CancellationToken.None);
        await FakeDiscord.WaitUntilAsync(() => _session.State == DiscordState.Ready);
        return instance;
    }

    private static ValueTask Tap(IWidgetInstance instance, int x = 10) =>
        instance.OnTouchAsync(new TouchEvent(TouchKind.Tap, x, 10, TimeSpan.Zero), CancellationToken.None);

    [Fact]
    public async Task Servidores_e_canais_vem_do_Discord()
    {
        await using var widget = await CreateAsync<ChannelInstance>(new VoiceChannelWidget());

        var guilds = await _session.GetGuildsAsync();
        var channels = await _session.GetChannelsAsync("g1");

        Assert.Equal("Servidor", Assert.Single(guilds).Name);
        Assert.Equal(["Geral"], channels.Where(c => c.IsVoice).Select(c => c.Name));
        Assert.Equal(["chat"], channels.Where(c => c.IsText).Select(c => c.Name));
    }

    [Fact]
    public async Task Voice_Channel_mostra_os_avatares_antes_de_entrar_e_fica_verde_dentro()
    {
        await using var widget = await CreateAsync<ChannelInstance>(new VoiceChannelWidget());
        Assert.Equal("Escolha o canal", widget.CurrentVisual().Detail);
        await widget.SaveOptionsAsync(new ChannelOptions { GuildId = "g1", GuildName = "Servidor", ChannelId = "c1", ChannelName = "Geral" });

        // Fora da call: o canal é acompanhado (GET_CHANNEL + VOICE_STATE_* do c1) e mostra quem está lá.
        await FakeDiscord.WaitUntilAsync(() => widget.CurrentVisual().Badge == 2);
        Assert.Equal(ButtonRenderer.DiscordBlurple, widget.CurrentVisual().Accent);
        Assert.Equal(["Amigo", "tester"], widget.CurrentVisual().Faces!.Select(f => f.Name));
        Assert.EndsWith("/embed/avatars/" + ((2UL >> 22) % 6) + ".png", widget.CurrentVisual().Faces![0].Url);

        await Tap(widget);
        Assert.Equal("c1", _channelId);
        await FakeDiscord.WaitUntilAsync(() => widget.CurrentVisual().Accent == ButtonRenderer.Success);

        await Tap(widget);
        Assert.Null(_channelId);
        await FakeDiscord.WaitUntilAsync(() => _session.VoiceChannel is null);
    }

    [Fact]
    public async Task Quem_esta_falando_ganha_o_anel()
    {
        await using var widget = await CreateAsync<ChannelInstance>(new VoiceChannelWidget());
        await widget.SaveOptionsAsync(new ChannelOptions { GuildId = "g1", ChannelId = "c1", ChannelName = "Geral" });
        await FakeDiscord.WaitUntilAsync(() => widget.CurrentVisual().Badge == 2);
        await FakeDiscord.WaitUntilAsync(() => _discord.Commands.Any(c =>
            (string?)c["cmd"] == "SUBSCRIBE" && (string?)c["evt"] == "SPEAKING_START" && (string?)c["args"]!["channel_id"] == "c1"));

        await _discord.SendEventAsync("SPEAKING_START", new JsonObject { ["user_id"] = "u2", ["channel_id"] = "c1" });
        await FakeDiscord.WaitUntilAsync(() => widget.CurrentVisual().Faces![0].Speaking);

        await _discord.SendEventAsync("SPEAKING_STOP", new JsonObject { ["user_id"] = "u2", ["channel_id"] = "c1" });
        await FakeDiscord.WaitUntilAsync(() => !widget.CurrentVisual().Faces![0].Speaking);
    }

    [Fact]
    public async Task Tirar_o_widget_para_de_acompanhar_o_canal()
    {
        var widget = await CreateAsync<ChannelInstance>(new VoiceChannelWidget());
        await widget.SaveOptionsAsync(new ChannelOptions { GuildId = "g1", ChannelId = "c1", ChannelName = "Geral" });
        await FakeDiscord.WaitUntilAsync(() => _session.GetChannel("c1") is not null);
        await using var other = await CreateAsync<MuteInstance>(new MuteWidget());

        await widget.DisposeAsync();

        await FakeDiscord.WaitUntilAsync(() => _discord.Commands.Any(c =>
            (string?)c["cmd"] == "UNSUBSCRIBE" && (string?)c["evt"] == "VOICE_STATE_UPDATE" && (string?)c["args"]!["channel_id"] == "c1"));
        await FakeDiscord.WaitUntilAsync(() => _session.GetChannel("c1") is null);
    }

    [Fact]
    public async Task Entrar_numa_call_assina_os_eventos_de_voz_daquele_canal()
    {
        _channelId = "c1";
        await using var widget = await CreateAsync<ChannelInstance>(new VoiceChannelWidget());

        await FakeDiscord.WaitUntilAsync(() => _discord.Commands.Any(c =>
            (string?)c["cmd"] == "SUBSCRIBE" && (string?)c["evt"] == "VOICE_STATE_UPDATE" && (string?)c["args"]!["channel_id"] == "c1"));
    }

    [Fact]
    public async Task Text_Channel_abre_o_canal()
    {
        await using var widget = await CreateAsync<ChannelInstance>(new TextChannelWidget());
        await widget.SaveOptionsAsync(new ChannelOptions { GuildId = "g1", ChannelId = "t1", ChannelName = "chat" });

        await Tap(widget);

        Assert.Equal("t1", _textChannelId);
        Assert.Equal("#chat", widget.CurrentVisual().Label);
        // Retorno do toque só depois que o Discord confirmou.
        Assert.Equal(ButtonRenderer.Success, widget.CurrentVisual().Accent);
        Assert.NotNull(widget.CurrentVisual().Level);
    }

    [Fact]
    public async Task Comando_recusado_pelo_Discord_fica_vermelho_com_Falhou()
    {
        await using var widget = await CreateAsync<ChannelInstance>(new TextChannelWidget());
        await widget.SaveOptionsAsync(new ChannelOptions { GuildId = "g1", ChannelId = "bad", ChannelName = "secreto" });

        await Tap(widget);

        Assert.Equal(ButtonRenderer.Danger, widget.CurrentVisual().Accent);
        Assert.Equal("Falhou", widget.CurrentVisual().Detail);
    }

    [Fact]
    public async Task Set_Audio_Device_troca_a_entrada_e_marca_em_uso()
    {
        await using var widget = await CreateAsync<SetAudioDeviceInstance>(new SetAudioDeviceWidget());
        await widget.SaveOptionsAsync(new AudioDeviceOptions { Side = AudioSide.Input, InputId = "mic2", InputName = "Shure" });
        Assert.Equal("Toque para usar", widget.CurrentVisual().Detail);

        await Tap(widget);

        Assert.Equal("mic2", _inputDevice);
        Assert.Equal("Em uso", widget.CurrentVisual().Detail);
    }

    [Fact]
    public async Task Volume_no_2x1_esquerda_abaixa_e_direita_aumenta()
    {
        await using var widget = await CreateAsync<VolumeControlInstance>(new VolumeControlWidget(), WidgetSize.Wide);
        await widget.SaveOptionsAsync(new VolumeOptions { Side = AudioSide.Output, Step = 10 });

        await Tap(widget, x: 10);
        Assert.Equal(90, _outputVolume);

        await Tap(widget, x: WidgetSize.Wide.PixelSize.Width - 10);
        await Tap(widget, x: WidgetSize.Wide.PixelSize.Width - 10);
        Assert.Equal(110, _outputVolume);
        Assert.Equal("110%", widget.CurrentVisual().Label);
    }

    [Fact]
    public async Task Volume_definir_usa_o_valor_e_respeita_o_limite()
    {
        await using var widget = await CreateAsync<VolumeControlInstance>(new VolumeControlWidget());
        await widget.SaveOptionsAsync(new VolumeOptions { Side = AudioSide.Output, Action = VolumeAction.Set, Value = 250 });

        await Tap(widget);

        Assert.Equal(200, _outputVolume);
    }

    [Fact]
    public async Task User_Volume_ajusta_e_muta_a_pessoa_da_call()
    {
        _channelId = "c1";
        await using var widget = await CreateAsync<UserVolumeInstance>(new UserVolumeWidget());
        await FakeDiscord.WaitUntilAsync(() => _session.VoiceChannel is not null);
        await widget.SaveOptionsAsync(new UserVolumeOptions { UserId = "u2", UserName = "Amigo", Step = -20 });
        Assert.Equal("Amigo", widget.CurrentVisual().Label);

        await Tap(widget);
        Assert.Equal(80, _friendVolume);
        await FakeDiscord.WaitUntilAsync(() => widget.CurrentVisual().Detail == "80%");

        await widget.SaveOptionsAsync(widget.Options with { Action = VolumeAction.ToggleMute });
        await Tap(widget);
        Assert.True(_friendMuted);
        await FakeDiscord.WaitUntilAsync(() => widget.CurrentVisual().Detail == "Mutado para você");
    }

    [Fact]
    public async Task User_Volume_fora_da_call_fica_apagado()
    {
        await using var widget = await CreateAsync<UserVolumeInstance>(new UserVolumeWidget());
        await widget.SaveOptionsAsync(new UserVolumeOptions { UserId = "u2", UserName = "Amigo" });

        Assert.Equal("Fora da call", widget.CurrentVisual().Detail);
        Assert.True(widget.CurrentVisual().Dimmed);
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
