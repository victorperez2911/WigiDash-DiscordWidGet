using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WigiDash.DiscordWidget.Discord;

/// <summary>
/// RPC do Discord sobre uma <see cref="IpcConnection"/>: comandos com nonce (resposta casada pelo nonce) e
/// eventos DISPATCH de quem fez SUBSCRIBE. O loop de leitura roda até o pipe fechar.
/// </summary>
public sealed class RpcClient : IAsyncDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly IpcConnection _connection;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<Exception?> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _readLoop;

    private RpcClient(IpcConnection connection, JsonElement ready)
    {
        _connection = connection;
        Ready = ready;
    }

    /// <summary>Payload do READY: <c>user</c> (quem está logado no Discord) e <c>config</c>.</summary>
    public JsonElement Ready { get; }

    /// <summary>Evento recebido (nome do evento, <c>data</c>). Chega na thread do loop de leitura.</summary>
    public event Action<string, JsonElement>? EventReceived;

    /// <summary>Completa quando a conexão cai; o valor é o motivo (null = fechamento pedido por nós).</summary>
    public Task<Exception?> Closed => _closed.Task;

    public static async Task<RpcClient> ConnectAsync(string clientId, CancellationToken cancellationToken,
        string pipePrefix = IpcConnection.DefaultPipePrefix)
    {
        var (connection, ready) = await IpcConnection.ConnectAsync(clientId, cancellationToken, pipePrefix).ConfigureAwait(false);
        var client = new RpcClient(connection, ready);
        client._readLoop = Task.Run(client.ReadLoopAsync);
        return client;
    }

    /// <summary>Manda um comando e espera a resposta. Evento ERROR vira <see cref="DiscordRpcException"/>.</summary>
    public async Task<JsonElement> SendAsync(string command, JsonObject? args = null, string? evt = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var nonce = Guid.NewGuid().ToString();
        var payload = new JsonObject { ["cmd"] = command, ["nonce"] = nonce, ["args"] = args ?? new JsonObject() };
        if (evt is not null)
            payload["evt"] = evt;

        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[nonce] = tcs;
        try
        {
            await _connection.SendAsync(IpcOpcode.Frame, payload, cancellationToken).ConfigureAwait(false);
            return await tcs.Task.WaitAsync(timeout ?? DefaultTimeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(nonce, out _);
        }
    }

    public Task SubscribeAsync(string evt, JsonObject? args = null, CancellationToken cancellationToken = default) =>
        SendAsync("SUBSCRIBE", args, evt, cancellationToken: cancellationToken);

    public Task UnsubscribeAsync(string evt, JsonObject? args = null, CancellationToken cancellationToken = default) =>
        SendAsync("UNSUBSCRIBE", args, evt, cancellationToken: cancellationToken);

    private async Task ReadLoopAsync()
    {
        Exception? reason = null;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var message = await _connection.ReadAsync(_stop.Token).ConfigureAwait(false);
                switch (message.Opcode)
                {
                    case IpcOpcode.Frame:
                        HandleFrame(message.Payload);
                        break;
                    case IpcOpcode.Ping:
                        await _connection.SendAsync(IpcOpcode.Pong, JsonNode.Parse(message.Payload.GetRawText())!, _stop.Token)
                            .ConfigureAwait(false);
                        break;
                    case IpcOpcode.Close:
                        reason = DiscordRpcException.FromPayload(message.Payload);
                        return;
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            reason = ex;
        }
        finally
        {
            var failure = reason ?? new ObjectDisposedException(nameof(RpcClient));
            foreach (var pending in _pending.Values)
                pending.TrySetException(failure);
            _closed.TrySetResult(reason);
        }
    }

    private void HandleFrame(JsonElement payload)
    {
        if (payload.TryGetProperty("nonce", out var nonce) && nonce.ValueKind == JsonValueKind.String
            && _pending.TryGetValue(nonce.GetString()!, out var tcs))
        {
            if (IpcConnection.IsEvent(payload, "ERROR"))
                tcs.TrySetException(DiscordRpcException.FromPayload(payload));
            else
                tcs.TrySetResult(payload.TryGetProperty("data", out var data) ? data : default);
            return;
        }

        if (payload.TryGetProperty("cmd", out var cmd) && cmd.GetString() == "DISPATCH"
            && payload.TryGetProperty("evt", out var evt) && evt.ValueKind == JsonValueKind.String)
        {
            var data = payload.TryGetProperty("data", out var d) ? d : default;
            try
            {
                EventReceived?.Invoke(evt.GetString()!, data);
            }
            catch
            {
                // Erro de quem ouve não pode derrubar o loop de leitura (quem ouve loga o próprio erro).
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        await _connection.DisposeAsync().ConfigureAwait(false);
        if (_readLoop is not null)
            await _readLoop.ConfigureAwait(false);
        _stop.Dispose();
    }
}
