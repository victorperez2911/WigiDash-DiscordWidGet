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
