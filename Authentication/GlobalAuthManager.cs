using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WigiDash.DiscordWidget.Authentication
{
    /// <summary>
    /// Manages global authentication state shared across all widget instances
    /// </summary>
    public static class GlobalAuthManager
    {
        private static string _globalAccessToken;
        private static string _globalRefreshToken;
        private static DateTime _tokenExpiration;
        private static readonly object _lock = new object();
        private static readonly string _tokenFilePath;

        static GlobalAuthManager()
        {
            // Store global token in user's AppData
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var widgetDataPath = Path.Combine(appDataPath, "G.SKILL", "WigiDashManager", "WidgetData", "DiscordWidget");

            Directory.CreateDirectory(widgetDataPath);
            _tokenFilePath = Path.Combine(widgetDataPath, "global_auth.dat");

            // Load existing token on startup
            LoadGlobalToken();
        }

        public static bool IsAuthenticated
        {
            get
            {
                lock (_lock)
                {
                    return !string.IsNullOrEmpty(_globalAccessToken) && DateTime.UtcNow < _tokenExpiration;
                }
            }
        }

        public static string AccessToken
        {
            get
            {
                lock (_lock)
                {
                    return _globalAccessToken;
                }
            }
        }

        public static string RefreshToken
        {
            get
            {
                lock (_lock)
                {
                    return _globalRefreshToken;
                }
            }
        }

        public static void SetTokens(string accessToken, string refreshToken, int expiresInSeconds)
        {
            lock (_lock)
            {
                _globalAccessToken = accessToken;
                _globalRefreshToken = refreshToken;
                _tokenExpiration = DateTime.UtcNow.AddSeconds(expiresInSeconds);

                // Persist to disk (encrypted)
                SaveGlobalToken();
            }
        }

        public static void ClearTokens()
        {
            lock (_lock)
            {
                _globalAccessToken = null;
                _globalRefreshToken = null;
                _tokenExpiration = DateTime.MinValue;

                // Delete persisted token
                try
                {
                    if (File.Exists(_tokenFilePath))
                    {
                        File.Delete(_tokenFilePath);
                    }
                }
                catch
                {
                    // Ignore deletion errors
                }
            }
        }

        private static void SaveGlobalToken()
        {
            try
            {
                var data = $"{_globalAccessToken}|{_globalRefreshToken}|{_tokenExpiration:O}";
                var encrypted = TokenManager.EncryptToken(data);
                File.WriteAllText(_tokenFilePath, encrypted);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save global token: {ex.Message}");
            }
        }

        private static void LoadGlobalToken()
        {
            try
            {
                if (File.Exists(_tokenFilePath))
                {
                    var encrypted = File.ReadAllText(_tokenFilePath);
                    var decrypted = TokenManager.DecryptToken(encrypted);

                    var parts = decrypted.Split('|');
                    if (parts.Length == 3)
                    {
                        _globalAccessToken = parts[0];
                        _globalRefreshToken = parts[1];
                        _tokenExpiration = DateTime.Parse(parts[2]).ToUniversalTime();

                        System.Diagnostics.Debug.WriteLine("Global token loaded from disk");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load global token: {ex.Message}");
                // If loading fails, clear everything
                ClearTokens();
            }
        }
    }
}
