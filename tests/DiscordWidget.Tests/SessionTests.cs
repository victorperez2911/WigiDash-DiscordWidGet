using System.Text.Json.Nodes;
using WigiDash.DiscordWidget.Discord;

namespace DiscordWidget.Tests;

public sealed class SessionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "discordwidget-tests-" + Guid.NewGuid().ToString("N"));

    private AccountStore NewStore(DiscordToken? token = null)
    {
        var store = new AccountStore(Path.Combine(_dir, "account.json"));
        store.SaveToken(token);
        return store;
    }

    private static DiscordToken ValidToken() =>
        new("access", "refresh", DateTimeOffset.UtcNow.AddDays(7), "rpc rpc.voice.write");

    [Fact]
    public async Task Com_token_salvo_autentica_le_a_voz_e_assina_o_evento()
    {
        await using var discord = new FakeDiscord();
        discord.Handler = (cmd, _) => cmd == "GET_VOICE_SETTINGS" ? Json.VoiceSettings(mute: true) : new JsonObject();
        var session = new DiscordSession(NewStore(ValidToken()), TimeProvider.System, discord.Prefix);

        using (session.Acquire())
        {
            await FakeDiscord.WaitUntilAsync(() => session.State == DiscordState.Ready);

            var auth = await discord.WaitForCommandAsync("AUTHENTICATE");
            Assert.Equal("access", (string?)auth["args"]!["access_token"]);
            var subscribe = await discord.WaitForCommandAsync("SUBSCRIBE");
            Assert.Equal("VOICE_SETTINGS_UPDATE", (string?)subscribe["evt"]);
            Assert.True(session.Voice!.Mute);
            Assert.Equal(2, session.Voice.Input.Devices.Count);
            Assert.Equal("Tester", session.UserName);
        }

        await FakeDiscord.WaitUntilAsync(() => session.State == DiscordState.Stopped);
    }

    [Fact]
    public async Task Evento_de_voz_atualiza_o_estado_e_mantem_os_dispositivos()
    {
        await using var discord = new FakeDiscord();
        discord.Handler = (cmd, _) => cmd == "GET_VOICE_SETTINGS" ? Json.VoiceSettings() : new JsonObject();
        var session = new DiscordSession(NewStore(ValidToken()), TimeProvider.System, discord.Prefix);
        using var lease = session.Acquire();
        await FakeDiscord.WaitUntilAsync(() => session.State == DiscordState.Ready);

        await discord.SendEventAsync("VOICE_SETTINGS_UPDATE", new JsonObject { ["mute"] = true, ["deaf"] = true });

        await FakeDiscord.WaitUntilAsync(() => session.Voice is { Deaf: true });
        Assert.Equal(2, session.Voice!.Input.Devices.Count);
    }

    [Fact]
    public async Task SetVoiceSettings_manda_o_comando_e_usa_a_resposta()
    {
        await using var discord = new FakeDiscord();
        discord.Handler = (cmd, args) => cmd switch
        {
            "GET_VOICE_SETTINGS" => Json.VoiceSettings(),
            "SET_VOICE_SETTINGS" => Json.VoiceSettings(mute: (bool)args["mute"]!),
            _ => new JsonObject(),
        };
        var session = new DiscordSession(NewStore(ValidToken()), TimeProvider.System, discord.Prefix);
        using var lease = session.Acquire();
        await FakeDiscord.WaitUntilAsync(() => session.State == DiscordState.Ready);

        await session.SetVoiceSettingsAsync(new JsonObject { ["mute"] = true });

        Assert.True(session.Voice!.Mute);
        var set = await discord.WaitForCommandAsync("SET_VOICE_SETTINGS");
        Assert.True((bool)set["args"]!["mute"]!);
    }

    [Fact]
    public async Task Sem_token_espera_o_pedido_e_manda_AUTHORIZE_com_PKCE_e_os_10_scopes()
    {
        await using var discord = new FakeDiscord();
        discord.Handler = (cmd, _) => cmd == "AUTHORIZE" ? throw new FakeError(5000, "OAuth2 Error: access_denied") : new JsonObject();
        var session = new DiscordSession(NewStore(), TimeProvider.System, discord.Prefix);
        using var lease = session.Acquire();

        await FakeDiscord.WaitUntilAsync(() => session.State == DiscordState.NeedsAuthorization);
        Assert.DoesNotContain(discord.Commands, c => (string?)c["cmd"] == "AUTHORIZE");

        session.RequestAuthorization();

        var authorize = await discord.WaitForCommandAsync("AUTHORIZE");
        var args = authorize["args"]!;
        Assert.Equal("S256", (string?)args["code_challenge_method"]);
        Assert.Equal(43, ((string?)args["code_challenge"])!.Length);
        Assert.Equal(10, args["scopes"]!.AsArray().Count);
        Assert.Equal(AccountStore.DefaultClientId, (string?)args["client_id"]);

        // Recusa no popup: volta a esperar, com o motivo para mostrar.
        await FakeDiscord.WaitUntilAsync(() => session.State == DiscordState.NeedsAuthorization && session.Message is not null);
        Assert.Contains("access_denied", session.Message);
    }

    [Fact]
    public async Task Sem_Discord_fica_DiscordNotRunning()
    {
        var session = new DiscordSession(NewStore(ValidToken()), TimeProvider.System, $"ninguem-{Guid.NewGuid():N}-");
        using var lease = session.Acquire();

        await FakeDiscord.WaitUntilAsync(() => session.State == DiscordState.DiscordNotRunning, 10000);
    }

    [Fact]
    public void Token_fica_criptografado_no_arquivo()
    {
        var store = NewStore(ValidToken());

        Assert.DoesNotContain("access", File.ReadAllText(store.FilePath));
        Assert.Equal("access", store.LoadToken()!.AccessToken);
    }

    [Fact]
    public void Trocar_o_client_id_descarta_o_token()
    {
        var store = NewStore(ValidToken());

        store.ClientId = "999";

        Assert.Equal("999", store.ClientId);
        Assert.Null(store.LoadToken());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
