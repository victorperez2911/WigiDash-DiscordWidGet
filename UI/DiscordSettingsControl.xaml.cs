using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WigiDash.DiscordWidget.Actions;
using WigiDash.DiscordWidget.UI.ActionConfigPanels;

namespace WigiDash.DiscordWidget.UI
{
    public partial class DiscordSettingsControl : UserControl
    {
        private readonly DiscordWidgetInstance _widgetInstance;
        private IActionHandler _currentActionHandler;
        private UserControl _currentConfigPanel;

        public DiscordSettingsControl(DiscordWidgetInstance widgetInstance)
        {
            InitializeComponent();

            _widgetInstance = widgetInstance;

            // Wire up events
            LoginButton.Click += LoginButton_Click;
            ApplyButton.Click += ApplyButton_Click;
            ActionTypeComboBox.SelectionChanged += ActionTypeComboBox_SelectionChanged;

            LoadActionTypes();

            // Check if already authenticated
            CheckAuthenticationStatus();
        }

        private void LoadActionTypes()
        {
            var types = new[]
            {
                new { ActionId = "voice_channel", DisplayName = "Voice Channel Status" },
                new { ActionId = "soundboard", DisplayName = "Soundboard (Coming Soon)" },
                new { ActionId = "mute_toggle", DisplayName = "Mute Toggle (Coming Soon)" },
                new { ActionId = "deafen_toggle", DisplayName = "Deafen Toggle (Coming Soon)" },
                new { ActionId = "audio_device", DisplayName = "Set Audio Device (Coming Soon)" },
                new { ActionId = "server_stats", DisplayName = "Server Stats (Coming Soon)" },
                new { ActionId = "video_toggle", DisplayName = "Video Toggle (Coming Soon)" },
                new { ActionId = "stream_toggle", DisplayName = "Stream Toggle (Coming Soon)" }
            };

            ActionTypeComboBox.ItemsSource = types;
            ActionTypeComboBox.SelectedIndex = 0;
        }

        private void CheckAuthenticationStatus()
        {
            // Check if widget instance has an access token
            if (_widgetInstance.IsAuthenticated())
            {
                // Update button to Logout
                LoginButton.Content = "Logout";

                AuthStatusText.Text = "✓ Authenticated";
                AuthStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(87, 242, 135)
                );

                // Enable configuration inputs
                ActionTypeComboBox.IsEnabled = true;
                ApplyButton.IsEnabled = true;
            }
            else
            {
                // Update button to Login
                LoginButton.Content = "Login with Discord";

                AuthStatusText.Text = "Not authenticated";
                AuthStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(128, 128, 128)
                );

