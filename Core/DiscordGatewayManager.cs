using System;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

namespace WigiDash.DiscordWidget.Core
{
    public class DiscordGatewayManager : IDisposable
    {
        private DiscordSocketClient _client;
        private bool _isConnected;

        // Events
        public event Func<SocketUser, SocketVoiceState, SocketVoiceState, Task> UserVoiceStateUpdated;
        public event Func<Task> Connected;
        public event Func<Exception, Task> Disconnected;

        public async Task<bool> ConnectAsync(string token, TokenType tokenType)
        {
            try
            {
                // Enable TLS 1.2 for .NET Framework
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

                var config = new DiscordSocketConfig
                {
                    GatewayIntents = GatewayIntents.Guilds |
                                   GatewayIntents.GuildVoiceStates |
                                   GatewayIntents.GuildMembers,
                    LogLevel = LogSeverity.Info
                };

                _client = new DiscordSocketClient(config);

                // Subscribe events
                _client.UserVoiceStateUpdated += OnUserVoiceStateUpdated;
                _client.Connected += OnConnected;
                _client.Disconnected += OnDisconnected;

                await _client.LoginAsync(tokenType, token);
                await _client.StartAsync();

                // Wait for connection (timeout 30s)
                return await WaitForConnectionAsync(TimeSpan.FromSeconds(30));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Gateway connection error: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> WaitForConnectionAsync(TimeSpan timeout)
        {
            var startTime = DateTime.UtcNow;

            while (!_isConnected && DateTime.UtcNow - startTime < timeout)
            {
                await Task.Delay(100);
            }

            return _isConnected;
        }

        private Task OnUserVoiceStateUpdated(SocketUser user, SocketVoiceState oldState, SocketVoiceState newState)
        {
            UserVoiceStateUpdated?.Invoke(user, oldState, newState);
            return Task.CompletedTask;
        }

        private Task OnConnected()
        {
            _isConnected = true;
            Connected?.Invoke();
            return Task.CompletedTask;
        }

        private Task OnDisconnected(Exception ex)
        {
            _isConnected = false;
            Disconnected?.Invoke(ex);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_client != null)
            {
                try
                {
                    _client.StopAsync().Wait(5000);
                    _client.LogoutAsync().Wait(5000);
                    _client.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }
            }
        }
    }
}
