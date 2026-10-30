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
    public async Task Previa_de_cada_widget_desenha_em_todos_os_tamanhos()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "previews");
        Directory.CreateDirectory(output);

        foreach (var widget in LoadWidgets())
        {
            foreach (var size in widget.Manifest.SupportedSizes)
            {
                var frame = await widget.GetPreviewAsync(size, CancellationToken.None);
                Assert.NotNull(frame);
                Assert.Equal(size.PixelSize, frame.Size);
                Save(frame, Path.Combine(output, $"{widget.Manifest.Name.Replace(':', '-')}-{size.Columns}x{size.Rows}.png"));
            }
        }
    }

    [Fact]
    public void Estados_desenham_sem_erro()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "previews");
        Directory.CreateDirectory(output);
        var theme = new WidgetTheme();
        Save(ButtonRenderer.Render(WidgetSize.Small.PixelSize, theme, new ButtonVisual(Glyphs.Video, "Câmera desligada") { Slashed = true }),
            Path.Combine(output, "estado-camera-off.png"));
        Save(ButtonRenderer.Render(WidgetSize.Small.PixelSize, theme, new ButtonVisual(Glyphs.Volume, "60%") { Level = 0.6, Hints = AdjustHints.Vertical }),
            Path.Combine(output, "estado-volume-1x1.png"));
        Save(ButtonRenderer.Render(WidgetSize.Wide.PixelSize, theme, new ButtonVisual(Glyphs.Volume, "60%") { Level = 0.6, Hints = AdjustHints.Horizontal }),
            Path.Combine(output, "estado-volume-2x1.png"));
        Save(ButtonRenderer.Render(WidgetSize.Small.PixelSize, theme, new ButtonVisual(Glyphs.Microphone, "Detecção de voz") { GlyphBadge = Glyphs.Volume, Detail = "Modo de voz" }),
            Path.Combine(output, "estado-modo-voz.png"));
        Save(ButtonRenderer.Render(WidgetSize.Small.PixelSize, theme, new ButtonVisual(Glyphs.Microphone, "Push to Talk") { Accent = ButtonRenderer.Success, GlyphBadge = Glyphs.Touch, Detail = "Modo de voz" }),
            Path.Combine(output, "estado-modo-ptt.png"));
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
