using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using OpenWigi.Sdk;
using WigiDash.DiscordWidget.Rendering;
using WigiDash.DiscordWidget.Widgets;

namespace DiscordWidget.Tests;

public sealed class PluginTests
{
    private static readonly Assembly Plugin = typeof(DiscordWidgetBase).Assembly;

    private static List<IWidget> LoadWidgets() => Plugin.GetCustomAttributes<OpenWigiWidgetAttribute>()
        .Select(a => (IWidget)Activator.CreateInstance(a.WidgetType)!)
        .ToList();

    [Fact]
    public void Todos_os_widgets_declarados_tem_ficha_valida_e_ids_unicos()
    {
        var widgets = LoadWidgets();

        Assert.NotEmpty(widgets);
        Assert.All(widgets, w => Assert.Empty(w.Manifest.Validate()));
        Assert.Equal(widgets.Count, widgets.Select(w => w.Manifest.Id).Distinct().Count());
    }

    [Fact]
    public void Mini_so_nos_botoes_de_icone()
    {
        string[] iconButtons =
        [
            "Discord: Mute", "Discord: Deafen", "Discord: Push to Talk", "Discord: Push to Mute",
            "Discord: Mute inteligente", "Discord: Voice Input Mode", "Discord: Soundboard",
            "Discord: Video Toggle", "Discord: Stream Toggle",
        ];
        foreach (var widget in LoadWidgets())
            Assert.Equal(iconButtons.Contains(widget.Manifest.Name), widget.Manifest.SupportedSizes.Contains(WidgetSize.Mini));
    }

    [Fact]
    public async Task Previa_de_cada_widget_desenha_em_todos_os_tamanhos_e_molduras()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "previews");
        Directory.CreateDirectory(output);

        foreach (var widget in LoadWidgets())
        {
            foreach (var size in widget.Manifest.SupportedSizes)
            {
                // A folga da moldura vai de 0 a 5 px de cada lado: o quadro tem que caber em todas.
                foreach (var inset in TileFrame.InsetOptions)
                {
                    var pixels = new PixelSize(size.CellSpan.Width - 2 * inset, size.CellSpan.Height - 2 * inset);
                    var frame = await widget.GetPreviewAsync(size, pixels, CancellationToken.None);
                    Assert.NotNull(frame);
                    Assert.Equal(pixels, frame.Size);
                    if (inset == TileFrame.Default.Inset)
                        Save(frame, Path.Combine(output, $"{widget.Manifest.Name.Replace(':', '-')}-{size.Columns}x{size.Rows}.png"));
                }
            }
        }
    }

    [Fact]
    public void Estados_desenham_sem_erro()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "previews");
        Directory.CreateDirectory(output);
        var theme = new WidgetTheme();
        Save(ButtonRenderer.Render(TileFrame.Default.ContentSize(WidgetSize.Small.CellSpan), theme, new ButtonVisual(Glyphs.Video, "Câmera desligada") { Slashed = true }),
            Path.Combine(output, "estado-camera-off.png"));
        Save(ButtonRenderer.Render(TileFrame.Default.ContentSize(WidgetSize.Small.CellSpan), theme, new ButtonVisual(Glyphs.Volume, "60%") { Level = 0.6, Hints = AdjustHints.Horizontal }),
            Path.Combine(output, "estado-volume-1x1.png"));
        Save(ButtonRenderer.Render(TileFrame.Default.ContentSize(WidgetSize.Wide.CellSpan), theme, new ButtonVisual(Glyphs.Volume, "60%") { Level = 0.6, Hints = AdjustHints.Horizontal }),
            Path.Combine(output, "estado-volume-2x1.png"));
        Save(ButtonRenderer.Render(TileFrame.Default.ContentSize(WidgetSize.Small.CellSpan), theme, new ButtonVisual(Glyphs.Microphone, "Detecção de voz") { GlyphBadge = Glyphs.Volume, Detail = "Modo de voz" }),
            Path.Combine(output, "estado-modo-voz.png"));
        Save(ButtonRenderer.Render(TileFrame.Default.ContentSize(WidgetSize.Small.CellSpan), theme, new ButtonVisual(Glyphs.Microphone, "Push to Talk") { Accent = ButtonRenderer.Success, GlyphBadge = Glyphs.Touch, Detail = "Modo de voz" }),
            Path.Combine(output, "estado-modo-ptt.png"));
        Save(ButtonRenderer.Render(TileFrame.Default.ContentSize(WidgetSize.Mini.CellSpan), theme, new ButtonVisual(Glyphs.Microphone, "Mute") { GlyphBadge = Glyphs.Touch, Detail = "não aparece no Mini" }),
            Path.Combine(output, "estado-mini.png"));
        Save(ButtonRenderer.Render(TileFrame.Default.ContentSize(WidgetSize.Mini.CellSpan), theme, new ButtonVisual(Glyphs.Volume, "60%") { Level = 0.6, Hints = AdjustHints.Horizontal }),
            Path.Combine(output, "estado-mini-volume.png"));
    }

    internal static void Save(WidgetFrame frame, string path)
    {
        using var bitmap = new Bitmap(frame.Size.Width, frame.Size.Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = frame.Pixels.ToArray();
            for (var y = 0; y < bitmap.Height; y++)
                Marshal.Copy(pixels, y * frame.Stride, data.Scan0 + y * data.Stride, Math.Min(frame.Stride, data.Stride));
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        bitmap.Save(path, ImageFormat.Png);
    }
}
