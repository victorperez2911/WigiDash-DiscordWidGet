using System.Text.Json.Nodes;
using WigiDash.DiscordWidget.Discord;

namespace DiscordWidget.Tests;

public sealed class RpcTests
{
    [Fact]
    public async Task Handshake_manda_client_id_e_le_o_READY()
    {
        await using var discord = new FakeDiscord();
        await using var rpc = await RpcClient.ConnectAsync("123", CancellationToken.None, discord.Prefix);

        Assert.Equal("123", discord.HandshakeClientId);
        Assert.Equal("Tester", rpc.Ready.GetProperty("user").GetProperty("global_name").GetString());
    }

    [Fact]
    public async Task Comando_recebe_a_resposta_do_mesmo_nonce()
    {
        await using var discord = new FakeDiscord();
        discord.Handler = (cmd, args) => cmd == "GET_VOICE_SETTINGS" ? Json.VoiceSettings(mute: true) : new JsonObject();
        await using var rpc = await RpcClient.ConnectAsync("123", CancellationToken.None, discord.Prefix);

        var data = await rpc.SendAsync("GET_VOICE_SETTINGS");

        Assert.True(VoiceSettings.Parse(data).Mute);
    }

    [Fact]
    public async Task Evento_ERROR_vira_DiscordRpcException_com_o_codigo()
    {
        await using var discord = new FakeDiscord();
        discord.Handler = (_, _) => throw new FakeError(4006, "Not authenticated or invalid scope");
        await using var rpc = await RpcClient.ConnectAsync("123", CancellationToken.None, discord.Prefix);

        var error = await Assert.ThrowsAsync<DiscordRpcException>(() => rpc.SendAsync("SET_VOICE_SETTINGS"));

        Assert.Equal(4006, error.Code);
    }

    [Fact]
    public async Task DISPATCH_chega_no_EventReceived()
    {
        await using var discord = new FakeDiscord();
        await using var rpc = await RpcClient.ConnectAsync("123", CancellationToken.None, discord.Prefix);
        var received = new TaskCompletionSource<string>();
        rpc.EventReceived += (evt, data) => received.TrySetResult($"{evt}:{data.GetProperty("mute").GetBoolean()}");

        await discord.SendEventAsync("VOICE_SETTINGS_UPDATE", Json.VoiceSettings(mute: true));

        Assert.Equal("VOICE_SETTINGS_UPDATE:True", await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Ping_recebe_pong_com_o_mesmo_payload()
    {
        await using var discord = new FakeDiscord();
        await using var rpc = await RpcClient.ConnectAsync("123", CancellationToken.None, discord.Prefix);

        await discord.SendPingAsync(new JsonObject { ["n"] = 7 });

        var pong = await discord.Pong.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(7, (int)pong!["n"]!);
    }

    [Fact]
    public async Task Fechamento_do_Discord_completa_Closed_com_o_motivo()
    {
        await using var discord = new FakeDiscord();
        await using var rpc = await RpcClient.ConnectAsync("123", CancellationToken.None, discord.Prefix);

        await discord.CloseAsync(4009, "Invalid token");

        var reason = Assert.IsType<DiscordRpcException>(await rpc.Closed.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(4009, reason.Code);
    }

    [Fact]
    public async Task Sem_pipe_lanca_DiscordNotRunning()
    {
        await Assert.ThrowsAsync<DiscordNotRunningException>(
            () => RpcClient.ConnectAsync("123", CancellationToken.None, $"ninguem-{Guid.NewGuid():N}-"));
    }
}
