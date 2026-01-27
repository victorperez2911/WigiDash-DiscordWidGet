using System;
using System.Windows;
using System.Windows.Controls;

namespace WigiDash.DiscordWidget.UI
{
    public partial class DiscordSettingsControl : UserControl
    {
        private readonly DiscordWidgetInstance _widgetInstance;

        public DiscordSettingsControl(DiscordWidgetInstance widgetInstance)
        {
            InitializeComponent();

            _widgetInstance = widgetInstance;

            // Wire up events
            LoginButton.Click += LoginButton_Click;
            ApplyButton.Click += ApplyButton_Click;

            LoadActionTypes();
        }

        private void LoadActionTypes()
        {
            var types = new[]
            {
                new { ActionId = "voice_channel", DisplayName = "Voice Channel Status" },
                new { ActionId = "soundboard", DisplayName = "Soundboard" },
                new { ActionId = "mute_toggle", DisplayName = "Mute Toggle" },
                new { ActionId = "deafen_toggle", DisplayName = "Deafen Toggle" },
                new { ActionId = "audio_device", DisplayName = "Set Audio Device" },
                new { ActionId = "server_stats", DisplayName = "Server Stats" },
                new { ActionId = "video_toggle", DisplayName = "Video Toggle (Coming Soon)" },
                new { ActionId = "stream_toggle", DisplayName = "Stream Toggle (Coming Soon)" }
            };

            ActionTypeComboBox.ItemsSource = types;
            ActionTypeComboBox.SelectedIndex = 0;
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            LoginButton.IsEnabled = false;
            AuthStatusText.Text = "Opening browser...";

            try
            {
                var result = await _widgetInstance.AuthenticateAsync();

                if (result.Success)
                {
                    AuthStatusText.Text = "✓ Authenticated";
                    AuthStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(87, 242, 135)
                    );
                }
                else
                {
                    AuthStatusText.Text = $"✗ Error: {result.Error}";
                    AuthStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Colors.Red
                    );
                }
            }
            finally
            {
                LoginButton.IsEnabled = true;
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Settings will be applied in future implementation",
                "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
