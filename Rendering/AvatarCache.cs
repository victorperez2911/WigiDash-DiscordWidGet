using System.Collections.Concurrent;
using System.Drawing;

namespace WigiDash.DiscordWidget.Rendering;

/// <summary>
/// Avatares baixados do CDN do Discord, em memória. <see cref="Get"/> nunca espera a rede: devolve o que já
/// tem e baixa o resto em segundo plano, avisando em <see cref="Loaded"/> para o widget redesenhar.
/// </summary>
public static class AvatarCache
{
    private const int MaxEntries = 200;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly ConcurrentDictionary<string, Bitmap?> Images = new();

    /// <summary>Um avatar chegou do CDN.</summary>
    public static event Action? Loaded;

    /// <summary>O avatar, se já está em memória. A imagem é compartilhada: desenhe com <c>lock</c> nela.</summary>
    public static Bitmap? Get(string? url)
    {
        if (url is null)
            return null;
        if (Images.TryGetValue(url, out var image))
            return image;
        if (Images.Count >= MaxEntries)
            Images.Clear();
        if (Images.TryAdd(url, null))
            _ = Task.Run(() => DownloadAsync(url));
        return null;
    }

    private static async Task DownloadAsync(string url)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            using var stream = new MemoryStream(bytes);
            using var source = new Bitmap(stream);
            Images[url] = new Bitmap(source);
            Loaded?.Invoke();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ArgumentException)
        {
            // Sem avatar: fica a inicial do nome. Tenta de novo na próxima vez que a lista esvaziar.
        }
    }
}
