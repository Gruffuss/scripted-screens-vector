using System;
using System.Collections.Generic;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>
/// The image formats Unity's own decoder does not take, decoded in managed code: BMP and the
/// first frame of a GIF. PNG and JPEG still go to Unity (<c>Texture2D.LoadImage</c>).
/// </summary>
/// <remarks>
/// Pure managed code with no Unity call, so the test suite runs it headless. Pixels come back
/// top row first, as the file stores a GIF and as a top-down BMP does.
/// </remarks>
internal static class ImageDecode
{
    internal enum Format
    {
        Unknown,
        Png,
        Jpeg,
        Bmp,
        Gif,
    }

    internal static Format Sniff(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G')
            return Format.Png;
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return Format.Jpeg;
        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
            return Format.Bmp;
        if (bytes.Length >= 6 && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F')
            return Format.Gif;
        return Format.Unknown;
    }

    /// <summary>
    /// The bytes of a `data:` URL, or null when it is not one this can read. Base64 and
    /// percent-encoded bodies are both accepted.
    /// </summary>
    internal static byte[]? DataUrl(string url)
    {
        if (!url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;

        var comma = url.IndexOf(',', StringComparison.Ordinal);
        if (comma < 0)
            return null;

        var header = url.Substring(5, comma - 5);
        var body = url.Substring(comma + 1);
        if (header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
            return Convert.FromBase64String(body.Trim());

        // Percent-encoded: each %XX is one byte, everything else is itself (ASCII).
        var bytes = new List<byte>(body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '%' && i + 2 < body.Length)
            {
                bytes.Add(Convert.ToByte(body.Substring(i + 1, 2), 16));
                i += 2;
            }
            else
            {
                bytes.Add((byte)body[i]);
            }
        }

        return bytes.ToArray();
    }

    /// <summary>An uncompressed BMP: 8-bit palette, 24-bit or 32-bit.</summary>
    internal static Color32[] Bmp(byte[] b, out int width, out int height)
    {
        int U16(int at) => b[at] | (b[at + 1] << 8);
        int S32(int at) => b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24);

        var dataOffset = S32(10);
        var headerSize = S32(14);
        width = S32(18);
        var rawHeight = S32(22);
        var bits = U16(28);
        var compression = headerSize >= 40 ? S32(30) : 0;
        var colours = headerSize >= 40 ? S32(46) : 0;

        // 0 = BI_RGB; 3 = BI_BITFIELDS, accepted for 32-bit files in the usual BGRA layout.
        if (compression != 0 && !(compression == 3 && bits == 32))
            throw new FormatException("compressed BMP is not supported");
        if (bits != 8 && bits != 24 && bits != 32)
            throw new FormatException($"{bits}-bit BMP is not supported");

        var topDown = rawHeight < 0;
        height = Math.Abs(rawHeight);
        if (width <= 0 || height <= 0 || (long)width * height > 16_777_216)
            throw new FormatException("BMP size out of range");

        Color32[]? palette = null;
        if (bits == 8)
        {
            var count = colours > 0 ? colours : 256;
            palette = new Color32[count];
            var at = 14 + headerSize;
            for (var i = 0; i < count; i++, at += 4)
                palette[i] = new Color32(b[at + 2], b[at + 1], b[at], 255);
        }

        var stride = ((width * bits + 31) / 32) * 4;
        var pixels = new Color32[width * height];
        for (var y = 0; y < height; y++)
        {
            var row = dataOffset + (topDown ? y : height - 1 - y) * stride;
            for (var x = 0; x < width; x++)
            {
                pixels[y * width + x] = bits switch
                {
                    8 => palette![b[row + x]],
                    24 => new Color32(b[row + x * 3 + 2], b[row + x * 3 + 1], b[row + x * 3], 255),
                    _ => new Color32(b[row + x * 4 + 2], b[row + x * 4 + 1], b[row + x * 4], compression == 3 || b[row + x * 4 + 3] != 0 ? b[row + x * 4 + 3] : (byte)255),
                };
            }
        }

        return pixels;
    }

    /// <summary>The first frame of a GIF, on its logical screen, transparent where it has no pixels.</summary>
    internal static Color32[] Gif(byte[] b, out int width, out int height)
    {
        width = b[6] | (b[7] << 8);
        height = b[8] | (b[9] << 8);
        if (width <= 0 || height <= 0 || (long)width * height > 16_777_216)
            throw new FormatException("GIF size out of range");

        var at = 10;
        var flags = b[at++];
        at += 2; // background colour index, aspect

        Color32[]? global = null;
        if ((flags & 0x80) != 0)
            global = ReadTable(b, ref at, 2 << (flags & 7));

        var transparent = -1;
        var pixels = new Color32[width * height];

        while (at < b.Length)
        {
            var block = b[at++];
            if (block == 0x3B)
                break;

            if (block == 0x21)
            {
                var label = b[at++];
                if (label == 0xF9 && b[at] >= 4)
                {
                    // Graphic control: flag bit 0 says the next colour index is transparent.
                    if ((b[at + 1] & 1) != 0)
                        transparent = b[at + 4];
                }

                SkipSubBlocks(b, ref at);
                continue;
            }

            if (block != 0x2C)
                throw new FormatException("malformed GIF");

            var left = b[at] | (b[at + 1] << 8);
            var top = b[at + 2] | (b[at + 3] << 8);
            var w = b[at + 4] | (b[at + 5] << 8);
            var h = b[at + 6] | (b[at + 7] << 8);
            var imageFlags = b[at + 8];
            at += 9;

            var table = (imageFlags & 0x80) != 0 ? ReadTable(b, ref at, 2 << (imageFlags & 7)) : global;
            if (table == null)
                throw new FormatException("GIF has no colour table");

            var minCode = b[at++];
            var data = new List<byte>();
            while (b[at] != 0)
            {
                var size = b[at++];
                for (var i = 0; i < size; i++)
                    data.Add(b[at + i]);
                at += size;
            }

            var indices = Lzw(data, minCode, w * h);
            var interlaced = (imageFlags & 0x40) != 0;
            for (var i = 0; i < indices.Length; i++)
            {
                var row = interlaced ? InterlacedRow(i / w, h) : i / w;
                var x = left + i % w;
                var y = top + row;
                if (x >= width || y >= height)
                    continue;

                var index = indices[i];
                if (index != transparent && index < table.Length)
                    pixels[y * width + x] = table[index];
            }

            // Only the first frame.
            break;
        }

        return pixels;
    }

    private static Color32[] ReadTable(byte[] b, ref int at, int count)
    {
        var table = new Color32[count];
        for (var i = 0; i < count; i++, at += 3)
            table[i] = new Color32(b[at], b[at + 1], b[at + 2], 255);
        return table;
    }

    private static void SkipSubBlocks(byte[] b, ref int at)
    {
        while (at < b.Length && b[at] != 0)
            at += b[at] + 1;
        at++;
    }

    /// <summary>Which image row the n-th stored row of an interlaced GIF is.</summary>
    private static int InterlacedRow(int n, int height)
    {
        var pass1 = (height + 7) / 8;
        if (n < pass1) return n * 8;
        n -= pass1;
        var pass2 = (height + 3) / 8;
        if (n < pass2) return n * 8 + 4;
        n -= pass2;
        var pass3 = (height + 1) / 4;
        if (n < pass3) return n * 4 + 2;
        n -= pass3;
        return n * 2 + 1;
    }

    /// <summary>GIF's variable-width LZW, up to 12-bit codes.</summary>
    private static byte[] Lzw(List<byte> data, int minCode, int count)
    {
        var output = new byte[count];
        var written = 0;
        var clear = 1 << minCode;
        var end = clear + 1;

        var prefix = new int[4096];
        var suffix = new byte[4096];
        var length = new int[4096];
        for (var i = 0; i < clear; i++)
        {
            prefix[i] = -1;
            suffix[i] = (byte)i;
            length[i] = 1;
        }

        var codeSize = minCode + 1;
        var next = end + 1;
        var previous = -1;
        var bitPos = 0;
        var scratch = new byte[4096];

        while (written < count)
        {
            if (bitPos + codeSize > data.Count * 8)
                break;

            var code = 0;
            for (var k = 0; k < codeSize; k++, bitPos++)
            {
                if ((data[bitPos >> 3] & (1 << (bitPos & 7))) != 0)
                    code |= 1 << k;
            }

            if (code == clear)
            {
                codeSize = minCode + 1;
                next = end + 1;
                previous = -1;
                continue;
            }

            if (code == end)
                break;

            int first;
            if (previous < 0)
            {
                if (code >= clear)
                    throw new FormatException("malformed GIF data");
                output[written++] = (byte)code;
                previous = code;
                continue;
            }

            // Emit the string for `code` (or, for the one code not yet in the table, the previous
            // string plus its own first byte).
            var emit = code < next ? code : previous;
            var n = length[emit];
            for (int c = emit, k = n - 1; c >= 0; c = prefix[c], k--)
                scratch[k] = suffix[c];
            first = scratch[0];

            for (var k = 0; k < n && written < count; k++)
                output[written++] = scratch[k];
            if (code >= next && written < count)
                output[written++] = (byte)first;

            if (next < 4096)
            {
                prefix[next] = previous;
                suffix[next] = (byte)first;
                length[next] = length[previous] + 1;
                next++;
                if (next == 1 << codeSize && codeSize < 12)
                    codeSize++;
            }

            previous = code;
        }

        return output;
    }
}
