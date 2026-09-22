namespace LineageSpriteStudio;

internal static class SprDecoder
{
    private sealed record BlockDef(int A, int B, int FrameType, int BlockId);

    public static Bitmap DecodeFrame(byte[] sprData, int frameIndex = 0)
    {
        if (sprData == null || sprData.Length == 0)
            throw new InvalidDataException("SPR 데이터가 비어 있습니다.");

        using var br = new BinaryReader(new MemoryStream(sprData));

        ushort[]? palette = null;
        int frameCount = br.ReadByte();

        if (frameCount == 255)
        {
            int paletteSize = br.ReadByte();
            if (paletteSize == 0) paletteSize = 256;
            palette = new ushort[paletteSize];
            for (int i = 0; i < paletteSize; i++)
                palette[i] = br.ReadUInt16();
            frameCount = br.ReadByte();
        }

        if (frameCount <= 0)
            throw new InvalidDataException("SPR 프레임이 없습니다.");

        frameIndex = Math.Clamp(frameIndex, 0, frameCount - 1);

        var defs = new BlockDef[frameCount][];
        for (int i = 0; i < frameCount; i++)
        {
            _ = br.ReadInt16(); // left
            _ = br.ReadInt16(); // top
            _ = br.ReadInt16(); // right
            _ = br.ReadInt16(); // bottom
            _ = br.ReadUInt16();
            _ = br.ReadUInt16();

            int blockCount = br.ReadUInt16();
            defs[i] = new BlockDef[blockCount];
            for (int j = 0; j < blockCount; j++)
            {
                int a = br.ReadSByte();
                int b = br.ReadSByte();
                int type = br.ReadByte();
                int blockId = br.ReadUInt16();
                defs[i][j] = new BlockDef(a, b, type, blockId);
            }
        }

        int blockTableSize = br.ReadInt32();
        if (blockTableSize < 0 || blockTableSize > 1_000_000)
            throw new InvalidDataException("SPR 블록 테이블이 잘못되었습니다.");

        var offsets = new int[blockTableSize];
        for (int i = 0; i < blockTableSize; i++)
            offsets[i] = br.ReadInt32();

        _ = br.ReadInt32(); // end offset / unknown
        int dataStart = checked((int)br.BaseStream.Position);

        var wanted = defs[frameIndex];
        if (wanted.Length == 0)
            return new Bitmap(1, 1);

        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;

        foreach (var d in wanted)
        {
            var (x, y) = BlockPosition(d.A, d.B);
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x + 23);
            maxY = Math.Max(maxY, y + 23);
        }

        int width = Math.Max(1, maxX - minX + 1);
        int height = Math.Max(1, maxY - minY + 1);
        var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        foreach (var d in wanted)
        {
            if (d.BlockId < 0 || d.BlockId >= offsets.Length) continue;
            var block = ReadBlock(br, dataStart + offsets[d.BlockId], palette);
            var (blockX, blockY) = BlockPosition(d.A, d.B);

            for (int y = 0; y < 24; y++)
            for (int x = 0; x < 24; x++)
            {
                ushort c = block[y, x];
                if (c == 0x8000) continue;

                int px = blockX + x - minX;
                int py = blockY + y - minY;
                if ((uint)px >= (uint)width || (uint)py >= (uint)height) continue;

                bmp.SetPixel(px, py, Rgb555ToColor(c));
            }
        }

        return bmp;
    }

    private static ushort[,] ReadBlock(BinaryReader br, long offset, ushort[]? palette)
    {
        var block = new ushort[24, 24];
        for (int y = 0; y < 24; y++)
            for (int x = 0; x < 24; x++)
                block[y, x] = 0x8000;

        br.BaseStream.Seek(offset, SeekOrigin.Begin);
        int startX = br.ReadByte();
        int startY = br.ReadByte();
        _ = br.ReadByte();
        int lineCount = br.ReadByte();

        for (int line = 0; line < lineCount; line++)
        {
            int y = startY + line;
            if (y < 0 || y >= 24) break;

            int x = startX;
            int segmentCount = br.ReadByte();
            for (int seg = 0; seg < segmentCount; seg++)
            {
                x += br.ReadByte() / 2;
                int pixelCount = br.ReadByte();

                for (int p = 0; p < pixelCount; p++)
                {
                    ushort color;
                    if (palette != null)
                    {
                        int pi = br.ReadByte();
                        color = pi >= 0 && pi < palette.Length ? palette[pi] : (ushort)0x8000;
                    }
                    else
                    {
                        color = br.ReadUInt16();
                    }

                    if (x >= 0 && x < 24)
                        block[y, x] = color;
                    x++;
                }
            }
        }

        return block;
    }

    private static (int x, int y) BlockPosition(int a, int b)
    {
        int adj = a;
        if (adj < 0) adj--;
        int x = 24 * (b + a - adj / 2);
        int y = 12 * (b - adj / 2);
        return (x, y);
    }

    private static Color Rgb555ToColor(ushort c)
    {
        int r5 = (c >> 10) & 0x1F;
        int g5 = (c >> 5) & 0x1F;
        int b5 = c & 0x1F;
        int r = (r5 << 3) | (r5 >> 2);
        int g = (g5 << 3) | (g5 >> 2);
        int b = (b5 << 3) | (b5 >> 2);
        return Color.FromArgb(255, r, g, b);
    }
}
