using System;
using System.Threading.Tasks;
using WigiDash.DiscordWidget.Authentication;

namespace WigiDash.DiscordWidget.Core
{
    /// <summary>
    /// Singleton manager for Discord RPC connection.
    /// Discord RPC is per-user, not per-widget, so all widget instances share this connection.
    /// </summary>
    public static class GlobalRPCManager
    {
        private static DiscordRPCManager _instance;
        private static readonly object _lock = new object();
        private static bool _isInitializing = false;
        private static Task<bool> _initializationTask;

        public static DiscordRPCManager Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new DiscordRPCManager();
                    }
                    return _instance;
                }
            }
        }

        public static bool IsConnected
        {
            get
            {
                lock (_lock)
                {
                    return _instance != null && _instance.IsConnected;
                }
            }
        }

        public static bool IsAuthorized
        {
            get
            {
                lock (_lock)
                {
                    return _instance != null && _instance.IsAuthorized;
                }
            }
        }

        /// <summary>
        /// Initialize RPC connection if not already connected.
        /// This method is thread-safe and will only initialize once.
        /// </summary>
        public static async Task<bool> InitializeAsync()
        {
            lock (_lock)
            {
                // If already connected and authorized, return success
                if (_instance != null && _instance.IsConnected && _instance.IsAuthorized)
                {
                    FileLogger.Log("GlobalRPC: Already connected and authorized");
                    return true;
                }

                // If initialization is in progress, return existing task
                if (_isInitializing && _initializationTask != null)
                {
                    FileLogger.Log("GlobalRPC: Initialization already in progress, waiting...");
                }
                else
                {
                    // Start new initialization
                    _isInitializing = true;
                    _initializationTask = InitializeInternalAsync();
                }
            }

            // Wait for initialization outside the lock
            return await _initializationTask;
        }

        private static async Task<bool> InitializeInternalAsync()
        {
            try
            {
                FileLogger.Log("GlobalRPC: Starting initialization...");

                // Get global auth token
                if (!GlobalAuthManager.IsAuthenticated)
                {
                    FileLogger.Log("GlobalRPC: Cannot initialize - not authenticated");
                    return false;
                }

                var accessToken = GlobalAuthManager.AccessToken;

                // Create instance if needed
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new DiscordRPCManager();
                        }
                    }
                }

                // Check if already connected and authorized (another thread may have finished)
                if (_instance.IsConnected && _instance.IsAuthorized)
                {
                    FileLogger.Log("GlobalRPC: Already connected and authorized (by another thread)");
                    return true;
                }

                // Connect to RPC WebSocket (only if not connected)
                if (!_instance.IsConnected)
                {
                    FileLogger.Log("GlobalRPC: Connecting to Discord RPC...");
                    var connected = await _instance.ConnectAsync(DiscordConfig.CLIENT_ID);

                    if (!connected)
                    {
                        FileLogger.Log("GlobalRPC: Failed to connect to Discord RPC");
                        return false;
                    }
                }
                else
                {
                    FileLogger.Log("GlobalRPC: Already connected to RPC WebSocket");
                }

                // Authenticate with OAuth2 token (only if not authorized)
                if (!_instance.IsAuthorized)
                {
                    FileLogger.Log("GlobalRPC: RPC connected, authenticating...");
                    var authenticated = await _instance.AuthenticateAsync(accessToken);

                    if (!authenticated)
                    {
                        FileLogger.Log("GlobalRPC: Failed to authenticate RPC");
                        return false;
                    }
                }
                else
                {
                    FileLogger.Log("GlobalRPC: Already authorized");
                }

                // Final success check
                if (!_instance.IsAuthorized)
                {
                    FileLogger.Log("GlobalRPC: Authorization verification failed");
                    return false;
                }

                FileLogger.Log("GlobalRPC: Initialization SUCCESS!");
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.Log($"GlobalRPC: Initialization EXCEPTION - {ex.Message}");
                return false;
            }
            finally
            {
                lock (_lock)
                {
                    _isInitializing = false;
                }
            }
        }

        /// <summary>
        /// Disconnect and dispose RPC connection.
        /// </summary>
        public static void Disconnect()
        {
            lock (_lock)
            {
                FileLogger.Log("GlobalRPC: Disconnecting...");
                _instance?.Dispose();
                _instance = null;
                _isInitializing = false;
                _initializationTask = null;
            }
        }

        /// <summary>
        /// Logout - clears RPC connection (alias for Disconnect).
        /// </summary>
        public static void Logout()
        {
            Disconnect();
        }
    }
}
