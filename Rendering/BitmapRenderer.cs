using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using WigiDashWidgetFramework.WidgetUtility;

namespace WigiDash.DiscordWidget.Rendering
{
    public class BitmapRenderer : IDisposable
    {
        private readonly Font _titleFont;
        private readonly Font _bodyFont;
        private readonly Font _smallFont;
        private readonly Brush _textBrush;
        private readonly Brush _dimTextBrush;

        public BitmapRenderer()
        {
            _titleFont = new Font("Segoe UI", 14, FontStyle.Bold);
            _bodyFont = new Font("Segoe UI", 11, FontStyle.Regular);
            _smallFont = new Font("Segoe UI", 9, FontStyle.Regular);
            _textBrush = new SolidBrush(Color.White);
            _dimTextBrush = new SolidBrush(Color.FromArgb(180, 180, 180));
        }

        public Bitmap CreateSimpleTextBitmap(WidgetSize size, string title, string subtitle, Color backgroundColor)
        {
            var pixelSize = size.ToSize();
            var bitmap = new Bitmap(pixelSize.Width, pixelSize.Height);

            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;

                // Background
                using (var brush = new SolidBrush(backgroundColor))
                {
                    g.FillRectangle(brush, 0, 0, pixelSize.Width, pixelSize.Height);
                }

                // Title
                var titleSize = g.MeasureString(title, _titleFont, pixelSize.Width - 20);
                float titleY = pixelSize.Height / 3;
                g.DrawString(
                    title,
                    _titleFont,
                    _textBrush,
                    new RectangleF(10, titleY, pixelSize.Width - 20, titleSize.Height),
                    new StringFormat { Alignment = StringAlignment.Center }
                );

                // Subtitle
                if (!string.IsNullOrEmpty(subtitle))
                {
                    var subtitleSize = g.MeasureString(subtitle, _smallFont, pixelSize.Width - 20);
                    float subtitleY = titleY + titleSize.Height + 5;
                    g.DrawString(
                        subtitle,
                        _smallFont,
                        _dimTextBrush,
                        new RectangleF(10, subtitleY, pixelSize.Width - 20, subtitleSize.Height),
                        new StringFormat { Alignment = StringAlignment.Center }
                    );
                }
            }

            return bitmap;
        }

        public Bitmap CreateSimpleIconBitmap(WidgetSize size, Bitmap icon, string text, Color backgroundColor)
        {
            var pixelSize = size.ToSize();
            var bitmap = new Bitmap(pixelSize.Width, pixelSize.Height);

            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;

                // Background
                using (var brush = new SolidBrush(backgroundColor))
                {
                    g.FillRectangle(brush, 0, 0, pixelSize.Width, pixelSize.Height);
                }

                // Icon (center-top)
                if (icon != null)
                {
                    int iconSize = Math.Min(64, pixelSize.Width / 2);
                    int x = (pixelSize.Width - iconSize) / 2;
                    int y = pixelSize.Height / 3 - iconSize / 2;

                    g.DrawImage(icon, x, y, iconSize, iconSize);
                }

                // Text (center-bottom)
                var textSize = g.MeasureString(text, _bodyFont);
                float textX = (pixelSize.Width - textSize.Width) / 2;
                float textY = pixelSize.Height * 2 / 3;

                g.DrawString(text, _bodyFont, _textBrush, textX, textY);
            }

