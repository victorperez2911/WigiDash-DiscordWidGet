using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WigiDash.DiscordWidget.Discord;

/// <summary>Opcodes do IPC local do Discord.</summary>
public enum IpcOpcode
{
    Handshake = 0,
    Frame = 1,
    Close = 2,
    Ping = 3,
    Pong = 4,
}

/// <summary>Um pacote do pipe: opcode e o JSON que veio com ele.</summary>
public readonly record struct IpcMessage(IpcOpcode Opcode, JsonElement Payload);

/// <summary>
/// Conexão com o cliente do Discord pelo named pipe <c>\\.\pipe\discord-ipc-N</c>. É o mesmo transporte do
/// plugin da Elgato: o pipe não checa Origin, ao contrário do WebSocket nas portas 6463-6472 (erro 4001).
/// Cada pacote é opcode (int32 LE) + tamanho (int32 LE) + JSON em UTF-8.
/// </summary>
public sealed class IpcConnection : IAsyncDisposable
{
    public const string DefaultPipePrefix = "discord-ipc-";
    private const int MaxPipes = 10;
    private const int MaxPayload = 16 * 1024 * 1024;

    private readonly NamedPipeClientStream _pipe;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private IpcConnection(NamedPipeClientStream pipe, string pipeName)
    {
        _pipe = pipe;
        PipeName = pipeName;
    }

    public string PipeName { get; }

    /// <summary>
    /// Abre o primeiro pipe disponível (discord-ipc-0 a 9) e faz o handshake. Devolve a conexão e o
    /// payload do READY (com o usuário logado no Discord).
    /// </summary>
    /// <exception cref="DiscordNotRunningException">Nenhum pipe respondeu.</exception>
    /// <exception cref="DiscordRpcException">O Discord fechou no handshake (ex.: client_id inválido).</exception>
    /// <param name="pipePrefix">Prefixo do pipe; os testes trocam para não cair no Discord de verdade.</param>
    public static async Task<(IpcConnection Connection, JsonElement Ready)> ConnectAsync(
        string clientId, CancellationToken cancellationToken, string pipePrefix = DefaultPipePrefix)
    {
        for (var i = 0; i < MaxPipes; i++)
        {
            var name = $"{pipePrefix}{i}";
            var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            var connection = new IpcConnection(pipe, name);
            try
            {
                await connection.SendAsync(IpcOpcode.Handshake, new JsonObject { ["v"] = 1, ["client_id"] = clientId },
                    cancellationToken).ConfigureAwait(false);
                var first = await connection.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (first.Opcode == IpcOpcode.Close)
                    throw DiscordRpcException.FromPayload(first.Payload);
                if (first.Opcode != IpcOpcode.Frame || !IsEvent(first.Payload, "READY"))
                    throw new DiscordRpcException(0, $"Resposta inesperada no handshake: {first.Payload}");
                return (connection, first.Payload.GetProperty("data").Clone());
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        throw new DiscordNotRunningException();
    }

    public async Task SendAsync(IpcOpcode opcode, JsonNode payload, CancellationToken cancellationToken)
    {
        var json = Encoding.UTF8.GetBytes(payload.ToJsonString());
        var buffer = new byte[8 + json.Length];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), (int)opcode);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4, 4), json.Length);
        json.CopyTo(buffer, 8);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _pipe.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            await _pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Lê o próximo pacote. Só uma leitura por vez (o loop do <see cref="RpcClient"/>).</summary>
    /// <exception cref="EndOfStreamException">O Discord fechou o pipe.</exception>
    public async Task<IpcMessage> ReadAsync(CancellationToken cancellationToken)
    {
        var header = new byte[8];
        await _pipe.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var opcode = (IpcOpcode)BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
        if (length < 0 || length > MaxPayload)
            throw new InvalidDataException($"Pacote do Discord com tamanho inválido: {length}");

        var body = new byte[length];
        await _pipe.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        return new IpcMessage(opcode, document.RootElement.Clone());
    }

    public async ValueTask DisposeAsync()
    {
        await _pipe.DisposeAsync().ConfigureAwait(false);
        _writeLock.Dispose();
    }

    internal static bool IsEvent(JsonElement payload, string name) =>
        payload.TryGetProperty("evt", out var evt) && evt.ValueKind == JsonValueKind.String && evt.GetString() == name;
}

/// <summary>Nenhum pipe do Discord respondeu: o app não está aberto.</summary>
public sealed class DiscordNotRunningException() : Exception("O Discord não está aberto.");

/// <summary>Erro devolvido pelo Discord (evento ERROR ou fechamento com código).</summary>
public sealed class DiscordRpcException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;

    public static DiscordRpcException FromPayload(JsonElement payload)
    {
        var source = payload.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object ? data : payload;
        var code = source.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
        var message = source.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString() ?? string.Empty
            : source.ToString();
        return new DiscordRpcException(code, message);
    }
}
