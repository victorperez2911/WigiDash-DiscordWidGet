using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using OpenWigi.Sdk;

namespace WigiDash.DiscordWidget.Rendering;

/// <summary>
/// O que um botão mostra. <see cref="Glyph"/> é um caractere da fonte de ícones do Windows
/// (Segoe Fluent Icons, ou Segoe MDL2 Assets no Windows 10): os ícones são do sistema, não da Elgato.
/// </summary>
public sealed record ButtonVisual(string Glyph, string Label)
{
    /// <summary>Cor de fundo do estado "ligado" (ex.: vermelho quando mutado). <c>null</c> = fundo do tema.</summary>
    public Color? Accent { get; init; }

    /// <summary>Segunda linha, menor (ex.: nome do canal, volume).</summary>
    public string? Detail { get; init; }

    /// <summary>Desenha apagado (Discord desconectado, falta configurar).</summary>
    public bool Dimmed { get; init; }

    /// <summary>Barra de nível no rodapé (volume), de 0 a 1. <c>null</c> = sem barra.</summary>
    public double? Level { get; init; }
}

/// <summary>Desenha um <see cref="ButtonVisual"/> no tamanho do widget, com as cores e fontes do tema.</summary>
public static class ButtonRenderer
{
    public static readonly Color DiscordBlurple = Color.FromArgb(88, 101, 242);
    public static readonly Color Danger = Color.FromArgb(218, 55, 60);
    public static readonly Color Success = Color.FromArgb(35, 165, 90);

    private static readonly string IconFont = PickIconFont();

    public static WidgetFrame Render(PixelSize size, WidgetTheme theme, ButtonVisual visual)
    {
        using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Black);

            var background = visual.Accent ?? ToColor(theme.PrimaryBackground);
            var foreground = ToColor(theme.PrimaryForeground);
            if (visual.Dimmed)
            {
                background = Blend(background, Color.Black, 0.5);
                foreground = Blend(foreground, background, 0.5);
            }

            using (var path = RoundedRect(new Rectangle(0, 0, size.Width, size.Height), theme.CornerRadius))
            using (var brush = new SolidBrush(background))
                g.FillPath(brush, path);

            var hasDetail = !string.IsNullOrEmpty(visual.Detail);
            var labelHeight = Math.Max(16, size.Height / 7);
            var textBlock = labelHeight + (hasDetail ? labelHeight * 4 / 5 : 0) + 8;
            var iconArea = new RectangleF(0, 6, size.Width, size.Height - textBlock - 6);

            using (var iconFont = new Font(IconFont, Math.Min(iconArea.Height * 0.62f, size.Width * 0.4f), GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(foreground))
                g.DrawString(visual.Glyph, iconFont, brush, iconArea, Centered);

            var y = size.Height - textBlock;
            using (var labelFont = new Font(theme.SecondaryFontFamily, labelHeight * 0.8f, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(foreground))
                g.DrawString(visual.Label, labelFont, brush, new RectangleF(4, y, size.Width - 8, labelHeight + 2), Centered);

            if (hasDetail)
            {
                using var detailFont = new Font(theme.PrimaryFontFamily, labelHeight * 0.65f, GraphicsUnit.Pixel);
                using var brush = new SolidBrush(Blend(foreground, background, 0.25));
                g.DrawString(visual.Detail, detailFont, brush,
                    new RectangleF(4, y + labelHeight, size.Width - 8, labelHeight), Centered);
            }

            if (visual.Level is { } level)
            {
                var inset = Math.Max(theme.CornerRadius / 2, 6);
                var track = new RectangleF(inset, size.Height - 7, size.Width - inset * 2, 4);
                using var trackBrush = new SolidBrush(Blend(background, foreground, 0.2));
                using var fillBrush = new SolidBrush(foreground);
                g.FillRectangle(trackBrush, track);
                g.FillRectangle(fillBrush, track with { Width = (float)(track.Width * Math.Clamp(level, 0, 1)) });
            }
        }
        return ToFrame(bitmap);
    }

    private static readonly StringFormat Centered = new()
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap,
    };

    internal static WidgetFrame ToFrame(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[data.Stride * bitmap.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return new WidgetFrame(new PixelSize(bitmap.Width, bitmap.Height), FramePixelFormat.Bgra32, pixels, data.Stride);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Clamp(radius * 2, 0, Math.Min(r.Width, r.Height));
        if (d == 0)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color ToColor(WidgetColor c) => Color.FromArgb(c.R, c.G, c.B);

    private static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    private static string PickIconFont()
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    }
}
