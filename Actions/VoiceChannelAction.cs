using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using WigiDashWidgetFramework.WidgetUtility;
using WigiDash.DiscordWidget.Core;

namespace WigiDash.DiscordWidget.Actions
{
    public class VoiceChannelAction : IActionHandler
    {
        private ActionContext _context;
        private string _configuredTitle;
        private string _configuredGuildId;
        private string _configuredGuildName;
        private string _configuredChannelId;
        private string _configuredChannelName;
        private string _iconStyle;
        private string _currentChannelId;
        private string _currentGuildId;
        private bool _isConnected;
        private int _userCount;
        private Dictionary<string, bool> _speakingUsers = new Dictionary<string, bool>();
        private Bitmap _cachedAvatar;
        private DateTime _lastRender = DateTime.MinValue;
        private readonly object _stateLock = new object();

        public string ActionId => "voice_channel";
        public string DisplayName => "Voice Channel Status";
        public string Description => "Shows current voice channel, connects/disconnects on click";
        public ActionAPIType APIType => ActionAPIType.RPC;

        public void SetContext(ActionContext context)
        {
            _context = context;
        }

        public async Task InitializeAsync(ActionContext context)
        {
            _context = context;

            if (_context.RPCManager != null)
            {
                // Subscribe to events
                _context.RPCManager.VoiceChannelSelected += OnVoiceChannelSelected;
                _context.RPCManager.SpeakingStarted += OnSpeakingStarted;
                _context.RPCManager.SpeakingStopped += OnSpeakingStopped;

                // Subscribe to voice channel select events
                if (_context.RPCManager.IsAuthorized)
                {
                    await _context.RPCManager.SubscribeAsync("VOICE_CHANNEL_SELECT");
                    await _context.RPCManager.SubscribeAsync("SPEAKING_START", new { channel_id = _currentChannelId });
                    await _context.RPCManager.SubscribeAsync("SPEAKING_STOP", new { channel_id = _currentChannelId });
                }
            }

            await Task.CompletedTask;
        }

        public async Task<Bitmap> RenderBitmapAsync(WidgetSize size)
        {
            // Throttle rendering (max 10 FPS)
            if ((DateTime.Now - _lastRender).TotalMilliseconds < 100)
            {
                return null; // Return null to keep previous bitmap
            }

            _lastRender = DateTime.Now;

            try
            {
                string channelName;
                bool isConnected;
                int userCount;
                bool isSpeaking;
                Bitmap avatar;

                lock (_stateLock)
                {
                    channelName = _isConnected ? _currentChannelId : (_configuredChannelName ?? "Not Set");
                    isConnected = _isConnected;
                    userCount = _userCount;
                    isSpeaking = _speakingUsers.Values.Any(s => s);
                    avatar = _cachedAvatar;
                }

                var backgroundColor = isConnected
                    ? Color.FromArgb(67, 181, 129)  // Green when connected
                    : Color.FromArgb(88, 101, 242);  // Discord blurple when disconnected

                return _context.Renderer.CreateVoiceChannelBitmap(
                    size,
                    avatar,
                    channelName,
                    userCount,
                    isConnected,
                    isSpeaking,
                    backgroundColor
                );
            }
            catch (Exception ex)
            {
                return _context.Renderer.CreateErrorBitmap(size, ex.Message);
            }
        }

        public async Task OnClickAsync(int x, int y)
        {
            try
            {
                // Check prerequisites
                var prereq = await ValidatePrerequisitesAsync();
                if (!prereq.IsValid)
                {
                    return;
                }

                lock (_stateLock)
                {
                    if (_isConnected)
                    {
                        // Disconnect
                        Task.Run(async () =>
                        {
                            await _context.RPCManager.SelectVoiceChannelAsync(null);
                        });
                    }
                    else
                    {
                        // Connect to configured channel
                        if (!string.IsNullOrEmpty(_configuredChannelId))
                        {
                            Task.Run(async () =>
                            {
                                await _context.RPCManager.SelectVoiceChannelAsync(_configuredChannelId);
                            });
                        }
                    }
                }
            }
            catch
            {
                // Ignore click errors
            }
        }

        public UserControl GetConfigurationPanel()
        {
            if (_context == null)
            {
                // Context not yet initialized
                return null;
            }

            return new UI.ActionConfigPanels.VoiceChannelConfigPanel(this, _context);
        }

