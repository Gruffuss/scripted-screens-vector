using System.Collections.Generic;
using UnityEngine;

namespace ScriptedScreensVector.Tests;

/// <summary>BMP, GIF and `data:` URL decoding, against files built here with known pixels.</summary>
internal static class ImageDecodeTests
{
    internal static void Run(TestRun run)
    {
        Bmp24(run);
        GifEncoded(run);
        GifKnownFile(run);
        GifRoundTrip(run);
        PercentDataUrl(run);
    }

    // A 2x2, 24-bit, bottom-up BMP: red, green on the top row; blue, white below.
    private static void Bmp24(TestRun run)
    {
        var bytes = new List<byte>();
        void U16(int v) { bytes.Add((byte)v); bytes.Add((byte)(v >> 8)); }
        void S32(int v) { U16(v & 0xFFFF); U16((v >> 16) & 0xFFFF); }

        const int Stride = 8; // 2 pixels x 3 bytes, padded to 4
        bytes.Add((byte)'B'); bytes.Add((byte)'M');
        S32(54 + Stride * 2); S32(0); S32(54);
        S32(40); S32(2); S32(2); U16(1); U16(24); S32(0); S32(Stride * 2); S32(0); S32(0); S32(0); S32(0);

        // Bottom row first: blue, white.
        bytes.AddRange(new byte[] { 255, 0, 0, 255, 255, 255, 0, 0 });
        // Top row: red, green (stored BGR).
        bytes.AddRange(new byte[] { 0, 0, 255, 0, 255, 0, 0, 0 });

        var raw = bytes.ToArray();
        var pixels = ImageDecode.Bmp(raw, out var w, out var h);
        run.Check("bmp: sniffed, sized, and read top row first",
            ImageDecode.Sniff(raw) == ImageDecode.Format.Bmp && w == 2 && h == 2
            && Same(pixels[0], 255, 0, 0) && Same(pixels[1], 0, 255, 0) && Same(pixels[2], 0, 0, 255) && Same(pixels[3], 255, 255, 255),
            $"{w}x{h} {string.Join(" ", pixels)}");
    }

    // A 3x2 GIF with four colours, LZW-encoded here with a clear code every two literals so the
    // code width stays at three bits.
    private static void GifEncoded(TestRun run)
    {
        byte[] indices = { 0, 1, 2, 3, 2, 1 };
        var codes = new List<int>();
        for (var i = 0; i < indices.Length; i++)
        {
            if (i % 2 == 0)
                codes.Add(4); // clear
            codes.Add(indices[i]);
        }

        codes.Add(5); // end

        var packed = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var code in codes)
        {
            buffer |= code << bits;
            bits += 3;
            while (bits >= 8) { packed.Add((byte)buffer); buffer >>= 8; bits -= 8; }
        }

        if (bits > 0)
            packed.Add((byte)buffer);

