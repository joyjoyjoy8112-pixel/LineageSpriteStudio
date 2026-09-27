namespace LineageSpriteStudio;

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