        public Dictionary<string, string> GetSettings()
        {
            return new Dictionary<string, string>
            {
                ["title"] = _configuredTitle ?? "",
                ["guild_id"] = _configuredGuildId ?? "",
                ["guild_name"] = _configuredGuildName ?? "",
                ["channel_id"] = _configuredChannelId ?? "",
                ["channel_name"] = _configuredChannelName ?? "",
                ["icon_style"] = _iconStyle ?? "dynamic"
            };
        }

        public void LoadSettings(Dictionary<string, string> settings)
        {
            if (settings.TryGetValue("title", out var title))
                _configuredTitle = title;

            if (settings.TryGetValue("guild_id", out var guildId))
                _configuredGuildId = guildId;

            if (settings.TryGetValue("guild_name", out var guildName))
                _configuredGuildName = guildName;

            if (settings.TryGetValue("channel_id", out var channelId))
                _configuredChannelId = channelId;

            if (settings.TryGetValue("channel_name", out var channelName))
                _configuredChannelName = channelName;

            if (settings.TryGetValue("icon_style", out var iconStyle))
                _iconStyle = iconStyle;
        }

        public List<TemplateVariable> GetAvailableVariables()
        {
            return new List<TemplateVariable>
            {
                new TemplateVariable
                {
                    Name = "{channelName}",
                    Description = "Name of the current voice channel",
                    ExampleValue = "General"
                },
                new TemplateVariable
                {
                    Name = "{userCount}",
                    Description = "Number of users in the channel",
                    ExampleValue = "5"
                },
                new TemplateVariable
                {
                    Name = "{guildName}",
                    Description = "Name of the server/guild",
                    ExampleValue = "My Server"
                }
            };
        }

        public async Task<PrerequisiteResult> ValidatePrerequisitesAsync()
        {
            if (_context?.RPCManager == null || !_context.RPCManager.IsConnected)
            {
                return PrerequisiteResult.Failure("Discord RPC not connected. Make sure Discord is running.");
            }

            if (!_context.RPCManager.IsAuthorized)
            {
                return PrerequisiteResult.Failure("Not authorized. Please login with Discord.");
            }

            if (string.IsNullOrEmpty(_configuredChannelId))
            {
                return PrerequisiteResult.Failure("No voice channel configured. Please configure the action.");
            }

            return PrerequisiteResult.Success();
        }

        private async Task OnVoiceChannelSelected(VoiceChannelSelectData data)
        {
            try
            {
                lock (_stateLock)
                {
                    _currentChannelId = data.ChannelId;
                    _currentGuildId = data.GuildId;
                    _isConnected = !string.IsNullOrEmpty(data.ChannelId);

                    // Reset speaking users when changing channels
                    _speakingUsers.Clear();
                }

                // Resubscribe to events for new channel
                if (_context.RPCManager.IsAuthorized && !string.IsNullOrEmpty(_currentChannelId))
                {
                    await _context.RPCManager.SubscribeAsync("SPEAKING_START", new { channel_id = _currentChannelId });
                    await _context.RPCManager.SubscribeAsync("SPEAKING_STOP", new { channel_id = _currentChannelId });
                }

                // Trigger re-render
                _lastRender = DateTime.MinValue;
            }
            catch
            {
                // Ignore event errors
            }
        }

        private Task OnSpeakingStarted(SpeakingData data)
        {
            try
            {
                lock (_stateLock)
                {
                    _speakingUsers[data.UserId] = true;
                }

                // Trigger re-render
                _lastRender = DateTime.MinValue;
            }
            catch
            {
                // Ignore event errors
            }

            return Task.CompletedTask;
        }

        private Task OnSpeakingStopped(SpeakingData data)
        {
            try
            {
                lock (_stateLock)
                {
                    _speakingUsers[data.UserId] = false;
                }

                // Trigger re-render
                _lastRender = DateTime.MinValue;
            }
            catch
            {
                // Ignore event errors
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_context?.RPCManager != null)
            {
                _context.RPCManager.VoiceChannelSelected -= OnVoiceChannelSelected;
                _context.RPCManager.SpeakingStarted -= OnSpeakingStarted;
                _context.RPCManager.SpeakingStopped -= OnSpeakingStopped;
            }

            _cachedAvatar?.Dispose();
        }
    }
}