        var gif = new List<byte>();
        gif.AddRange(System.Text.Encoding.ASCII.GetBytes("GIF89a"));
        gif.AddRange(new byte[] { 3, 0, 2, 0, 0x81, 0, 0 });              // 3x2, global table of 4
        gif.AddRange(new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255 });
        gif.AddRange(new byte[] { 0x2C, 0, 0, 0, 0, 3, 0, 2, 0, 0 });     // image, no local table
        gif.Add(2);                                                       // minimum code size
        gif.Add((byte)packed.Count);
        gif.AddRange(packed);
        gif.Add(0);
        gif.Add(0x3B);

        var raw = gif.ToArray();
        var pixels = ImageDecode.Gif(raw, out var w, out var h);
        run.Check("gif: the first frame decodes to its palette colours",
            ImageDecode.Sniff(raw) == ImageDecode.Format.Gif && w == 3 && h == 2
            && Same(pixels[0], 255, 0, 0) && Same(pixels[1], 0, 255, 0) && Same(pixels[2], 0, 0, 255)
            && Same(pixels[3], 255, 255, 255) && Same(pixels[4], 0, 0, 255) && Same(pixels[5], 0, 255, 0),
            $"{w}x{h} {string.Join(" ", pixels)}");
    }

    // A real LZW encoding (no clear codes, so the code width grows and the table fills) of a
    // 40x30 image in 16 colours with long runs, which exercises the code-not-yet-in-table case.
    internal static void GifRoundTrip(TestRun run)
    {
        const int W = 40, H = 30;
        var indices = new byte[W * H];
        for (var i = 0; i < indices.Length; i++)
            indices[i] = (byte)((i / 7 + (i % W) / 13) % 16);

        const int MinCode = 4;
        int clear = 1 << MinCode, end = clear + 1;
        var table = new Dictionary<string, int>();
        for (var i = 0; i < clear; i++)
            table[((char)i).ToString()] = i;

        var codes = new List<(int Code, int Size)>();
        int next = end + 1, size = MinCode + 1;
        codes.Add((clear, size));
        var current = ((char)indices[0]).ToString();
        for (var i = 1; i < indices.Length; i++)
        {
            var extended = current + (char)indices[i];
            if (table.ContainsKey(extended))
            {
                current = extended;
                continue;
            }

            codes.Add((table[current], size));
            if (next < 4096)
            {
                table[extended] = next++;
                if (next > (1 << size) && size < 12)
                    size++;
            }

            current = ((char)indices[i]).ToString();
        }

        codes.Add((table[current], size));
        codes.Add((end, size));

        var packed = new List<byte>();
        long buffer = 0;
        var bits = 0;
        foreach (var (code, width) in codes)
        {
            buffer |= (long)code << bits;
            bits += width;
            while (bits >= 8) { packed.Add((byte)buffer); buffer >>= 8; bits -= 8; }
        }

        if (bits > 0)
            packed.Add((byte)buffer);

        var gif = new List<byte>();
        gif.AddRange(System.Text.Encoding.ASCII.GetBytes("GIF89a"));
        gif.AddRange(new byte[] { W, 0, H, 0, 0x83, 0, 0 });
        for (var c = 0; c < 16; c++)
            gif.AddRange(new[] { (byte)(c * 16), (byte)(255 - c * 16), (byte)(c * 7) });
        gif.AddRange(new byte[] { 0x2C, 0, 0, 0, 0, W, 0, H, 0, 0 });
        gif.Add(MinCode);
        for (var at = 0; at < packed.Count; at += 255)
        {
            var n = System.Math.Min(255, packed.Count - at);
            gif.Add((byte)n);
            gif.AddRange(packed.GetRange(at, n));
        }

        gif.Add(0);
        gif.Add(0x3B);

        var pixels = ImageDecode.Gif(gif.ToArray(), out _, out _);
        var wrong = 0;
        for (var i = 0; i < indices.Length; i++)
        {
            if (pixels[i].r != (byte)(indices[i] * 16) || pixels[i].g != (byte)(255 - indices[i] * 16))
                wrong++;
        }

        run.Check("gif: a real LZW stream round-trips pixel for pixel", wrong == 0,
            $"{wrong} of {indices.Length} wrong, {codes.Count} codes, width up to {size} bits");
    }

    // The well-known 1x1 transparent GIF, as a base64 data: URL: real LZW, a transparency index.
    private static void GifKnownFile(TestRun run)
    {
        var raw = ImageDecode.DataUrl("data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
        var pixels = raw == null ? null : ImageDecode.Gif(raw, out _, out _);
        run.Check("gif: a base64 data: URL decodes, and its transparent index is clear",
            pixels is { Length: 1 } && pixels[0].a == 0, pixels == null ? "null" : pixels[0].ToString());
    }

    private static void PercentDataUrl(TestRun run)
    {
        var raw = ImageDecode.DataUrl("data:text/plain,%42M%00");
        run.Check("data: a percent-encoded body is read byte for byte",
            raw is { Length: 3 } && raw[0] == 0x42 && raw[1] == (byte)'M' && raw[2] == 0, raw == null ? "null" : string.Join(",", raw));
    }

    private static bool Same(Color32 c, byte r, byte g, byte b) => c.r == r && c.g == g && c.b == b && c.a == 255;
}
