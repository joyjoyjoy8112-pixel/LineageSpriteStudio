using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LineageSpriteStudio;

internal static class SprEncoder
{
    private const int BlockSize = 24;
    private const ushort Transparent = 0x8000;

    public static byte[] CreateFromPngs(
        IReadOnlyList<string> pngFiles,
        bool paletteMode = false,
        byte frameType = 0)
    {
        if (pngFiles.Count == 0) throw new ArgumentException("PNG 프레임이 없습니다.");
        if (pngFiles.Count > 254) throw new ArgumentException("한 SPR은 최대 254프레임까지만 지원합니다.");

        var images = new List<SixLabors.ImageSharp.Image<Rgba32>>(pngFiles.Count);
        try
        {
            foreach (var f in pngFiles)
                images.Add(SixLabors.ImageSharp.Image.Load<Rgba32>(f));
            return Create(images, paletteMode, frameType);
        }
        finally
        {
            foreach (var i in images) i.Dispose();
        }
    }

    private static byte[] Create(
        IReadOnlyList<SixLabors.ImageSharp.Image<Rgba32>> images,
        bool paletteMode,
        byte frameType)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        ushort[]? palette = null;
        Dictionary<ushort, byte>? paletteIndex = null;
        Func<ushort, ushort>? quantize = null;

        if (paletteMode)
        {
            palette = BuildPalette(images);
            paletteIndex = palette
                .Select((c, i) => (c, i))
                .ToDictionary(x => x.c, x => (byte)x.i);

            var nearestCache = new Dictionary<ushort, ushort>();
            quantize = color =>
            {
                if (paletteIndex.ContainsKey(color)) return color;
                if (nearestCache.TryGetValue(color, out var cached)) return cached;

                int cr = (color >> 10) & 0x1F;
                int cg = (color >> 5) & 0x1F;
                int cb = color & 0x1F;
                int best = int.MaxValue;
                ushort bestColor = palette[0];

                foreach (ushort p in palette)
                {
                    int pr = (p >> 10) & 0x1F;
                    int pg = (p >> 5) & 0x1F;
                    int pb = p & 0x1F;
                    int dr = cr - pr, dg = cg - pg, db = cb - pb;
                    int d = dr * dr + dg * dg + db * db;
                    if (d < best)
                    {
                        best = d;
                        bestColor = p;
                        if (d == 0) break;
                    }
                }

                nearestCache[color] = bestColor;
                return bestColor;
            };

            bw.Write((byte)255);
            bw.Write((byte)(palette.Length == 256 ? 0 : palette.Length));
            foreach (ushort c in palette) bw.Write(c);
        }

        var frames = new List<FrameInfo>();
        var allBlocks = new List<ushort[,]>();
        var blockMap = new Dictionary<string, int>();

        foreach (var image in images)
            frames.Add(Analyze(image, allBlocks, blockMap, quantize));

        bw.Write((byte)frames.Count);
        foreach (var f in frames)
        {
            bw.Write((short)f.Left);
            bw.Write((short)f.Top);
            bw.Write((short)f.Right);
            bw.Write((short)f.Bottom);
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            bw.Write((ushort)f.Blocks.Count);

            foreach (var b in f.Blocks)
            {
                bw.Write((sbyte)b.A);
                bw.Write((sbyte)b.B);
                bw.Write(frameType);
                bw.Write((ushort)b.Id);
            }
        }

        bw.Write(allBlocks.Count);
        var encoded = allBlocks
            .Select(p => EncodeBlock(p, paletteIndex))
            .ToList();

        int offset = 0;
        foreach (var b in encoded)
        {
            bw.Write(offset);
            offset += b.Length;
        }

