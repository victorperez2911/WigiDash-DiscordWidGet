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
    /// <summary>Cor de fundo do estado (verde ativo, vermelho mutado). <c>null</c> = azul do Discord.</summary>
    public Color? Accent { get; init; }

    /// <summary>Segunda linha, menor (ex.: nome do canal, volume).</summary>
    public string? Detail { get; init; }

    /// <summary>Desenha apagado (Discord desconectado, falta configurar).</summary>
    public bool Dimmed { get; init; }

    /// <summary>Barra de nível no rodapé (volume), de 0 a 1. <c>null</c> = sem barra.</summary>
    public double? Level { get; init; }

    /// <summary>Avatares no lugar do ícone (quem está no canal), sobrepostos da esquerda para a direita.</summary>
    public IReadOnlyList<AvatarFace>? Faces { get; init; }

    /// <summary>Contador no canto superior direito (ex.: pessoas no canal).</summary>
    public int? Badge { get; init; }

    /// <summary>Imagem no lugar do ícone (ex.: emoji do som), baixada pelo <see cref="AvatarCache"/>.</summary>
    public string? ImageUrl { get; init; }
}

/// <summary>Um avatar: imagem do CDN (ou a inicial do nome enquanto não chega) e o anel de quem está falando.</summary>
public sealed record AvatarFace(string? Url, string Name, bool Speaking);

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

            // Padrão visual das imagens do plugin da Elgato: fundo azul do Discord; verde quando a ação está
            // ativa e vermelho quando algo está cortado (mutado, ensurdecido) vêm em Accent.
            var background = visual.Accent ?? DiscordBlurple;
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

            if (visual.Faces is { Count: > 0 } faces)
                DrawFaces(g, iconArea, faces, background, size.Columns() > 1 ? 5 : 3);
            else if (AvatarCache.Get(visual.ImageUrl) is { } image)
            {
                var side = Math.Min(iconArea.Height * 0.8f, size.Width * 0.45f);
                var rect = new RectangleF(iconArea.X + (iconArea.Width - side) / 2, iconArea.Y + (iconArea.Height - side) / 2, side, side);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (visual.Dimmed)
                {
                    using var attributes = new ImageAttributes();
                    attributes.SetColorMatrix(new ColorMatrix { Matrix33 = 0.4f });
                    lock (image)
                        g.DrawImage(image, Rectangle.Round(rect), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
                }
                else
                {
                    lock (image)
                        g.DrawImage(image, rect);
                }
            }
            else
            {
                using var iconFont = new Font(IconFont, Math.Min(iconArea.Height * 0.62f, size.Width * 0.4f), GraphicsUnit.Pixel);
                using var brush = new SolidBrush(foreground);
                g.DrawString(visual.Glyph, iconFont, brush, iconArea, Centered);
            }

            if (visual.Badge is { } badge)
                DrawBadge(g, size, theme, badge, background, foreground);

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

    /// <summary>
    /// Avatares em círculo, sobrepostos como no plugin da Elgato. Quem está falando ganha anel verde; acima do
    /// limite, o último círculo vira "+N".
    /// </summary>
    private static void DrawFaces(Graphics g, RectangleF area, IReadOnlyList<AvatarFace> faces, Color background, int max)
    {
        var shown = faces.Count > max ? faces.Take(max - 1).ToList() : faces.ToList();
        var extra = faces.Count - shown.Count;
        var slots = shown.Count + (extra > 0 ? 1 : 0);
        var d = Math.Min(area.Height * 0.92f, area.Width * 0.5f);
        var step = slots > 1 ? Math.Min(d * 0.7f, (area.Width - 12 - d) / (slots - 1)) : 0;
        var total = d + step * (slots - 1);
        var x = area.X + (area.Width - total) / 2;
        var y = area.Y + (area.Height - d) / 2;
        var ring = Math.Max(3f, d / 16);

        for (var i = 0; i < shown.Count; i++)
        {
            var rect = new RectangleF(x + step * i, y, d, d);
            if (shown[i].Speaking)
            {
                // Anel de quem fala; no fundo verde (você na call) o verde sumiria, então vai branco.
                var speaking = background.ToArgb() == Success.ToArgb() ? Color.White : Success;
                using var glow = new SolidBrush(speaking);
                g.FillEllipse(glow, RectangleF.Inflate(rect, ring * 1.1f, ring * 1.1f));
            }
            else
            {
                using var border = new SolidBrush(background);
                g.FillEllipse(border, RectangleF.Inflate(rect, ring, ring));
            }
            DrawAvatar(g, rect, shown[i]);
        }

        if (extra > 0)
        {
            var rect = new RectangleF(x + step * shown.Count, y, d, d);
            using (var border = new SolidBrush(background))
                g.FillEllipse(border, RectangleF.Inflate(rect, ring, ring));
            using (var fill = new SolidBrush(Color.FromArgb(78, 80, 88)))
                g.FillEllipse(fill, rect);
            using var font = new Font("Segoe UI Semibold", d * 0.32f, GraphicsUnit.Pixel);
            g.DrawString($"+{extra}", font, Brushes.White, rect, Centered);
        }
    }

    private static void DrawAvatar(Graphics g, RectangleF rect, AvatarFace face)
    {
        using var clip = new GraphicsPath();
        clip.AddEllipse(rect);
        var image = AvatarCache.Get(face.Url);
        if (image is not null)
        {
            var state = g.Save();
            g.SetClip(clip);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            lock (image)
                g.DrawImage(image, rect);
            g.Restore(state);
            return;
        }
        // Enquanto o avatar não chega: a inicial sobre a cor da pessoa (estável pelo nome).
        var hue = (uint)face.Name.GetHashCode() % 6;
        Color[] palette = [DiscordBlurple, Success, Danger, Color.FromArgb(240, 178, 50), Color.FromArgb(235, 69, 158), Color.FromArgb(87, 158, 230)];
        using (var fill = new SolidBrush(palette[hue]))
            g.FillEllipse(fill, rect);
        using var font = new Font("Segoe UI Semibold", rect.Height * 0.45f, GraphicsUnit.Pixel);
        var initial = face.Name.Length > 0 ? char.ToUpperInvariant(face.Name[0]).ToString() : "?";
        g.DrawString(initial, font, Brushes.White, rect, Centered);
    }

    /// <summary>Pílula com o contador no canto superior direito, como o "2" de pessoas da Elgato.</summary>
    private static void DrawBadge(Graphics g, PixelSize size, WidgetTheme theme, int count, Color background, Color foreground)
    {
        var height = Math.Max(20f, size.Height / 6.5f);
        using var font = new Font(theme.SecondaryFontFamily, height * 0.62f, GraphicsUnit.Pixel);
        using var iconFont = new Font(IconFont, height * 0.55f, GraphicsUnit.Pixel);
        var text = count.ToString();
        var textWidth = g.MeasureString(text, font).Width;
        var width = height * 0.9f + textWidth + height * 0.3f;
        var inset = Math.Max(theme.CornerRadius * 0.6f, 6);
        var rect = new RectangleF(size.Width - width - inset, inset, width, height);
        using (var path = RoundedRect(Rectangle.Round(rect), (int)(height / 2)))
        using (var fill = new SolidBrush(Color.FromArgb(200, Blend(background, Color.Black, 0.55))))
            g.FillPath(fill, path);
        using var brush = new SolidBrush(foreground);
        g.DrawString("\uE77B", iconFont, brush, new RectangleF(rect.X + height * 0.15f, rect.Y, height * 0.75f, height), Centered);
        g.DrawString(text, font, brush, new RectangleF(rect.X + height * 0.8f, rect.Y, textWidth + height * 0.2f, height), Centered);
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

    /// <summary>Quantas células de largura o quadro ocupa (para decidir quantos avatares cabem).</summary>
    private static int Columns(this PixelSize size) => size.Width > WidgetGrid.CellWidth ? 2 : 1;

    /// <summary>A cor um pouco mais clara (pulso de atenção).</summary>
    public static Color Lighter(Color color) => Blend(color, Color.White, 0.3);

    private static Color ToColor(WidgetColor c) => Color.FromArgb(c.R, c.G, c.B);

    private static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    private static string PickIconFont()
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    }
}
