using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Controls;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;
using WigiDash.DiscordWidget.Core;
using WigiDash.DiscordWidget.Actions;
using WigiDash.DiscordWidget.Rendering;
using WigiDash.DiscordWidget.Authentication;

namespace WigiDash.DiscordWidget
{
    public class DiscordWidgetInstance : IWidgetInstance, IDisposable
    {
        private readonly DiscordWidgetObject _widgetObject;
        private readonly Guid _instanceGuid;
        private readonly WidgetSize _widgetSize;
        private readonly string _resourcePath;
        private readonly object _bitmapLock = new object();

        private Bitmap _currentBitmap;
        private bool _isDisposed;
        private bool _isInitialized;

        // Discord Managers
        private DiscordGatewayManager _gatewayManager;
        private DiscordRPCManager _rpcManager;
        private DiscordRESTClient _restClient;

        // Rendering
        private BitmapRenderer _renderer;
        private ImageCache _imageCache;

        // Current Action
        private IActionHandler _currentAction;
        private string _currentActionType;

        // Settings
        private string _accessToken;

        // IWidgetInstance properties
        public IWidgetObject WidgetObject => _widgetObject;
        public Guid Guid => _instanceGuid;
        public WidgetSize WidgetSize => _widgetSize;

        // Events
        public event WidgetUpdatedEventHandler WidgetUpdated;

        public DiscordWidgetInstance(
            DiscordWidgetObject widgetObject,
            WidgetSize widgetSize,
            Guid instanceGuid,
            string resourcePath)
        {
            _widgetObject = widgetObject;
            _widgetSize = widgetSize;
            _instanceGuid = instanceGuid;
            _resourcePath = resourcePath;

            // Initialize rendering components
            _renderer = new BitmapRenderer();
            _imageCache = new ImageCache(resourcePath);

            // Load settings
            LoadSettings();

            // Initialize with default action if not set
            if (string.IsNullOrEmpty(_currentActionType))
            {
                _currentActionType = "voice_channel"; // Default action
            }

            // Create initial bitmap
            UpdateBitmap(CreateInitialBitmap());

            Log("Discord Widget instance created", LogLevel.INFO);
        }

        public void RequestUpdate()
        {
            if (_isDisposed) return;

            Task.Run(async () =>
            {
                try
                {
                    await RegenerateBitmapAsync();
                }
                catch (Exception ex)
                {
                    Log($"Error during update: {ex.Message}", LogLevel.ERROR);
                }
            });
        }

