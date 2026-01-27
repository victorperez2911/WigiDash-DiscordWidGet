using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp;

namespace WigiDash.DiscordWidget.Core
{
    public class DiscordRPCManager : IDisposable
    {
        private WebSocket _webSocket;
        private string _clientId;
        private string _accessToken;
        private bool _isConnected;
        private bool _isAuthorized;
        private readonly Dictionary<string, TaskCompletionSource<RPCResponse>> _pendingRequests
            = new Dictionary<string, TaskCompletionSource<RPCResponse>>();
        private readonly object _requestLock = new object();

        // Events
        public event Func<Task> Connected;
        public event Func<Task> Disconnected;
        public event Func<VoiceStateData, Task> VoiceStateUpdated;
        public event Func<SpeakingData, Task> SpeakingStarted;
        public event Func<SpeakingData, Task> SpeakingStopped;
        public event Func<VoiceChannelSelectData, Task> VoiceChannelSelected;

        public bool IsConnected => _isConnected && _webSocket != null && _webSocket.IsAlive;
        public bool IsAuthorized => _isAuthorized;

        public async Task<bool> ConnectAsync(string clientId)
        {
            _clientId = clientId;

            FileLogger.Log($"RPC: Starting connection attempt with client_id={clientId}");

            // Try multiple Origin values (Discord may expect specific value)
            var originsToTry = new[]
            {
                "https://localhost",      // Recommended by Discord docs
                "http://localhost",       // Alternative
                "http://127.0.0.1",       // IP-based
                null                      // No Origin (last resort)
            };

            // Try ports 6463-6472
            for (int port = DiscordConfig.RPC_PORT_START; port <= DiscordConfig.RPC_PORT_END; port++)
            {
                foreach (var origin in originsToTry)
                {
                    try
                    {
                        var url = $"ws://127.0.0.1:{port}/?v=1&client_id={clientId}";
                        FileLogger.Log($"RPC: Trying port {port} with Origin: {origin ?? "NULL"}");

                        _webSocket = new WebSocket(url);

                        // Set Origin to test value (or null to omit)
                        _webSocket.Origin = origin;
                        FileLogger.Log($"RPC: Origin header configured to: {(_webSocket.Origin ?? "NULL")}");

                    // Configure WebSocket
                    _webSocket.Log.Level = LogLevel.Error; // Reduce logging noise
                    _webSocket.WaitTime = TimeSpan.FromSeconds(5);

                    // Setup event handlers
                    var tcs = new TaskCompletionSource<bool>();

                    _webSocket.OnOpen += (sender, e) =>
                    {
                        _isConnected = true;
                        FileLogger.Log($"RPC: WebSocket CONNECTED on port {port}!");
                        tcs.TrySetResult(true);
                        Connected?.Invoke();
                    };

                    _webSocket.OnMessage += OnMessageReceived;

                    bool got4001Error = false;
                    _webSocket.OnClose += (sender, e) =>
                    {
                        FileLogger.Log($"RPC: WebSocket CLOSED - Code: {e.Code}, Reason: {e.Reason}");
                        if (e.Code == 4001)
                        {
                            got4001Error = true;
                            FileLogger.Log($"RPC: ERROR 4001 - Invalid Origin '{origin ?? "NULL"}' rejected by Discord");
                        }
                        _isConnected = false;
                        _isAuthorized = false;
                        Disconnected?.Invoke();
                        tcs.TrySetResult(false);
                    };

                    _webSocket.OnError += (sender, e) =>
                    {
                        FileLogger.Log($"RPC: WebSocket ERROR - {e.Message}");
                        if (e.Exception != null)
                        {
                            FileLogger.Log($"RPC: Exception: {e.Exception}");
                        }
                        tcs.TrySetResult(false);
                    };

                    // Connect
                    _webSocket.Connect();

                        // Wait for connection result (timeout 5 seconds)
                        var timeoutTask = Task.Delay(5000);
                        var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                        if (completedTask == timeoutTask || !tcs.Task.Result)
                        {
                            FileLogger.Log($"RPC: Port {port} with Origin '{origin ?? "NULL"}' - timeout or failed");
                            _webSocket?.Close();
                            _webSocket = null;
                            continue; // Try next Origin
                        }

                        // Check if we got error 4001 (invalid origin)
                        if (got4001Error)
                        {
                            FileLogger.Log($"RPC: Origin '{origin ?? "NULL"}' rejected, trying next...");
                            _webSocket?.Close();
                            _webSocket = null;
                            continue; // Try next Origin
                        }

                        // Success! No error 4001
                        FileLogger.Log($"RPC: Successfully connected on port {port} with Origin: {origin ?? "NULL"}");

                        // Wait a bit for READY event
                        await Task.Delay(500);

                        return true;
                    }
                    catch (Exception ex)
                    {
                        FileLogger.Log($"RPC: Port {port} Origin '{origin ?? "NULL"}' exception - {ex.Message}");
                        _webSocket?.Close();
                        _webSocket = null;
                    }
                } // End of foreach origin
            } // End of for port

            FileLogger.Log("RPC: Connection FAILED - no ports responded");
            return false;
        }

