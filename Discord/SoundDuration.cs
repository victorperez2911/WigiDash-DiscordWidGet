using System.Buffers.Binary;
using System.Collections.Concurrent;


namespace WigiDash.DiscordWidget.Discord;

/// <summary>
/// Duração dos sons do soundboard, lida do próprio arquivo no CDN do Discord
/// (<c>cdn.discordapp.com/soundboard-sounds/{id}</c>): os padrão vêm em MP3 e os dos servidores em OGG.
/// O RPC não informa a duração nem avisa quando o som termina, então é daqui que sai a barra do botão.
/// </summary>
public static class SoundDuration
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly ConcurrentDictionary<string, TimeSpan?> Cache = new();

    /// <summary>Duração do som; <c>null</c> se não deu para baixar ou ler o arquivo.</summary>
    public static async Task<TimeSpan?> GetAsync(string soundId, CancellationToken cancellationToken = default)
    {
        if (Cache.TryGetValue(soundId, out var cached))
            return cached;
        TimeSpan? duration = null;
        try
        {
            var bytes = await Http.GetByteArrayAsync($"https://cdn.discordapp.com/soundboard-sounds/{soundId}", cancellationToken)
                .ConfigureAwait(false);
            duration = Parse(bytes);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
        }
        Cache[soundId] = duration;
        return duration;
    }

    public static TimeSpan? Parse(ReadOnlySpan<byte> data) =>
        data.StartsWith("OggS"u8) ? Ogg(data) : Mp3(data);

    /// <summary>OGG: granule da última página, menos o pre-skip (Opus, 48 kHz) ou na taxa do Vorbis.</summary>
    private static TimeSpan? Ogg(ReadOnlySpan<byte> data)
    {
        var last = data.LastIndexOf("OggS"u8);
        if (last < 0 || last + 14 > data.Length)
            return null;
        var granule = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(last + 6, 8));

        var opus = data.IndexOf("OpusHead"u8);
        if (opus >= 0 && opus + 12 <= data.Length)
        {
            var preSkip = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(opus + 10, 2));
            return TimeSpan.FromSeconds(Math.Max(0, granule - preSkip) / 48000.0);
        }
        var vorbis = data.IndexOf("\u0001vorbis"u8);
        if (vorbis >= 0 && vorbis + 16 <= data.Length)
        {
            var rate = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(vorbis + 12, 4));
            return rate > 0 ? TimeSpan.FromSeconds(granule / (double)rate) : null;
        }
        return null;
    }

    private static readonly int[] Mpeg1Bitrates = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
    private static readonly int[] Mpeg2Bitrates = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
    private static readonly int[] Mpeg1Rates = [44100, 48000, 32000];

    /// <summary>MP3 (Layer III): soma os quadros, pulando a tag ID3v2. Serve para CBR e VBR.</summary>
    private static TimeSpan? Mp3(ReadOnlySpan<byte> data)
    {
        var offset = 0;
        if (data.Length > 10 && data.StartsWith("ID3"u8))
            offset = 10 + ((data[6] & 0x7F) << 21 | (data[7] & 0x7F) << 14 | (data[8] & 0x7F) << 7 | (data[9] & 0x7F));

        double seconds = 0;
        var frames = 0;
        while (offset + 4 <= data.Length)
        {
            if (data[offset] != 0xFF || (data[offset + 1] & 0xE0) != 0xE0)
            {
                offset++;
                continue;
            }
            var version = (data[offset + 1] >> 3) & 0x3; // 3 = MPEG1, 2 = MPEG2, 0 = MPEG2.5
            var layer = (data[offset + 1] >> 1) & 0x3;   // 1 = Layer III
            var bitrateIndex = data[offset + 2] >> 4;
            var rateIndex = (data[offset + 2] >> 2) & 0x3;
            var padding = (data[offset + 2] >> 1) & 0x1;
            if (version == 1 || layer != 1 || bitrateIndex is 0 or 15 || rateIndex == 3)
            {
                offset++;
                continue;
            }
            var mpeg1 = version == 3;
            var rate = Mpeg1Rates[rateIndex] / (mpeg1 ? 1 : version == 2 ? 2 : 4);
            var bitrate = (mpeg1 ? Mpeg1Bitrates : Mpeg2Bitrates)[bitrateIndex] * 1000;
            var samples = mpeg1 ? 1152 : 576;
            var length = samples / 8 * bitrate / rate + padding;
            if (length <= 4)
                break;
            seconds += samples / (double)rate;
            frames++;
            offset += length;
        }
        return frames > 0 ? TimeSpan.FromSeconds(seconds) : null;
    }

}