        public void ClickEvent(ClickType clickType, int x, int y)
        {
            if (_isDisposed) return;

            if (clickType == ClickType.Single)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        if (_currentAction != null)
                        {
                            await _currentAction.OnClickAsync(x, y);
                        }
                        else
                        {
                            Log("No action configured", LogLevel.WARN);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Error handling click: {ex.Message}", LogLevel.ERROR);
                    }
                });
            }
        }

        public UserControl GetSettingsControl()
        {
            try
            {
                return new UI.DiscordSettingsControl(this);
            }
            catch (Exception ex)
            {
                Log($"Error creating settings control: {ex.Message}", LogLevel.ERROR);
                return null;
            }
        }

        public void EnterSleep()
        {
            if (_isDisposed) return;

            try
            {
                // Disconnect Discord managers to save resources
                _gatewayManager?.Dispose();
                _rpcManager?.Dispose();
                _restClient?.Dispose();

                _gatewayManager = null;
                _rpcManager = null;
                _restClient = null;

                _isInitialized = false;

                Log("Entered sleep mode", LogLevel.INFO);
            }
            catch (Exception ex)
            {
                Log($"Error entering sleep: {ex.Message}", LogLevel.ERROR);
            }
        }

        public void ExitSleep()
        {
            if (_isDisposed) return;

            try
            {
                // Reinitialize if we have a token
                if (!string.IsNullOrEmpty(_accessToken))
                {
                    Task.Run(async () =>
                    {
                        await InitializeDiscordAsync();
                        await RegenerateBitmapAsync();
                    });
                }

                Log("Exited sleep mode", LogLevel.INFO);
            }
            catch (Exception ex)
            {
                Log($"Error exiting sleep: {ex.Message}", LogLevel.ERROR);
            }
        }

        public async Task<AuthenticationResult> AuthenticateAsync()
        {
            try
            {
                var oauth = new OAuth2Handler();
                var result = await oauth.AuthenticateAsync();

                if (result.Success)
                {
                    _accessToken = result.AccessToken;

                    // Save token (encrypted)
                    SaveSettings();

                    // Initialize Discord connections
                    await InitializeDiscordAsync();

                    // Regenerate bitmap
                    await RegenerateBitmapAsync();

                    Log("Authentication successful", LogLevel.INFO);
                }

                return result;
            }
            catch (Exception ex)
            {
                Log($"Authentication error: {ex.Message}", LogLevel.ERROR);
                return new AuthenticationResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public IActionHandler LoadActionHandler(string actionType)
        {
            try
            {
                _currentActionType = actionType;

                // Dispose old action
                _currentAction?.Dispose();

                // Create action context
                var context = new ActionContext
                {
                    GatewayManager = _gatewayManager,
                    RPCManager = _rpcManager,
                    RESTClient = _restClient,
                    Renderer = _renderer,
                    ImageCache = _imageCache,
                    WidgetManager = _widgetObject.WidgetManager
                };

                // Create new action handler based on type
                _currentAction = ActionFactory.CreateAction(actionType, context);

                if (_currentAction != null)
                {
                    // Initialize action
                    Task.Run(async () =>
                    {
                        await _currentAction.InitializeAsync(context);
                    });
                }

                return _currentAction;
            }
            catch (Exception ex)
            {
                Log($"Error loading action handler: {ex.Message}", LogLevel.ERROR);
                return null;
            }
        }

        public void SaveWidgetSettings(Dictionary<string, string> settings)
        {
            try
            {
                var mgr = _widgetObject.WidgetManager;

                foreach (var kvp in settings)
                {
                    if (kvp.Key == "accessToken")
                    {
                        // Encrypt token before saving
                        var encrypted = TokenManager.EncryptToken(kvp.Value);
                        mgr.StoreSetting(this, kvp.Key, encrypted);
                    }
                    else
                    {
                        mgr.StoreSetting(this, kvp.Key, kvp.Value);
                    }
                }

                Log("Settings saved", LogLevel.INFO);
            }
            catch (Exception ex)
            {
                Log($"Error saving settings: {ex.Message}", LogLevel.ERROR);
            }
        }

        private void LoadSettings()
        {
            try
            {
                var mgr = _widgetObject.WidgetManager;

                // Load action type
                if (mgr.LoadSetting(this, "actionType", out string actionType))
                {
                    _currentActionType = actionType;
                }

                // Load access token
                if (mgr.LoadSetting(this, "accessToken", out string encryptedToken))
                {
                    _accessToken = TokenManager.DecryptToken(encryptedToken);
                }

                Log("Settings loaded", LogLevel.INFO);
            }
            catch (Exception ex)
            {
                Log($"Error loading settings: {ex.Message}", LogLevel.WARN);
            }
        }

        private void SaveSettings()
        {
            var settings = new Dictionary<string, string>
            {
                ["actionType"] = _currentActionType ?? "voice_channel",
                ["accessToken"] = _accessToken ?? string.Empty
            };

            SaveWidgetSettings(settings);
        }

        private async Task InitializeDiscordAsync()
        {
            if (_isInitialized || string.IsNullOrEmpty(_accessToken))
                return;

            try
            {
                // Initialize Gateway Manager
                _gatewayManager = new DiscordGatewayManager();
                var gatewayConnected = await _gatewayManager.ConnectAsync(_accessToken, Discord.TokenType.Bearer);

                if (!gatewayConnected)
                {
                    Log("Failed to connect to Discord Gateway", LogLevel.ERROR);
                }

                // Initialize RPC Manager
                _rpcManager = new DiscordRPCManager();
                var rpcConnected = await _rpcManager.ConnectAsync(DiscordConfig.CLIENT_ID);

                if (rpcConnected)
                {
                    await _rpcManager.AuthorizeAsync(_accessToken);
                }
                else
                {
                    Log("Failed to connect to Discord RPC (client may not be running)", LogLevel.WARN);
                }

                // Initialize REST Client
                _restClient = new DiscordRESTClient(_accessToken);

                _isInitialized = true;

                Log("Discord managers initialized", LogLevel.INFO);
            }
            catch (Exception ex)
            {
                Log($"Error initializing Discord: {ex.Message}", LogLevel.ERROR);
            }
        }

        private async Task RegenerateBitmapAsync()
        {
            if (_isDisposed) return;

            try
            {
                Bitmap newBitmap;

                if (_currentAction != null && _isInitialized)
                {
                    newBitmap = await _currentAction.RenderBitmapAsync(_widgetSize);
                }
                else
                {
                    newBitmap = CreateNotConfiguredBitmap();
                }

                UpdateBitmap(newBitmap);
            }
            catch (Exception ex)
            {
                Log($"Error regenerating bitmap: {ex.Message}", LogLevel.ERROR);
                UpdateBitmap(CreateErrorBitmap(ex.Message));
            }
        }

        private void UpdateBitmap(Bitmap newBitmap)
        {
            if (_isDisposed || newBitmap == null) return;

            Bitmap oldBitmap;

            lock (_bitmapLock)
            {
                oldBitmap = _currentBitmap;
                _currentBitmap = newBitmap;
            }

            // Fire update event
            WidgetUpdated?.Invoke(this, new WidgetUpdatedEventArgs
            {
                WidgetBitmap = newBitmap
            });

            // Dispose old bitmap
            oldBitmap?.Dispose();
        }

        private Bitmap CreateInitialBitmap()
        {
            return _renderer.CreateSimpleTextBitmap(
                _widgetSize,
                "Discord Widget",
                "Click to configure",
                Color.FromArgb(88, 101, 242)
            );
        }

        private Bitmap CreateNotConfiguredBitmap()
        {
            return _renderer.CreateSimpleTextBitmap(
                _widgetSize,
                "Not Configured",
                "Open settings",
                Color.FromArgb(237, 66, 69)
            );
        }

        private Bitmap CreateErrorBitmap(string error)
        {
            return _renderer.CreateSimpleTextBitmap(
                _widgetSize,
                "Error",
                error,
                Color.FromArgb(237, 66, 69)
            );
        }

        private void Log(string message, LogLevel level = LogLevel.INFO)
        {
            _widgetObject.WidgetManager?.WriteLogMessage(
                this,
                level,
                message,
                $"[Discord Widget {_instanceGuid}] {message}"
            );
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            try
            {
                _isDisposed = true;

                // Dispose current action
                _currentAction?.Dispose();

                // Dispose Discord managers
                _gatewayManager?.Dispose();
                _rpcManager?.Dispose();
                _restClient?.Dispose();

                // Dispose rendering components
                _renderer?.Dispose();
                _imageCache?.Dispose();

                // Dispose current bitmap
                lock (_bitmapLock)
                {
                    _currentBitmap?.Dispose();
                    _currentBitmap = null;
                }

                Log("Discord Widget instance disposed", LogLevel.INFO);
            }
            catch (Exception ex)
            {
                _widgetObject.WidgetManager?.WriteLogMessage(
                    null,
                    LogLevel.ERROR,
                    "Disposal error",
                    $"[Discord Widget {_instanceGuid}] Error during disposal: {ex}"
                );
            }
        }
    }
}