            return bitmap;
        }

        public Bitmap CreateErrorBitmap(WidgetSize size, string errorMessage)
        {
            return CreateSimpleTextBitmap(
                size,
                "Error",
                errorMessage,
                Color.FromArgb(237, 66, 69)
            );
        }

        /// <summary>
        /// Creates a voice channel button like Stream Deck
        /// </summary>
        public Bitmap CreateVoiceChannelBitmap(
            WidgetSize size,
            Bitmap avatar,
            string channelName,
            int userCount,
            bool isConnected,
            bool isSpeaking,
            Color backgroundColor)
        {
            var pixelSize = size.ToSize();
            var bitmap = new Bitmap(pixelSize.Width, pixelSize.Height);

            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;

                // Background
                using (var brush = new SolidBrush(backgroundColor))
                {
                    g.FillRectangle(brush, 0, 0, pixelSize.Width, pixelSize.Height);
                }

                if (isConnected && avatar != null)
                {
                    // Connected state: Show avatar
                    int avatarSize = Math.Min(pixelSize.Width, pixelSize.Height) - 40;
                    int avatarX = (pixelSize.Width - avatarSize) / 2;
                    int avatarY = (pixelSize.Height - avatarSize) / 2 - 10;

                    // Draw circular avatar with border if speaking
                    DrawCircularAvatar(g, avatar, avatarX, avatarY, avatarSize, isSpeaking);

                    // Draw user count badge if more than 1 user
                    if (userCount > 1)
                    {
                        DrawUserCountBadge(g, userCount, pixelSize.Width - 30, 10);
                    }

                    // Draw channel name at bottom
                    if (!string.IsNullOrEmpty(channelName))
                    {
                        using (var format = new StringFormat { Alignment = StringAlignment.Center })
                        {
                            var textRect = new RectangleF(5, pixelSize.Height - 25, pixelSize.Width - 10, 20);
                            g.DrawString(channelName, _smallFont, _textBrush, textRect, format);
                        }
                    }
                }
                else
                {
                    // Disconnected state: Show channel name/icon
                    if (avatar != null)
                    {
                        // Server/channel icon
                        int iconSize = 64;
                        int iconX = (pixelSize.Width - iconSize) / 2;
                        int iconY = (pixelSize.Height - iconSize) / 2 - 15;

                        g.DrawImage(avatar, iconX, iconY, iconSize, iconSize);
                    }

                    // Channel name
                    if (!string.IsNullOrEmpty(channelName))
                    {
                        using (var format = new StringFormat { Alignment = StringAlignment.Center })
                        {
                            var textRect = new RectangleF(5, pixelSize.Height - 40, pixelSize.Width - 10, 35);
                            g.DrawString(channelName, _bodyFont, _textBrush, textRect, format);
                        }
                    }
                }
            }

            return bitmap;
        }

        /// <summary>
        /// Draws a circular avatar with optional speaking border
        /// </summary>
        private void DrawCircularAvatar(Graphics g, Bitmap avatar, int x, int y, int size, bool isSpeaking)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(x, y, size, size);

                // Clip to circle
                g.SetClip(path);
                g.DrawImage(avatar, x, y, size, size);
                g.ResetClip();

                // Draw border
                if (isSpeaking)
                {
                    // Green border when speaking
                    using (var pen = new Pen(Color.FromArgb(67, 181, 129), 4))
                    {
                        g.DrawEllipse(pen, x, y, size, size);
                    }
                }
                else
                {
                    // Subtle border when not speaking
                    using (var pen = new Pen(Color.FromArgb(60, 60, 60), 2))
                    {
                        g.DrawEllipse(pen, x, y, size, size);
                    }
                }
            }
        }

        /// <summary>
        /// Draws a user count badge (e.g., "+3")
        /// </summary>
        private void DrawUserCountBadge(Graphics g, int userCount, int x, int y)
        {
            string text = $"+{userCount - 1}";
            var textSize = g.MeasureString(text, _smallFont);

            int badgeWidth = (int)textSize.Width + 12;
            int badgeHeight = (int)textSize.Height + 6;

            // Badge background (Discord blurple)
            using (var brush = new SolidBrush(Color.FromArgb(88, 101, 242)))
            using (var path = new GraphicsPath())
            {
                var rect = new Rectangle(x - badgeWidth, y, badgeWidth, badgeHeight);
                int radius = badgeHeight / 2;

                // Rounded rectangle
                path.AddArc(rect.X, rect.Y, radius, radius, 180, 90);
                path.AddArc(rect.Right - radius, rect.Y, radius, radius, 270, 90);
                path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
                path.AddArc(rect.X, rect.Bottom - radius, radius, radius, 90, 90);
                path.CloseFigure();

                g.FillPath(brush, path);
            }

            // Badge text
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                var textRect = new RectangleF(x - badgeWidth, y, badgeWidth, badgeHeight);
                g.DrawString(text, _smallFont, _textBrush, textRect, format);
            }
        }

        /// <summary>
        /// Creates a circular bitmap from a square bitmap (for avatars)
        /// </summary>
        public Bitmap CreateCircularBitmap(Bitmap source, int size)
        {
            if (source == null)
                return null;

            var output = new Bitmap(size, size);

            using (var g = Graphics.FromImage(output))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(0, 0, size, size);

                    g.SetClip(path);

                    // Draw scaled image
                    g.DrawImage(source, 0, 0, size, size);
                }
            }

            return output;
        }

        public void Dispose()
        {
            _titleFont?.Dispose();
            _bodyFont?.Dispose();
            _smallFont?.Dispose();
            _textBrush?.Dispose();
            _dimTextBrush?.Dispose();
        }
    }
}
