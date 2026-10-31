using System.Buffers.Binary;
using System.Text;
using WigiDash.DiscordWidget.Discord;

namespace DiscordWidget.Tests;

public sealed class SoundDurationTests
{
    /// <summary>OGG Opus mínimo: página com OpusHead (pre-skip 312) e última página com granule 96312 = 2 s.</summary>
    [Fact]
    public void Ogg_opus_usa_o_granule_da_ultima_pagina_menos_o_pre_skip()
    {
        var head = new byte[19];
        Encoding.ASCII.GetBytes("OpusHead").CopyTo(head, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(10), 312);
        var data = Page(0).Concat(head).Concat(Page(96312)).ToArray();

        Assert.Equal(TimeSpan.FromSeconds(2), SoundDuration.Parse(data));
    }

    /// <summary>MP3 MPEG1 Layer III 128 kbps 44,1 kHz: 38 quadros de 1152 amostras ≈ 0,9927 s.</summary>
    [Fact]
    public void Mp3_soma_os_quadros_depois_da_tag_ID3()
    {
        var frame = new byte[417];
        frame[0] = 0xFF;
        frame[1] = 0xFB; // MPEG1, Layer III, sem CRC
        frame[2] = 0x90; // 128 kbps, 44,1 kHz, sem padding
        var id3 = new byte[] { (byte)'I', (byte)'D', (byte)'3', 4, 0, 0, 0, 0, 0, 5, 1, 2, 3, 4, 5 };
        var data = id3.Concat(Enumerable.Repeat(frame, 38).SelectMany(f => f)).ToArray();

        var duration = SoundDuration.Parse(data)!.Value;

        Assert.Equal(38 * 1152 / 44100.0, duration.TotalSeconds, 4);
    }

    [Fact]
    public void Arquivo_desconhecido_nao_tem_duracao() =>
        Assert.Null(SoundDuration.Parse(new byte[64]));

    private static byte[] Page(long granule)
    {
        var page = new byte[27];
        Encoding.ASCII.GetBytes("OggS").CopyTo(page, 0);
        BinaryPrimitives.WriteInt64LittleEndian(page.AsSpan(6), granule);
        return page;
    }
}
