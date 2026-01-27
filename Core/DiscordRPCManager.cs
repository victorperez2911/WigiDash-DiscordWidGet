using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace WigiDash.DiscordWidget.Core
{
    public class DiscordRPCManager : IDisposable
    {
        private ClientWebSocket _webSocket;
        private string _clientId;
        private bool _isConnected;

        // Events
        public event Func<Task> Connected;
        public event Func<Task> Disconnected;

        public async Task<bool> ConnectAsync(string clientId)
        {
            _clientId = clientId;

            // Try ports 6463-6472
            for (int port = DiscordConfig.RPC_PORT_START; port <= DiscordConfig.RPC_PORT_END; port++)
            {
                try
                {
                    var uri = new Uri($"ws://127.0.0.1:{port}/?v=1&client_id={clientId}&encoding=json");

                    _webSocket = new ClientWebSocket();
                    _webSocket.Options.SetRequestHeader("Origin", DiscordConfig.RPC_ORIGIN);

                    await _webSocket.ConnectAsync(uri, CancellationToken.None);

                    if (_webSocket.State == WebSocketState.Open)
                    {
                        _isConnected = true;

                        // Start receive loop
                        _ = Task.Run(ReceiveLoop);

                        // Handshake
                        await SendHandshakeAsync();

                        Connected?.Invoke();

                        return true;
                    }
                }
                catch
                {
                    // Try next port
                    continue;
                }
            }

            return false;
        }

        private async Task SendHandshakeAsync()
        {
            var handshake = new
            {
                v = 1,
                client_id = _clientId
            };

            await SendAsync(handshake);
        }

        public async Task<bool> AuthorizeAsync(string accessToken)
        {
            var payload = new
            {
                cmd = "AUTHORIZE",
                args = new
                {
                    client_id = _clientId,
                    scopes = DiscordConfig.SCOPES
                },
                nonce = Guid.NewGuid().ToString()
            };

            await SendAsync(payload);
            // TODO: Wait for response
            return true;
        }

        public async Task<bool> SelectVoiceChannelAsync(string channelId)
        {
            var payload = new
            {
                cmd = "SELECT_VOICE_CHANNEL",
                args = new
                {
                    channel_id = channelId,
                    force = false
                },
                nonce = Guid.NewGuid().ToString()
            };

            await SendAsync(payload);
            // TODO: Wait for response
            return true;
        }

        public async Task<bool> SetVoiceSettingsAsync(bool? mute = null, bool? deaf = null)
        {
            var args = new System.Collections.Generic.Dictionary<string, object>();

            if (mute.HasValue)
                args["mute"] = mute.Value;

            if (deaf.HasValue)
                args["deaf"] = deaf.Value;

            var payload = new
            {
                cmd = "SET_VOICE_SETTINGS",
                args = args,
                nonce = Guid.NewGuid().ToString()
            };

            await SendAsync(payload);
            // TODO: Wait for response
            return true;
        }

        private async Task SendAsync(object payload)
        {
            if (!_isConnected || _webSocket?.State != WebSocketState.Open)
                return;

            var json = JsonConvert.SerializeObject(payload);
            var bytes = Encoding.UTF8.GetBytes(json);

            await _webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                CancellationToken.None
            );
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[4096];

            while (_isConnected && _webSocket?.State == WebSocketState.Open)
            {
                try
                {
                    var result = await _webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        CancellationToken.None
                    );

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        // TODO: Process message
                    }
                }
                catch
                {
                    break;
                }
            }

            _isConnected = false;
            Disconnected?.Invoke();
        }

        public void Dispose()
        {
            _isConnected = false;

            if (_webSocket != null)
            {
                try
                {
                    _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).Wait(2000);
                    _webSocket.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }
            }
        }
    }
}