        public async Task<bool> AuthorizeAsync(string accessToken)
        {
            if (!IsConnected)
            {
                FileLogger.Log("RPC: Cannot authorize - not connected");
                return false;
            }

            _accessToken = accessToken;

            try
            {
                FileLogger.Log("RPC: Sending AUTHORIZE command...");
                FileLogger.Log($"RPC: Client ID: {_clientId}");
                FileLogger.Log($"RPC: Token prefix: {(accessToken.Length > 10 ? accessToken.Substring(0, 10) + "..." : "invalid")}");
                FileLogger.Log($"RPC: Scopes: {string.Join(", ", DiscordConfig.SCOPES)}");

                var payload = new RPCRequest
                {
                    Command = "AUTHORIZE",
                    Args = new Dictionary<string, object>
                    {
                        ["client_id"] = _clientId,
                        ["scopes"] = DiscordConfig.SCOPES,
                        ["access_token"] = accessToken
                    },
                    Nonce = Guid.NewGuid().ToString()
                };

                FileLogger.Log($"RPC: AUTHORIZE payload: {JsonConvert.SerializeObject(payload, Formatting.Indented)}");

                var response = await SendRequestAsync(payload, timeoutMs: 15000);

                FileLogger.Log($"RPC: AUTHORIZE response received - Command: {response?.Command}, Event: {response?.Event}");

                if (response != null && response.Event == null)
                {
                    _isAuthorized = true;
                    FileLogger.Log("RPC: Authorization SUCCESS!");
                    return true;
                }

                if (response?.Event == "ERROR")
                {
                    var errorCode = response.Data?["code"]?.ToString();
                    var errorMessage = response.Data?["message"]?.ToString();
                    FileLogger.Log($"RPC: Authorization ERROR - Code: {errorCode}, Message: {errorMessage}");
                    FileLogger.Log($"RPC: Full error data: {response.Data?.ToString()}");
                }

                FileLogger.Log("RPC: Authorization FAILED - Invalid response");
                return false;
            }
            catch (Exception ex)
            {
                FileLogger.Log($"RPC: Authorization EXCEPTION - {ex.GetType().Name}: {ex.Message}");
                FileLogger.Log($"RPC: Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        public async Task<bool> AuthenticateAsync(string accessToken)
        {
            if (!IsConnected)
            {
                FileLogger.Log("RPC: Cannot authenticate - not connected");
                System.Diagnostics.Debug.WriteLine("RPC: Cannot authenticate - not connected");
                return false;
            }

            try
            {
                FileLogger.Log("RPC: Sending AUTHENTICATE command...");
                FileLogger.Log($"RPC: Token prefix: {(accessToken.Length > 10 ? accessToken.Substring(0, 10) + "..." : "invalid")}");
                System.Diagnostics.Debug.WriteLine("RPC: Sending AUTHENTICATE command...");

                var payload = new RPCRequest
                {
                    Command = "AUTHENTICATE",
                    Args = new Dictionary<string, object>
                    {
                        ["access_token"] = accessToken
                    },
                    Nonce = Guid.NewGuid().ToString()
                };

                var response = await SendRequestAsync(payload, timeoutMs: 15000);

                FileLogger.Log($"RPC: AUTHENTICATE response received - Command: {response?.Command}, Event: {response?.Event}");
                System.Diagnostics.Debug.WriteLine($"RPC: AUTHENTICATE response received - Command: {response?.Command}, Event: {response?.Event}");

                if (response != null && response.Event == null)
                {
                    _isAuthorized = true;
                    FileLogger.Log("RPC: Authentication SUCCESS!");
                    System.Diagnostics.Debug.WriteLine("RPC: Authentication SUCCESS");
                    return true;
                }

                if (response?.Event == "ERROR")
                {
                    var errorCode = response.Data?["code"]?.ToString();
                    var errorMessage = response.Data?["message"]?.ToString();
                    var fullError = response.Data?.ToString();
                    FileLogger.Log($"RPC: Authentication ERROR - Code: {errorCode}, Message: {errorMessage}");
                    FileLogger.Log($"RPC: Full error data: {fullError}");
                    System.Diagnostics.Debug.WriteLine($"RPC: Authentication ERROR - Code: {errorCode}, Message: {errorMessage}");
                }

                FileLogger.Log("RPC: Authentication FAILED - Invalid response");
                System.Diagnostics.Debug.WriteLine("RPC: Authentication FAILED - Invalid response");
                return false;
            }
            catch (Exception ex)
            {
                FileLogger.Log($"RPC: Authentication EXCEPTION - {ex.GetType().Name}: {ex.Message}");
                FileLogger.Log($"RPC: Stack trace: {ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"RPC: Authentication EXCEPTION - {ex.Message}");
                return false;
            }
        }

        public async Task<VoiceStateData> GetVoiceSettingsAsync()
        {
            if (!IsConnected || !IsAuthorized)
                return null;

            try
            {
                var payload = new RPCRequest
                {
                    Command = "GET_VOICE_SETTINGS",
                    Args = new Dictionary<string, object>(),
                    Nonce = Guid.NewGuid().ToString()
                };

                var response = await SendRequestAsync(payload);

                if (response?.Data != null)
                {
                    return response.Data.ToObject<VoiceStateData>();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> SelectVoiceChannelAsync(string channelId)
        {
            if (!IsConnected || !IsAuthorized)
                return false;

            try
            {
                var payload = new RPCRequest
                {
                    Command = "SELECT_VOICE_CHANNEL",
                    Args = new Dictionary<string, object>
                    {
                        ["channel_id"] = string.IsNullOrEmpty(channelId) ? null : channelId,
                        ["timeout"] = 30,
                        ["force"] = false
                    },
                    Nonce = Guid.NewGuid().ToString()
                };

                var response = await SendRequestAsync(payload);
                return response != null && response.Event == null;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> SetVoiceSettingsAsync(Dictionary<string, object> settings)
        {
            if (!IsConnected || !IsAuthorized)
                return false;

            try
            {
                var payload = new RPCRequest
                {
                    Command = "SET_VOICE_SETTINGS",
                    Args = settings,
                    Nonce = Guid.NewGuid().ToString()
                };

                var response = await SendRequestAsync(payload);
                return response != null && response.Event == null;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> SubscribeAsync(string eventName, params object[] args)
        {
            if (!IsConnected || !IsAuthorized)
                return false;

            try
            {
                var payload = new RPCRequest
                {
                    Command = "SUBSCRIBE",
                    Event = eventName,
                    Args = args.Length > 0
                        ? JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(args[0]))
                        : new Dictionary<string, object>(),
                    Nonce = Guid.NewGuid().ToString()
                };

                var response = await SendRequestAsync(payload);
                return response != null && response.Event == null;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> UnsubscribeAsync(string eventName, params object[] args)
        {
            if (!IsConnected || !IsAuthorized)
                return false;

            try
            {
                var payload = new RPCRequest
                {
                    Command = "UNSUBSCRIBE",
                    Event = eventName,
                    Args = args.Length > 0
                        ? JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(args[0]))
                        : new Dictionary<string, object>(),
                    Nonce = Guid.NewGuid().ToString()
                };

                var response = await SendRequestAsync(payload);
                return response != null && response.Event == null;
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<RPCGuild>> GetGuildsAsync()
        {
            if (!IsConnected || !IsAuthorized)
            {
                FileLogger.Log($"RPC: GetGuilds FAILED - Connected: {IsConnected}, Authorized: {IsAuthorized}");
                return null;
            }

            try
            {
                FileLogger.Log("RPC: Sending GET_GUILDS command...");

                var payload = new RPCRequest
                {
                    Command = "GET_GUILDS",
                    Args = new Dictionary<string, object>(),
                    Nonce = Guid.NewGuid().ToString()
                };

                var response = await SendRequestAsync(payload);

                if (response?.Data != null)
                {
                    var guildsData = response.Data["guilds"];
                    var guilds = JsonConvert.DeserializeObject<List<RPCGuild>>(guildsData.ToString());
                    FileLogger.Log($"RPC: GET_GUILDS SUCCESS - Found {guilds?.Count ?? 0} guilds");
                    return guilds;
                }

                FileLogger.Log("RPC: GET_GUILDS FAILED - No data in response");
                return null;
            }
            catch (Exception ex)
            {
                FileLogger.Log($"RPC: GET_GUILDS EXCEPTION - {ex.Message}");
                return null;
            }
        }

        public async Task<List<RPCChannel>> GetChannelsAsync(string guildId)
        {
            if (!IsConnected || !IsAuthorized)
                return null;

            try
            {
                var payload = new RPCRequest
                {
                    Command = "GET_CHANNELS",
                    Args = new Dictionary<string, object>
                    {
                        ["guild_id"] = guildId
                    },
                    Nonce = Guid.NewGuid().ToString()
                };

                var response = await SendRequestAsync(payload);

                if (response?.Data != null)
                {
                    var channelsData = response.Data["channels"];
                    var allChannels = JsonConvert.DeserializeObject<List<RPCChannel>>(channelsData.ToString());

                    // Filter only voice channels (type 2 = voice channel)
                    return allChannels?.Where(c => c.Type == 2).ToList();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<RPCResponse> SendRequestAsync(RPCRequest request, int timeoutMs = 10000)
        {
            if (!IsConnected)
                return null;

            var tcs = new TaskCompletionSource<RPCResponse>();

            lock (_requestLock)
            {
                _pendingRequests[request.Nonce] = tcs;
            }

            try
            {
                Send(request);

                var timeoutTask = Task.Delay(timeoutMs);
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    throw new TimeoutException("RPC request timed out");
                }

                return await tcs.Task;
            }
            finally
            {
                lock (_requestLock)
                {
                    _pendingRequests.Remove(request.Nonce);
                }
            }
        }

        private void Send(object payload)
        {
            if (!IsConnected || _webSocket == null || !_webSocket.IsAlive)
                return;

            var json = JsonConvert.SerializeObject(payload, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });

            _webSocket.Send(json);
        }

        private void OnMessageReceived(object sender, MessageEventArgs e)
        {
            if (!e.IsText)
                return;

            try
            {
                var json = e.Data;
                FileLogger.Log($"RPC: Received message: {json}");

                var response = JsonConvert.DeserializeObject<RPCResponse>(json);

                if (response == null)
                    return;

                // Handle response to pending request
                if (!string.IsNullOrEmpty(response.Nonce))
                {
                    lock (_requestLock)
                    {
                        if (_pendingRequests.TryGetValue(response.Nonce, out var tcs))
                        {
                            tcs.TrySetResult(response);
                            return;
                        }
                    }
                }

                // Handle events
                if (!string.IsNullOrEmpty(response.Event) && response.Event != "ERROR")
                {
                    _ = Task.Run(() => HandleEventAsync(response));
                }
            }
            catch (Exception ex)
            {
                FileLogger.Log($"RPC: OnMessageReceived EXCEPTION - {ex.Message}");
            }
        }

        private async Task HandleEventAsync(RPCResponse response)
        {
            try
            {
                switch (response.Event)
                {
                    case "VOICE_STATE_UPDATE":
                    case "VOICE_STATE_CREATE":
                    case "VOICE_STATE_DELETE":
                        if (response.Data != null && VoiceStateUpdated != null)
                        {
                            var voiceState = response.Data.ToObject<VoiceStateData>();
                            await VoiceStateUpdated.Invoke(voiceState);
                        }
                        break;

                    case "SPEAKING_START":
                        if (response.Data != null && SpeakingStarted != null)
                        {
                            var speaking = response.Data.ToObject<SpeakingData>();
                            await SpeakingStarted.Invoke(speaking);
                        }
                        break;

                    case "SPEAKING_STOP":
                        if (response.Data != null && SpeakingStopped != null)
                        {
                            var speaking = response.Data.ToObject<SpeakingData>();
                            await SpeakingStopped.Invoke(speaking);
                        }
                        break;

                    case "VOICE_CHANNEL_SELECT":
                        if (response.Data != null && VoiceChannelSelected != null)
                        {
                            var channelSelect = response.Data.ToObject<VoiceChannelSelectData>();
                            await VoiceChannelSelected.Invoke(channelSelect);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                FileLogger.Log($"RPC: HandleEvent EXCEPTION - {ex.Message}");
            }
        }

        public void Dispose()
        {
            _isConnected = false;
            _isAuthorized = false;

            if (_webSocket != null)
            {
                try
                {
                    _webSocket.Close(CloseStatusCode.Normal, "Closing");
                }
                catch
                {
                    // Ignore disposal errors
                }

                _webSocket = null;
            }
        }
    }

    // RPC Data classes
    public class RPCRequest
    {
        [JsonProperty("cmd")]
        public string Command { get; set; }

        [JsonProperty("evt", NullValueHandling = NullValueHandling.Ignore)]
        public string Event { get; set; }

        [JsonProperty("args")]
        public Dictionary<string, object> Args { get; set; }

        [JsonProperty("nonce")]
        public string Nonce { get; set; }
    }

    public class RPCResponse
    {
        [JsonProperty("cmd")]
        public string Command { get; set; }

        [JsonProperty("evt")]
        public string Event { get; set; }

        [JsonProperty("data")]
        public JObject Data { get; set; }

        [JsonProperty("nonce")]
        public string Nonce { get; set; }
    }

    public class VoiceStateData
    {
        [JsonProperty("mute")]
        public bool Mute { get; set; }

        [JsonProperty("deaf")]
        public bool Deaf { get; set; }

        [JsonProperty("self_mute")]
        public bool SelfMute { get; set; }

        [JsonProperty("self_deaf")]
        public bool SelfDeaf { get; set; }

        [JsonProperty("suppress")]
        public bool Suppress { get; set; }
    }

    public class SpeakingData
    {
        [JsonProperty("user_id")]
        public string UserId { get; set; }
    }

    public class VoiceChannelSelectData
    {
        [JsonProperty("channel_id")]
        public string ChannelId { get; set; }

        [JsonProperty("guild_id")]
        public string GuildId { get; set; }
    }

    public class RPCGuild
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("icon_url")]
        public string IconUrl { get; set; }
    }

    public class RPCChannel
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("type")]
        public int Type { get; set; }

        [JsonProperty("guild_id")]
        public string GuildId { get; set; }

        [JsonProperty("position")]
        public int Position { get; set; }
    }
}
