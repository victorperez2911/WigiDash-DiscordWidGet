using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WigiDash.DiscordWidget.Actions;
using WigiDash.DiscordWidget.Core;

namespace WigiDash.DiscordWidget.UI.ActionConfigPanels
{
    public partial class VoiceChannelConfigPanel : UserControl
    {
        private readonly VoiceChannelAction _action;
        private readonly ActionContext _context;
        private Dictionary<string, string> _guilds = new Dictionary<string, string>();
        private Dictionary<string, Dictionary<string, string>> _guildChannels = new Dictionary<string, Dictionary<string, string>>();

        public VoiceChannelConfigPanel(VoiceChannelAction action, ActionContext context)
        {
            InitializeComponent();

            _action = action;
            _context = context;

            // Wire up events
            ServerComboBox.SelectionChanged += ServerComboBox_SelectionChanged;

            // Load existing settings
            LoadSettings();

            // Load guilds from RPC if connected
            RefreshGuildsList();
        }

        public void RefreshGuildsList()
        {
            // Load guilds from RPC if connected
            if (_context.RPCManager != null && _context.RPCManager.IsAuthorized)
            {
                LoadGuildsAsync();
            }
            else
            {
                StatusText.Text = "⚠ Not connected to Discord RPC. Connecting...";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(255, 165, 0)); // Orange
            }
        }

        private void LoadSettings()
        {
            var settings = _action.GetSettings();

            if (settings.TryGetValue("title", out var title) && !string.IsNullOrEmpty(title))
            {
                TitleTextBox.Text = title;
            }

            if (settings.TryGetValue("channel_name", out var channelName) && !string.IsNullOrEmpty(channelName))
            {
                // Will be set after loading channels
            }
        }

        private async void LoadGuildsAsync()
        {
            try
            {
                StatusText.Text = "Loading servers...";

                var guilds = await _context.RPCManager.GetGuildsAsync();

                if (guilds == null || guilds.Count == 0)
                {
                    StatusText.Text = "⚠ No servers found or not connected";
                    StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(237, 66, 69));
                    return;
                }

                _guilds.Clear();
                ServerComboBox.Items.Clear();

                foreach (var guild in guilds)
                {
                    _guilds[guild.Id] = guild.Name;

                    var item = new ComboBoxItem
                    {
                        Content = guild.Name,
                        Tag = guild.Id
                    };
                    ServerComboBox.Items.Add(item);
                }

                if (ServerComboBox.Items.Count > 0)
                {
                    ServerComboBox.SelectedIndex = 0;
                    StatusText.Text = "✓ Connected to Discord";
                    StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(67, 181, 129));
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error loading servers: {ex.Message}";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(237, 66, 69));
            }
        }

        private async void ServerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerComboBox.SelectedItem is ComboBoxItem selected && selected.Tag is string guildId)
            {
                VoiceChannelComboBox.Items.Clear();
                VoiceChannelComboBox.Items.Add(new ComboBoxItem
                {
                    Content = "Loading channels...",
                    IsEnabled = false
                });

                try
                {
                    var channels = await _context.RPCManager.GetChannelsAsync(guildId);

                    if (channels == null || channels.Count == 0)
                    {
                        VoiceChannelComboBox.Items.Clear();
                        VoiceChannelComboBox.Items.Add(new ComboBoxItem
                        {
                            Content = "No voice channels found",
                            IsEnabled = false
                        });
                        return;
                    }

                    var channelDict = new Dictionary<string, string>();
                    foreach (var channel in channels)
                    {
                        channelDict[channel.Id] = channel.Name;
                    }

                    _guildChannels[guildId] = channelDict;

                    VoiceChannelComboBox.Items.Clear();

                    foreach (var channel in channelDict)
                    {
                        var item = new ComboBoxItem
                        {
                            Content = $"🔊 {channel.Value}",
                            Tag = channel.Key
                        };
                        VoiceChannelComboBox.Items.Add(item);
                    }

                    if (VoiceChannelComboBox.Items.Count > 0)
                    {
                        VoiceChannelComboBox.SelectedIndex = 0;
                    }
                }
                catch (Exception ex)
                {
                    VoiceChannelComboBox.Items.Clear();
                    VoiceChannelComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = $"Error: {ex.Message}",
                        IsEnabled = false
                    });
                }
            }
        }

        public Dictionary<string, string> GetSettings()
        {
            var settings = new Dictionary<string, string>();

            settings["title"] = TitleTextBox.Text;

            if (ServerComboBox.SelectedItem is ComboBoxItem serverItem && serverItem.Tag is string guildId)
            {
                settings["guild_id"] = guildId;
                settings["guild_name"] = serverItem.Content.ToString();
            }

            if (VoiceChannelComboBox.SelectedItem is ComboBoxItem channelItem && channelItem.Tag is string channelId)
            {
                settings["channel_id"] = channelId;
                settings["channel_name"] = channelItem.Content.ToString().Replace("🔊 ", "");
            }

            settings["icon_style"] = IconStyleComboBox.SelectedIndex == 0 ? "dynamic" : "static";

            return settings;
        }
    }
}
