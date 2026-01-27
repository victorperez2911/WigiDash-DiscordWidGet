using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using WigiDashWidgetFramework;
using WigiDashWidgetFramework.WidgetUtility;

namespace WigiDash.DiscordWidget
{
    public class DiscordWidgetObject : IWidgetObject
    {
        private readonly Guid _guid = new Guid("3035795c-15ec-4b09-859d-27d1178c949c");
        private readonly Dictionary<Guid, DiscordWidgetInstance> _instances = new Dictionary<Guid, DiscordWidgetInstance>();
        private readonly object _instanceLock = new object();
        private string _resourcePath;

        // IWidgetBase properties
        public Guid Guid => _guid;
        public string Name => "Discord Widget";
        public string Author => "Victor Perez";
        public string Website => "https://github.com/victorperez2911";
        public string Description => "Multi-action Discord integration widget with Soundboard, Voice Control, and Server Stats";
        public Version Version => new Version(1, 0, 0);
        public SdkVersion TargetSdk => WidgetUtility.CurrentSdkVersion;
        public List<WidgetSize> SupportedSizes => new List<WidgetSize>
        {
            new WidgetSize(1, 1),
            new WidgetSize(1, 2),
            new WidgetSize(2, 1),
            new WidgetSize(2, 2)
        };

        public Bitmap PreviewImage => GetWidgetPreview(new WidgetSize(2, 2));

        // IWidgetObject properties
        public IWidgetManager WidgetManager { get; set; }
        public string LastErrorMessage { get; set; }

        public Bitmap WidgetThumbnail => PreviewImage;


        public WidgetError Load(string resourcePath)
        {
            try
            {
                _resourcePath = resourcePath;

                // Validate resource path
                if (!System.IO.Directory.Exists(resourcePath))
                {
                    LastErrorMessage = "Resource path does not exist";
                    return WidgetError.CUSTOM_ERROR;
                }

                return WidgetError.NO_ERROR;
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"Load failed: {ex.Message}";
                return WidgetError.CUSTOM_ERROR;
            }
        }

        public WidgetError Unload()
        {
            try
            {
                // Dispose all instances
                lock (_instanceLock)
                {
                    foreach (var instance in _instances.Values.ToList())
                    {
                        try
                        {
                            instance.Dispose();
                        }
                        catch
                        {
                            // Silent fail - widget is being unloaded anyway
                        }
                    }

                    _instances.Clear();
                }

                return WidgetError.NO_ERROR;
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"Unload failed: {ex.Message}";
                return WidgetError.CUSTOM_ERROR;
            }
        }

        public IWidgetInstance CreateWidgetInstance(WidgetSize widgetSize, Guid instanceGuid)
        {
            try
            {
                lock (_instanceLock)
                {
                    // Check if instance already exists
                    if (_instances.ContainsKey(instanceGuid))
                    {
                        LastErrorMessage = "Instance with this GUID already exists";
                        return null;
                    }

                    // Create new instance
                    var instance = new DiscordWidgetInstance(this, widgetSize, instanceGuid, _resourcePath);

                    _instances[instanceGuid] = instance;

                    WidgetManager?.WriteLogMessage(
                        instance,
                        LogLevel.INFO,
                        "Discord Widget instance created",
                        $"[Discord Widget] Instance {instanceGuid} created with size {widgetSize}"
                    );

                    return instance;
                }
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"Failed to create instance: {ex.Message}";
                WidgetManager?.WriteLogMessage(
                    null,
                    LogLevel.ERROR,
                    "Instance creation failed",
                    $"[Discord Widget] Error creating instance {instanceGuid}: {ex}"
                );
                return null;
            }
        }

        public bool RemoveWidgetInstance(Guid instanceGuid)
        {
            try
            {
                lock (_instanceLock)
                {
                    if (_instances.TryGetValue(instanceGuid, out var instance))
                    {
                        instance.Dispose();
                        _instances.Remove(instanceGuid);

                        WidgetManager?.WriteLogMessage(
                            null,
                            LogLevel.INFO,
                            "Discord Widget instance removed",
                            $"[Discord Widget] Instance {instanceGuid} removed"
                        );

                        return true;
                    }

                    LastErrorMessage = "Instance not found";
                    return false;
                }
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"Failed to remove instance: {ex.Message}";
                return false;
            }
        }

        public Bitmap GetWidgetPreview(WidgetSize widgetSize)
        {
            try
            {
                var size = widgetSize.ToSize();
                var bitmap = new Bitmap(size.Width, size.Height);

                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.FromArgb(88, 101, 242)); // Discord blurple

                    // Draw Discord logo placeholder
                    using (var font = new Font("Segoe UI", 18, FontStyle.Bold))
                    using (var brush = new SolidBrush(Color.White))
                    {
                        var text = "Discord";
                        var textSize = g.MeasureString(text, font);
                        var x = (size.Width - textSize.Width) / 2;
                        var y = (size.Height - textSize.Height) / 2;

                        g.DrawString(text, font, brush, x, y);
                    }
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"Failed to create preview: {ex.Message}";
                return CreatePlaceholderBitmap(widgetSize.ToSize().Width, widgetSize.ToSize().Height, "Error");
            }
        }

        private Bitmap CreatePlaceholderBitmap(int width, int height, string text)
        {
            var bitmap = new Bitmap(width, height);

            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(48, 48, 48));

                using (var font = new Font("Segoe UI", 10, FontStyle.Bold))
                using (var brush = new SolidBrush(Color.White))
                {
                    var textSize = g.MeasureString(text, font);
                    var x = (width - textSize.Width) / 2;
                    var y = (height - textSize.Height) / 2;

                    g.DrawString(text, font, brush, x, y);
                }
            }

            return bitmap;
        }
    }
}
