using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiscordWidget.Tests;

/// <summary>
/// Um "Discord" de mentira no pipe <c>{Prefix}0</c>: responde o handshake com READY e entrega cada comando
/// recebido ao <see cref="Handler"/>, que devolve o <c>data</c> da resposta (ou lança para responder ERROR).
/// </summary>
internal sealed class FakeDiscord : IAsyncDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly Task _loop;

    public FakeDiscord()
    {
        Prefix = $"wigidash-test-{Guid.NewGuid():N}-";
        _pipe = new NamedPipeServerStream(Prefix + "0", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        _loop = Task.Run(RunAsync);
    }

    public string Prefix { get; }

    public string? HandshakeClientId { get; private set; }

    /// <summary>Comandos recebidos, na ordem.</summary>
    public ConcurrentQueue<JsonObject> Commands { get; } = new();

    public Func<string, JsonObject, JsonNode?> Handler { get; set; } = (_, _) => new JsonObject();

    public TaskCompletionSource<JsonNode?> Pong { get; } = new();

    public Task<JsonObject> WaitForCommandAsync(string cmd) => WaitAsync(() => Commands.FirstOrDefault(c => (string?)c["cmd"] == cmd));

    public async Task SendEventAsync(string evt, JsonNode data) =>
        await WriteAsync(1, new JsonObject { ["cmd"] = "DISPATCH", ["evt"] = evt, ["data"] = data, ["nonce"] = null });

    public Task SendPingAsync(JsonNode payload) => WriteAsync(3, payload);

    public Task CloseAsync(int code, string message) =>
        WriteAsync(2, new JsonObject { ["code"] = code, ["message"] = message });

    private async Task RunAsync()
    {
        try
        {
            await _pipe.WaitForConnectionAsync(_stop.Token);
            var (_, handshake) = await ReadAsync();
            HandshakeClientId = (string?)handshake["client_id"];
            await WriteAsync(1, new JsonObject
            {
                ["cmd"] = "DISPATCH",
                ["evt"] = "READY",
                ["nonce"] = null,
                ["data"] = new JsonObject { ["v"] = 1, ["user"] = new JsonObject { ["username"] = "tester", ["global_name"] = "Tester" } },
            });

            while (!_stop.IsCancellationRequested)
            {
                var (op, payload) = await ReadAsync();
                if (op == 4)
                {
                    Pong.TrySetResult(payload);
                    continue;
                }
                var command = payload.AsObject();
                Commands.Enqueue(command);
                var cmd = (string)command["cmd"]!;
                var nonce = (string?)command["nonce"];
                JsonObject reply;
                try
                {
                    var data = Handler(cmd, command["args"]?.AsObject() ?? new JsonObject());
                    reply = new JsonObject { ["cmd"] = cmd, ["nonce"] = nonce, ["data"] = data, ["evt"] = null };
                }
                catch (FakeError error)
                {
                    reply = new JsonObject
                    {
                        ["cmd"] = cmd,
                        ["nonce"] = nonce,
                        ["evt"] = "ERROR",
                        ["data"] = new JsonObject { ["code"] = error.Code, ["message"] = error.Message },
                    };
                }
                await WriteAsync(1, reply);
            }
        }
        catch (Exception) when (_stop.IsCancellationRequested)
        {
        }
        catch (EndOfStreamException)
        {
        }
        catch (IOException)
        {
        }
    }

    private async Task<(int Op, JsonNode Payload)> ReadAsync()
    {
        var header = new byte[8];
        await _pipe.ReadExactlyAsync(header, _stop.Token);
        var body = new byte[BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4))];
        await _pipe.ReadExactlyAsync(body, _stop.Token);
        return (BinaryPrimitives.ReadInt32LittleEndian(header), JsonNode.Parse(body)!);
    }

    private async Task WriteAsync(int op, JsonNode payload)
    {
        var json = Encoding.UTF8.GetBytes(payload.ToJsonString());
        var buffer = new byte[8 + json.Length];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, op);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), json.Length);
        json.CopyTo(buffer, 8);
        await _write.WaitAsync();
        try
        {
            await _pipe.WriteAsync(buffer, _stop.Token);
            await _pipe.FlushAsync(_stop.Token);
        }
        finally
        {
            _write.Release();
        }
    }

    public static async Task<T> WaitAsync<T>(Func<T?> probe, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (probe() is { } value)
                return value;
            await Task.Delay(20);
        }
        throw new TimeoutException("Condição não aconteceu a tempo.");
    }

    public static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000) =>
        await WaitAsync<object>(() => condition() ? new object() : null, timeoutMs);

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try
        {
            if (_pipe.IsConnected)
                _pipe.Disconnect();
        }
        catch (InvalidOperationException)
        {
        }
        await _pipe.DisposeAsync();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }
    }
}

internal sealed class FakeError(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}

internal static class Json
{
    public static JsonNode VoiceSettings(bool mute = false, bool deaf = false) => JsonNode.Parse($$"""
        {
          "mute": {{(mute ? "true" : "false")}},
          "deaf": {{(deaf ? "true" : "false")}},
          "mode": { "type": "VOICE_ACTIVITY" },
          "input": { "device_id": "default", "volume": 100, "available_devices": [ { "id": "default", "name": "Padrão" }, { "id": "mic2", "name": "Shure" } ] },
          "output": { "device_id": "default", "volume": 80, "available_devices": [ { "id": "default", "name": "Padrão" } ] }
        }
        """)!;
}