                // Disable configuration inputs until authenticated
                ActionTypeComboBox.IsEnabled = false;
                ApplyButton.IsEnabled = false;
            }
        }

        private void ActionTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ActionTypeComboBox.SelectedValue == null)
                return;

            var actionId = ActionTypeComboBox.SelectedValue.ToString();

            try
            {
                // Load action handler
                _currentActionHandler = _widgetInstance.LoadActionHandler(actionId);

                if (_currentActionHandler != null)
                {
                    // Get configuration panel for this action
                    _currentConfigPanel = _currentActionHandler.GetConfigurationPanel();

                    if (_currentConfigPanel != null)
                    {
                        TypeSpecificPanelContainer.Child = _currentConfigPanel;
                        ApplyButton.IsEnabled = true;
                    }
                    else
                    {
                        TypeSpecificPanelContainer.Child = null;
                        ApplyButton.IsEnabled = false;
                        InfoText.Text = "This action type doesn't require additional configuration.";
                    }

                    // Update info text
                    UpdateInfoText(actionId);
                }
                else
                {
                    TypeSpecificPanelContainer.Child = null;
                    ApplyButton.IsEnabled = false;
                    InfoText.Text = "This action type is not yet implemented.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading action: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateInfoText(string actionId)
        {
            InfoText.Text = actionId switch
            {
                "voice_channel" => "• Shows voice channel status\n• Click to connect/disconnect\n• Displays user avatars and count\n• Green border when users speak",
                "soundboard" => "• Play soundboard sounds in voice channel\n• Requires connection to voice channel\n• Select server and sound",
                "mute_toggle" => "• Toggle microphone mute on/off\n• Works instantly\n• Visual feedback with color",
                "deafen_toggle" => "• Toggle deafen on/off\n• Works instantly\n• Visual feedback with color",
                "audio_device" => "• Switch audio input/output devices\n• Quick device selection\n• Apply changes instantly",
                "server_stats" => "• Display server information\n• Shows online member count\n• Customizable template",
                _ => "Select an action type to get started."
            };
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            LoginButton.IsEnabled = false;

            try
            {
                // Check current authentication state
                if (_widgetInstance.IsAuthenticated())
                {
                    // Currently logged in - perform logout
                    await PerformLogoutAsync();
                }
                else
                {
                    // Currently logged out - perform login
                    await PerformLoginAsync();
                }
            }
            finally
            {
                LoginButton.IsEnabled = true;
            }
        }

        private async Task PerformLoginAsync()
        {
            AuthStatusText.Text = "Opening browser...";

            var result = await _widgetInstance.AuthenticateAsync();

            if (result.Success)
            {
                AuthStatusText.Text = "✓ Authenticated - Connecting to Discord...";
                AuthStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(255, 165, 0) // Orange
                );

                // Update button text
                LoginButton.Content = "Logout";

                // Enable configuration inputs
                ActionTypeComboBox.IsEnabled = true;
                ApplyButton.IsEnabled = true;

                // Reload action config panel (may need guild/channel lists)
                if (ActionTypeComboBox.SelectedValue != null)
                {
                    ActionTypeComboBox_SelectionChanged(null, null);
                }

                // Wait for Discord connections to initialize (happens in background)
                // Then refresh guilds list
                _ = Task.Run(async () =>
                {
                    // Check if already connected immediately
                    if (_widgetInstance.IsRPCConnected())
                    {
                        // Already connected! Update UI immediately
                        Dispatcher.Invoke(() =>
                        {
                            AuthStatusText.Text = "✓ Authenticated & Connected";
                            AuthStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(87, 242, 135)
                            );

                            // Refresh guilds list if panel is loaded
                            if (_currentConfigPanel is VoiceChannelConfigPanel voicePanel)
                            {
                                voicePanel.RefreshGuildsList();
                            }
                        });
                        return;
                    }

                    // Wait up to 10 seconds for RPC to connect
                    bool connected = false;
                    for (int i = 0; i < 20; i++)
                    {
                        await Task.Delay(500);

                        if (_widgetInstance.IsRPCConnected())
                        {
                            connected = true;
                            // RPC connected! Refresh UI on main thread
                            Dispatcher.Invoke(() =>
                            {
                                AuthStatusText.Text = "✓ Authenticated & Connected";
                                AuthStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                                    System.Windows.Media.Color.FromRgb(87, 242, 135)
                                );

                                // Refresh guilds list if panel is loaded
                                if (_currentConfigPanel is VoiceChannelConfigPanel voicePanel)
                                {
                                    voicePanel.RefreshGuildsList();
                                }
                            });
                            break;
                        }
                    }

                    // If timeout, update status to show RPC didn't connect
                    if (!connected)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            AuthStatusText.Text = "✓ Authenticated (RPC not connected)";
                            AuthStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(255, 165, 0) // Orange - warning
                            );
                        });
                    }
                });
            }
            else
            {
                AuthStatusText.Text = $"✗ Error: {result.Error}";
                AuthStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Colors.Red
                );
            }
        }

        private Task PerformLogoutAsync()
        {
            AuthStatusText.Text = "Logging out...";

            // Call widget instance logout
            _widgetInstance.Logout();

            // Update UI
            LoginButton.Content = "Login with Discord";
            AuthStatusText.Text = "Logged out";
            AuthStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(128, 128, 128)
            );

            // Disable configuration inputs
            ActionTypeComboBox.IsEnabled = false;
            ApplyButton.IsEnabled = false;

            // Clear config panel
            TypeSpecificPanelContainer.Child = null;

            return Task.CompletedTask;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentActionHandler == null || _currentConfigPanel == null)
                return;

            try
            {
                // Get settings from config panel
                var settings = (_currentConfigPanel as VoiceChannelConfigPanel)?.GetSettings();

                if (settings != null)
                {
                    // Save action settings
                    _widgetInstance.SaveActionSettings(_currentActionHandler.ActionId, settings);

                    // Load settings back into action
                    _currentActionHandler.LoadSettings(settings);

                    // Request widget update
                    _widgetInstance.RequestUpdate();

                    MessageBox.Show("Settings applied successfully!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    ApplyButton.IsEnabled = false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying settings: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
