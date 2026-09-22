using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LineageSpriteStudio;

internal static class SprEncoder
{
    private const int BlockSize = 24;
    private const ushort Transparent = 0x8000;

    public static byte[] CreateFromPngs(IReadOnlyList<string> pngFiles)
    {
        if (pngFiles.Count == 0) throw new ArgumentException("PNG 프레임이 없습니다.");
        if (pngFiles.Count > 254) throw new ArgumentException("한 SPR은 최대 254프레임까지만 지원합니다.");

        var images = new List<SixLabors.ImageSharp.Image<Rgba32>>(pngFiles.Count);
        try
        {
            foreach (var f in pngFiles) images.Add(SixLabors.ImageSharp.Image.Load<Rgba32>(f));
            return Create(images);
        }
        finally
        {
            foreach (var i in images) i.Dispose();
        }
    }

    private static byte[] Create(IReadOnlyList<SixLabors.ImageSharp.Image<Rgba32>> images)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        var frames = new List<FrameInfo>();
        var allBlocks = new List<ushort[,]>();
        var blockMap = new Dictionary<string, int>();

        foreach (var image in images)
            frames.Add(Analyze(image, allBlocks, blockMap));

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
                bw.Write((byte)0);
                bw.Write((ushort)b.Id);
            }
        }

        bw.Write(allBlocks.Count);
        var encoded = allBlocks.Select(EncodeBlock).ToList();
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

    private static FrameInfo Analyze(SixLabors.ImageSharp.Image<Rgba32> image, List<ushort[,]> allBlocks, Dictionary<string, int> map)
    {
        int bx = (image.Width + BlockSize - 1) / BlockSize;
        int by = (image.Height + BlockSize - 1) / BlockSize;
        var defs = new List<BlockRef>();
        int minX=int.MaxValue,minY=int.MaxValue,maxX=int.MinValue,maxY=int.MinValue;

        for (int gy=0; gy<by; gy++)
        for (int gx=0; gx<bx; gx++)
        {
            var pix = Extract(image,gx*BlockSize,gy*BlockSize);
            if (Empty(pix)) continue;
            var key = Hash(pix);
            if (!map.TryGetValue(key,out int id))
            {
                id = allBlocks.Count;
                allBlocks.Add(pix);
                map[key] = id;
            }

            int a = gx - 2*gy;
            int b = 2*gy + (a>=0 ? a/2 : (a-1)/2);
            int aa = a < 0 ? a - 1 : a;
            int px = 24 * (b + a - aa/2);
            int py = 12 * (b - aa/2);
            minX=Math.Min(minX,px); minY=Math.Min(minY,py);
            maxX=Math.Max(maxX,px+23); maxY=Math.Max(maxY,py+23);
            defs.Add(new BlockRef(a,b,id));
        }

        if (defs.Count==0)
            return new FrameInfo(0,0,Math.Max(0,image.Width-1),Math.Max(0,image.Height-1),defs);

        return new FrameInfo(minX,minY,maxX,maxY,defs);
    }

    private static ushort[,] Extract(SixLabors.ImageSharp.Image<Rgba32> image,int sx,int sy)
    {
        var p=new ushort[24,24];
        for(int y=0;y<24;y++)
        for(int x=0;x<24;x++)
        {
            int ix=sx+x, iy=sy+y;
            if(ix>=image.Width||iy>=image.Height){p[y,x]=Transparent;continue;}
            var c=image[ix,iy];
            if(c.A<128){p[y,x]=Transparent;continue;}
            p[y,x]=(ushort)(((c.R>>3)<<10)|((c.G>>3)<<5)|(c.B>>3));
        }
        return p;
    }

    private static bool Empty(ushort[,] p)
    {
        for(int y=0;y<24;y++) for(int x=0;x<24;x++) if(p[y,x]!=Transparent) return false;
        return true;
    }

    private static string Hash(ushort[,] p)
    {
        unchecked
        {
            int h=17;
            for(int y=0;y<24;y++) for(int x=0;x<24;x++) h=h*31+p[y,x];
            return h.ToString("X8");
        }
    }

    private static byte[] EncodeBlock(ushort[,] p)
    {
        int minX=24,maxX=-1,minY=24,maxY=-1;
        for(int y=0;y<24;y++) for(int x=0;x<24;x++) if(p[y,x]!=Transparent)
        { minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y); }

        using var ms=new MemoryStream();
        using var bw=new BinaryWriter(ms);
        if(maxX<0){bw.Write((byte)0);bw.Write((byte)0);bw.Write((byte)0);bw.Write((byte)0);return ms.ToArray();}

        bw.Write((byte)minX); bw.Write((byte)minY); bw.Write((byte)0); bw.Write((byte)(maxY-minY+1));
        for(int y=minY;y<=maxY;y++)
        {
            var segs=new List<(int skip,List<ushort> pix)>();
            int x=minX;
            while(x<24)
            {
                int start=x; while(x<24&&p[y,x]==Transparent)x++;
                if(x>=24)break;
                int skip=(x-start)*2;
                var list=new List<ushort>();
                while(x<24&&p[y,x]!=Transparent){list.Add(p[y,x]);x++;}
                if(list.Count>0)segs.Add((skip,list));
            }
            bw.Write((byte)segs.Count);
            foreach(var s in segs)
            {
                bw.Write((byte)s.skip); bw.Write((byte)s.pix.Count);
                foreach(var c in s.pix)bw.Write(c);
            }
        }
        return ms.ToArray();
    }

    private sealed record BlockRef(int A,int B,int Id);
    private sealed record FrameInfo(int Left,int Top,int Right,int Bottom,List<BlockRef> Blocks);
}

internal static class SprInfo
{
    public static int FrameCount(byte[] spr)
    {
        if (spr == null || spr.Length == 0) return 0;

        int pos = 0;
        int first = spr[pos++];

        // Lineage SPR palette format:
        // 0xFF, paletteSize(0 means 256), palette entries (ushort each), frameCount
        if (first == 255)
        {
            if (pos >= spr.Length) return 0;
            int paletteSize = spr[pos++];
            if (paletteSize == 0) paletteSize = 256;

            int paletteBytes = paletteSize * 2;
            if (pos + paletteBytes >= spr.Length) return 0;
            pos += paletteBytes;

            return spr[pos];
        }

        return first;
    }
}