        bw.Write(offset);
        foreach (var b in encoded) bw.Write(b);
        return ms.ToArray();
    }

    private static ushort[] BuildPalette(IReadOnlyList<SixLabors.ImageSharp.Image<Rgba32>> images)
    {
        var freq = new Dictionary<ushort, int>();

        foreach (var image in images)
        {
            for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                var c = image[x, y];
                if (c.A < 128) continue;

                ushort rgb = Rgb555(c);
                freq.TryGetValue(rgb, out int n);
                freq[rgb] = n + 1;
            }
        }

        if (freq.Count == 0)
            return new ushort[] { 0 };

        // 3.8 계열 palette SPR은 최대 256색 인덱스를 사용한다.
        // RGB555로 내린 뒤 사용 빈도가 높은 색부터 256색을 선택하고,
        // 나머지는 가장 가까운 palette 색으로 양자화한다.
        return freq
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Take(256)
            .Select(kv => kv.Key)
            .ToArray();
    }

    private static FrameInfo Analyze(
        SixLabors.ImageSharp.Image<Rgba32> image,
        List<ushort[,]> allBlocks,
        Dictionary<string, int> map,
        Func<ushort, ushort>? quantize)
    {
        int bx = (image.Width + BlockSize - 1) / BlockSize;
        int by = (image.Height + BlockSize - 1) / BlockSize;
        var defs = new List<BlockRef>();
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

        for (int gy = 0; gy < by; gy++)
        for (int gx = 0; gx < bx; gx++)
        {
            var pix = Extract(image, gx * BlockSize, gy * BlockSize, quantize);
            if (Empty(pix)) continue;

            var key = Hash(pix);
            if (!map.TryGetValue(key, out int id))
            {
                id = allBlocks.Count;
                allBlocks.Add(pix);
                map[key] = id;
            }

            int a = gx - 2 * gy;
            int b = 2 * gy + (a >= 0 ? a / 2 : (a - 1) / 2);
            int aa = a < 0 ? a - 1 : a;
            int px = 24 * (b + a - aa / 2);
            int py = 12 * (b - aa / 2);

            minX = Math.Min(minX, px);
            minY = Math.Min(minY, py);
            maxX = Math.Max(maxX, px + 23);
            maxY = Math.Max(maxY, py + 23);
            defs.Add(new BlockRef(a, b, id));
        }

        if (defs.Count == 0)
            return new FrameInfo(0, 0, Math.Max(0, image.Width - 1), Math.Max(0, image.Height - 1), defs);

        return new FrameInfo(minX, minY, maxX, maxY, defs);
    }

    private static ushort[,] Extract(
        SixLabors.ImageSharp.Image<Rgba32> image,
        int sx,
        int sy,
        Func<ushort, ushort>? quantize)
    {
        var p = new ushort[24, 24];

        for (int y = 0; y < 24; y++)
        for (int x = 0; x < 24; x++)
        {
            int ix = sx + x, iy = sy + y;

            if (ix >= image.Width || iy >= image.Height)
            {
                p[y, x] = Transparent;
                continue;
            }

            var c = image[ix, iy];
            if (c.A < 128)
            {
                p[y, x] = Transparent;
                continue;
            }

            ushort rgb = Rgb555(c);
            p[y, x] = quantize == null ? rgb : quantize(rgb);
        }

        return p;
    }

    private static ushort Rgb555(Rgba32 c) =>
        (ushort)(((c.R >> 3) << 10) | ((c.G >> 3) << 5) | (c.B >> 3));

    private static bool Empty(ushort[,] p)
    {
        for (int y = 0; y < 24; y++)
        for (int x = 0; x < 24; x++)
            if (p[y, x] != Transparent) return false;
        return true;
    }

    private static string Hash(ushort[,] p)
    {
        unchecked
        {
            int h = 17;
            for (int y = 0; y < 24; y++)
            for (int x = 0; x < 24; x++)
                h = h * 31 + p[y, x];
            return h.ToString("X8");
        }
    }

    private static byte[] EncodeBlock(ushort[,] p, Dictionary<ushort, byte>? paletteIndex)
    {
        int minX = 24, maxX = -1, minY = 24, maxY = -1;

        for (int y = 0; y < 24; y++)
        for (int x = 0; x < 24; x++)
            if (p[y, x] != Transparent)
            {
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        if (maxX < 0)
        {
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((byte)0);
            return ms.ToArray();
        }

        bw.Write((byte)minX);
        bw.Write((byte)minY);
        bw.Write((byte)0);
        bw.Write((byte)(maxY - minY + 1));

        for (int y = minY; y <= maxY; y++)
        {
            var segs = new List<(int skip, List<ushort> pix)>();
            int x = minX;

            while (x < 24)
            {
                int start = x;
                while (x < 24 && p[y, x] == Transparent) x++;
                if (x >= 24) break;

                int skip = (x - start) * 2;
                var list = new List<ushort>();
                while (x < 24 && p[y, x] != Transparent)
                {
                    list.Add(p[y, x]);
                    x++;
                }

                if (list.Count > 0)
                    segs.Add((skip, list));
            }

            bw.Write((byte)segs.Count);
            foreach (var s in segs)
            {
                bw.Write((byte)s.skip);
                bw.Write((byte)s.pix.Count);

                foreach (var c in s.pix)
                {
                    if (paletteIndex != null)
                    {
                        if (!paletteIndex.TryGetValue(c, out byte pi))
                            throw new InvalidDataException("palette index를 찾지 못했습니다.");
                        bw.Write(pi);
                    }
                    else
                    {
                        bw.Write(c);
                    }
                }
            }
        }

        return ms.ToArray();
    }

    private sealed record BlockRef(int A, int B, int Id);
    private sealed record FrameInfo(int Left, int Top, int Right, int Bottom, List<BlockRef> Blocks);
}

internal sealed record SprFormatInfo(
    int FrameCount,
    bool IsPalette,
    int PaletteSize,
    byte FrameType);

internal static class SprInfo
{
    public static int FrameCount(byte[] spr) => Analyze(spr).FrameCount;

    public static SprFormatInfo Analyze(byte[] spr)
    {
        if (spr == null || spr.Length == 0)
            return new SprFormatInfo(0, false, 0, 0);

        try
        {
            using var br = new BinaryReader(new MemoryStream(spr));
            int first = br.ReadByte();
            bool palette = first == 255;
            int paletteSize = 0;
            int frameCount;

            if (palette)
            {
                int storedPaletteSize = br.ReadByte();
                paletteSize = storedPaletteSize == 0 ? 256 : storedPaletteSize;
                br.BaseStream.Seek(paletteSize * 2L, SeekOrigin.Current);
                frameCount = br.ReadByte();
            }
            else
            {
                frameCount = first;
            }

            if (frameCount <= 0 || frameCount > 254)
                return new SprFormatInfo(frameCount, palette, paletteSize, 0);

            var typeCounts = new Dictionary<byte, int>();

            for (int i = 0; i < frameCount; i++)
            {
                br.BaseStream.Seek(12, SeekOrigin.Current);
                int blockCount = br.ReadUInt16();

                for (int j = 0; j < blockCount; j++)
                {
                    _ = br.ReadSByte();
                    _ = br.ReadSByte();
                    byte type = br.ReadByte();
                    _ = br.ReadUInt16();

                    typeCounts.TryGetValue(type, out int n);
                    typeCounts[type] = n + 1;
                }
            }

            byte frameType = typeCounts.Count == 0
                ? (byte)0
                : typeCounts.OrderByDescending(x => x.Value).ThenBy(x => x.Key).First().Key;

            return new SprFormatInfo(frameCount, palette, paletteSize, frameType);
        }
        catch
        {
            return new SprFormatInfo(0, false, 0, 0);
        }
    }
}
