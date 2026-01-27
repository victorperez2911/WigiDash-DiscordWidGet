using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace WigiDash.DiscordWidget.Rendering
{
    public class ImageCache : IDisposable
    {
        private readonly string _resourcePath;
        private readonly Dictionary<string, Bitmap> _cache = new Dictionary<string, Bitmap>();
        private readonly HttpClient _httpClient;
        private readonly object _cacheLock = new object();

        public ImageCache(string resourcePath)
        {
            _resourcePath = resourcePath;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public async Task<Bitmap> GetImageAsync(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            // Check cache first
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(path, out var cached))
                {
                    return new Bitmap(cached); // Return copy
                }
            }

            Bitmap bitmap = null;

            // Try to load from file or URL
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                bitmap = await DownloadImageAsync(path);
            }
            else
            {
                bitmap = LoadLocalImage(path);
            }

            // Cache the bitmap
            if (bitmap != null)
            {
                lock (_cacheLock)
                {
                    if (!_cache.ContainsKey(path))
                    {
                        _cache[path] = new Bitmap(bitmap);
                    }
                }
            }

            return bitmap;
        }

        private Bitmap LoadLocalImage(string relativePath)
        {
            try
            {
                var fullPath = Path.Combine(_resourcePath, relativePath);

                if (File.Exists(fullPath))
                {
                    return new Bitmap(fullPath);
                }
            }
            catch
            {
                // Ignore errors
            }

            return null;
        }

        private async Task<Bitmap> DownloadImageAsync(string url)
        {
            try
            {
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    {
                        return new Bitmap(stream);
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return null;
        }

        public void ClearCache()
        {
            lock (_cacheLock)
            {
                foreach (var bitmap in _cache.Values)
                {
                    bitmap?.Dispose();
                }

                _cache.Clear();
            }
        }

        public void Dispose()
        {
            ClearCache();
            _httpClient?.Dispose();
        }
    }
}
