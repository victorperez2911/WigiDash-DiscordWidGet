using System.Text.Json;
using System.Text.Json.Nodes;
using OpenWigi.Sdk;
using WigiDash.DiscordWidget.Discord;
using WigiDash.DiscordWidget.Widgets;

namespace DiscordWidget.Tests;

/// <summary>Soundboard, Notifications, Video e Stream contra o Discord simulado.</summary>
[Collection(nameof(SessionOverrideCollection))]
public sealed class ExtraWidgetTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "discordwidget-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeDiscord _discord = new();
    private DiscordSession _session = null!;
    private bool _inCall = true;

    public Task InitializeAsync()
    {
        _discord.Handler = (cmd, args) => cmd switch
        {
            "GET_VOICE_SETTINGS" => Json.VoiceSettings(),
            "GET_SELECTED_VOICE_CHANNEL" => _inCall ? JsonNode.Parse("""{ "id": "c1", "name": "Geral", "voice_states": [] }""") : null,
            "GET_SOUNDBOARD_SOUNDS" => JsonNode.Parse("""
                [ { "sound_id": "s1", "name": "Aplausos", "emoji_name": "👏", "guild_id": null, "available": true },
                  { "sound_id": "s2", "name": "Bloqueado", "available": false } ]
                """),
            _ => new JsonObject(),
        };
        var store = new AccountStore(Path.Combine(_dir, "account.json"));
        store.SaveToken(new DiscordToken("access", "refresh", DateTimeOffset.UtcNow.AddDays(1), "rpc"));
        _session = new DiscordSession(store, TimeProvider.System, _discord.Prefix);
        DiscordButtonInstance.SessionOverride = _session;
        return Task.CompletedTask;
    }

    private async Task<T> CreateAsync<T>(DiscordWidgetBase widget) where T : DiscordButtonInstance
    {
        var instance = (T)await widget.CreateInstanceAsync(new FakeContext(WidgetSize.Small), CancellationToken.None);
        await FakeDiscord.WaitUntilAsync(() => _session.State == DiscordState.Ready);
        return instance;
    }

    private static ValueTask Tap(IWidgetInstance instance) =>
        instance.OnTouchAsync(new TouchEvent(TouchKind.Tap, 10, 10, TimeSpan.Zero), CancellationToken.None);

    [Fact]
    public async Task Soundboard_lista_so_os_sons_disponiveis_e_toca_o_escolhido()
    {
        await using var widget = await CreateAsync<SoundboardInstance>(new SoundboardWidget());

        var sound = Assert.Single(await _session.GetSoundboardSoundsAsync());
        Assert.Equal("👏", sound.Emoji);
        await widget.SaveOptionsAsync(new SoundboardOptions { SoundId = sound.Id, SoundName = sound.Name, Emoji = sound.Emoji });
        await Tap(widget);

        var play = await _discord.WaitForCommandAsync("PLAY_SOUNDBOARD_SOUND");
        Assert.Equal("s1", (string?)play["args"]!["sound_id"]);
        Assert.Null(play["args"]!["guild_id"]);
    }

    [Fact]
    public async Task Soundboard_fora_da_call_nao_toca()
    {
        _inCall = false;
        await using var widget = await CreateAsync<SoundboardInstance>(new SoundboardWidget());
        await widget.SaveOptionsAsync(new SoundboardOptions { SoundId = "s1", SoundName = "Aplausos" });

        await Tap(widget);

        Assert.Equal("Entre numa call", widget.CurrentVisual().Detail);
        Assert.DoesNotContain(_discord.Commands, c => (string?)c["cmd"] == "PLAY_SOUNDBOARD_SOUND");
    }

    [Fact]
    public async Task Notifications_conta_passa_e_limpa()
    {
        await using var widget = await CreateAsync<NotificationsInstance>(new NotificationsWidget());
        Assert.Equal("Nenhuma notificação", widget.CurrentVisual().Label);

        await _discord.SendEventAsync("NOTIFICATION_CREATE", JsonNode.Parse("""{ "channel_id": "t1", "title": "Ana", "body": "oi" }""")!);
        await _discord.SendEventAsync("NOTIFICATION_CREATE", JsonNode.Parse("""{ "channel_id": "t2", "title": "Bia", "body": "tudo bem?" }""")!);
        await FakeDiscord.WaitUntilAsync(() => _session.Notifications.Count == 2);
        Assert.Equal("2  Bia", widget.CurrentVisual().Label);

        await Tap(widget);
        var open = await _discord.WaitForCommandAsync("SELECT_TEXT_CHANNEL");
        Assert.Equal("t2", (string?)open["args"]!["channel_id"]);

        await widget.SaveOptionsAsync(new NotificationsOptions { OnPress = NotificationPress.CycleOldest });
        await Tap(widget);
        Assert.Equal("1/2  Ana", widget.CurrentVisual().Label);

        await widget.SaveOptionsAsync(new NotificationsOptions { OnPress = NotificationPress.Clear });
        await Tap(widget);
        Assert.Empty(_session.Notifications);
    }

    [Fact]
    public async Task Video_e_Stream_mandam_o_toggle_e_seguem_o_evento_de_estado()
    {
        await using var video = await CreateAsync<MediaToggleInstance>(new VideoToggleWidget());
        await using var stream = (MediaToggleInstance)await new StreamToggleWidget().CreateInstanceAsync(new FakeContext(WidgetSize.Small), CancellationToken.None);
        await FakeDiscord.WaitUntilAsync(() => _session.VoiceChannel is not null);

        await Tap(video);
        await Tap(stream);
        await _discord.WaitForCommandAsync("TOGGLE_VIDEO");
        await _discord.WaitForCommandAsync("TOGGLE_SCREENSHARE");

        await _discord.SendEventAsync("VIDEO_STATE_UPDATE", new JsonObject { ["active"] = true });
        await FakeDiscord.WaitUntilAsync(() => video.CurrentVisual().Label == "Câmera ligada");
        Assert.Equal("Toque para alternar", stream.CurrentVisual().Detail);
    }

    [Fact]
    public async Task Assina_os_eventos_opcionais_ao_conectar()
    {
        await using var widget = await CreateAsync<NotificationsInstance>(new NotificationsWidget());

        foreach (var evt in new[] { "NOTIFICATION_CREATE", "VIDEO_STATE_UPDATE", "SCREENSHARE_STATE_UPDATE" })
            Assert.Contains(_discord.Commands, c => (string?)c["cmd"] == "SUBSCRIBE" && (string?)c["evt"] == evt);
    }

    [Fact]
    public void Contagens_do_servidor_vem_da_lista_de_guilds_com_counts()
    {
        using var json = JsonDocument.Parse("""
            [ { "id": "g0", "name": "Outro" },
              { "id": "g1", "name": "Servidor", "approximate_member_count": 1234, "approximate_presence_count": 56 } ]
            """);

        var counts = GuildCounts.Find(json.RootElement, "g1");

        Assert.Equal(new GuildCounts("Servidor", 1234, 56), counts);
        Assert.Null(GuildCounts.Find(json.RootElement, "nao-existe"));
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
