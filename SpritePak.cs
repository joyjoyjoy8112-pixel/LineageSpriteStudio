using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace LineageSpriteStudio;

internal sealed class SpritePak : IDisposable
{
    private static readonly byte[] DesKey = { 0x7e, 0x21, 0x40, 0x23, 0x25, 0x5e, 0x24, 0x3c };
    public string IdxPath { get; }
    public string PakPath { get; }
    public bool IsDesEncrypted { get; private set; }
    public string IndexFormat { get; private set; } = "UNKNOWN";
    public List<Entry> Entries { get; private set; } = new();

    public SpritePak(string idxPath)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        IdxPath = idxPath;
        PakPath = Path.ChangeExtension(idxPath, ".pak");
        if (!File.Exists(PakPath)) throw new FileNotFoundException("PAK 파일을 찾을 수 없습니다.", PakPath);
        Load();
    }

    private void Load()
    {
        var data = File.ReadAllBytes(IdxPath);

        // Newer 128-byte _EXT index.
        if (data.Length >= 8 && data[0] == '_' && data[1] == 'E' && data[2] == 'X' && data[3] == 'T')
        {
            IndexFormat = "_EXT";

            int count = BitConverter.ToInt32(data, 4);
            if (count < 0 || data.Length < 8L + count * 128L)
                throw new InvalidDataException($"IDX 레코드 수가 잘못되었습니다: {Path.GetFileName(IdxPath)}");

            var body = new byte[count * 128];
            Buffer.BlockCopy(data, 8, body, 0, body.Length);

            var plain = Parse(body, count);
            if (plain != null)
            {
                IsDesEncrypted = false;
                Entries = plain;
                return;
            }

            DesTransform(body, false);
            var dec = Parse(body, count);
            if (dec == null)
                throw new InvalidDataException($"IDX를 해석하지 못했습니다: {Path.GetFileName(IdxPath)}");

            IsDesEncrypted = true;
            Entries = dec;
            return;
        }

        // Older Lineage 28-byte index:
        // int32 count + count * [uint32 offset + char[20] filename + int32 fileSize].
        // The user's current Sprite IDX files match this exactly, e.g.
        // Sprite00.idx: 4 + 0x1886 * 28 = 175,788 bytes.
        if (data.Length >= 32)
        {
            int count = BitConverter.ToInt32(data, 0);
            long expected = 4L + count * 28L;
            if (count > 0 && count <= 200000 && expected == data.Length)
            {
                var legacy = ParseLegacy28(data, count);
                if (legacy != null)
                {
                    IndexFormat = "LEGACY28";
                    IsDesEncrypted = false;
                    Entries = legacy;
                    return;
                }
            }
        }

        string hex = Convert.ToHexString(data.Take(Math.Min(16, data.Length)).ToArray());
        string ascii = new string(data.Take(Math.Min(16, data.Length))
            .Select(b => b >= 32 && b <= 126 ? (char)b : '.').ToArray());
        throw new InvalidDataException(
            $"지원하지 않는 IDX 형식: {Path.GetFileName(IdxPath)} / 크기 {data.Length:N0} / HEAD {hex} / ASCII {ascii}");
    }

    private static List<Entry>? Parse(byte[] body, int count)
    {
        try
        {
            var list = new List<Entry>(count);
            for (int i = 0; i < count; i++)
            {
                int p = i * 128;
                uint offset = BitConverter.ToUInt32(body, p);
                int fileSize = BitConverter.ToInt32(body, p + 4);
                int compressedSize = BitConverter.ToInt32(body, p + 8);
                int flags = BitConverter.ToInt32(body, p + 12);
                int end = Array.IndexOf(body, (byte)0, p + 16, 112);
                if (end < 0) end = p + 128;
                string name = Encoding.Default.GetString(body, p + 16, end - (p + 16));
                if (string.IsNullOrWhiteSpace(name)) return null;
                char first = name[0];
                if (!(char.IsLetterOrDigit(first) || first == '_' || first == '.')) return null;
                list.Add(new Entry
                {
                    Offset = offset,
                    FileSize = fileSize,
                    CompressedSize = compressedSize,
                    Flags = flags,
                    FileName = name
                });
            }
            return list;
        }
        catch { return null; }
    }

    private static List<Entry>? ParseLegacy28(byte[] data, int count)
    {
        try
        {
            var list = new List<Entry>(count);
            for (int i = 0; i < count; i++)
            {
                int p = 4 + i * 28;
                uint offset = BitConverter.ToUInt32(data, p);

                int nameEnd = p + 4;
                int nameLimit = p + 24;
                while (nameEnd < nameLimit && data[nameEnd] != 0) nameEnd++;

                string name = Encoding.Default.GetString(data, p + 4, nameEnd - (p + 4));
                int fileSize = BitConverter.ToInt32(data, p + 24);

                if (string.IsNullOrWhiteSpace(name) || fileSize < 0) return null;
                char first = name[0];
                if (!(char.IsLetterOrDigit(first) || first == '_' || first == '.')) return null;

                list.Add(new Entry
                {
                    Offset = offset,
                    FileSize = fileSize,
                    CompressedSize = 0,
                    Flags = 0,
                    FileName = name
                });
            }
            return list;
        }
        catch { return null; }
    }

    public byte[] Extract(Entry e)
    {
        int stored = (e.Flags == 2 && e.CompressedSize > 0) ? e.CompressedSize : e.FileSize;
        var raw = new byte[stored];
        using var fs = new FileStream(PakPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Position = e.Offset;
        fs.ReadExactly(raw);
        if (IsDesEncrypted) DesTransform(raw, false);
        if (e.Flags == 2 && e.CompressedSize > 0)
        {
            using var input = new MemoryStream(raw);
            using var br = new BrotliStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            br.CopyTo(output);
            return output.ToArray();
        }
        return raw;
    }

    public void RebuildLegacyPak(IReadOnlyDictionary<string, byte[]> replacements)
    {
        if (!IndexFormat.Equals("LEGACY28", StringComparison.OrdinalIgnoreCase) || IsDesEncrypted)
            throw new InvalidOperationException("안전 재묶기는 비암호화 LEGACY28 Sprite PAK에서만 사용할 수 있습니다.");

        string tmpPak = PakPath + ".sprite-studio-repack.tmp";
        if (File.Exists(tmpPak)) File.Delete(tmpPak);

        // 원본 offset/size를 먼저 고정해 둔다. Entries는 새 PAK 위치로 갱신된다.
        var originals = Entries
            .Select(e => new
            {
                Entry = e,
                Offset = e.Offset,
                Size = e.FileSize
            })
            .ToList();

        try
        {
            using (var src = new FileStream(PakPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var dst = new FileStream(tmpPak, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[1024 * 1024];

                foreach (var item in originals)
                {
                    long newOffset = dst.Position;
                    if (newOffset > uint.MaxValue)
                        throw new InvalidDataException("새 PAK offset이 32비트 범위를 초과했습니다.");

                    if (replacements.TryGetValue(item.Entry.FileName, out var replacement))
                    {
                        dst.Write(replacement, 0, replacement.Length);
                        item.Entry.Offset = newOffset;
                        item.Entry.FileSize = replacement.Length;
                    }
                    else
                    {
                        if (item.Offset < 0 || item.Size < 0 || item.Offset + item.Size > src.Length)
                            throw new InvalidDataException($"원본 PAK 범위 오류: {item.Entry.FileName}");

                        src.Position = item.Offset;
                        int remain = item.Size;
                        while (remain > 0)
                        {
                            int want = Math.Min(buffer.Length, remain);
                            int read = src.Read(buffer, 0, want);
                            if (read <= 0)
                                throw new EndOfStreamException($"원본 PAK 읽기 실패: {item.Entry.FileName}");
                            dst.Write(buffer, 0, read);
                            remain -= read;
                        }

                        item.Entry.Offset = newOffset;
                        item.Entry.FileSize = item.Size;
                    }
                }

                dst.Flush(true);
            }

            File.Move(tmpPak, PakPath, true);
            SaveIndex();
        }
        catch
        {
            try { if (File.Exists(tmpPak)) File.Delete(tmpPak); } catch { }
            throw;
        }
    }

    public long AppendRaw(byte[] rawData)
    {
        byte[] stored = rawData;
        if (IsDesEncrypted)
        {
            stored = (byte[])rawData.Clone();
            DesTransform(stored, true);
        }

        using var fs = new FileStream(PakPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        long offset = fs.Position;
        fs.Write(stored);
        fs.Flush(true);
        return offset;
    }

    public void SaveIndex()
    {
        byte[] result;

        if (IndexFormat == "LEGACY28")
        {
            result = new byte[4 + Entries.Count * 28];
            BitConverter.GetBytes(Entries.Count).CopyTo(result, 0);

            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                int p = 4 + i * 28;

                BitConverter.GetBytes((uint)e.Offset).CopyTo(result, p);

                var nameBytes = Encoding.Default.GetBytes(e.FileName);
                if (nameBytes.Length > 20)
                    throw new InvalidDataException($"LEGACY28 파일명이 20바이트를 초과합니다: {e.FileName}");

                Buffer.BlockCopy(nameBytes, 0, result, p + 4, nameBytes.Length);
                BitConverter.GetBytes(e.FileSize).CopyTo(result, p + 24);
            }
        }
        else
        {
            var body = new byte[Entries.Count * 128];
            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                int p = i * 128;
                BitConverter.GetBytes((uint)e.Offset).CopyTo(body, p);
                BitConverter.GetBytes(e.FileSize).CopyTo(body, p + 4);
                BitConverter.GetBytes(e.CompressedSize).CopyTo(body, p + 8);
                BitConverter.GetBytes(e.Flags).CopyTo(body, p + 12);
                var nameBytes = Encoding.Default.GetBytes(e.FileName);
                Buffer.BlockCopy(nameBytes, 0, body, p + 16, Math.Min(111, nameBytes.Length));
            }

            if (IsDesEncrypted) DesTransform(body, true);
            result = new byte[8 + body.Length];
            result[0] = (byte)'_'; result[1] = (byte)'E'; result[2] = (byte)'X'; result[3] = (byte)'T';
            BitConverter.GetBytes(Entries.Count).CopyTo(result, 4);
            Buffer.BlockCopy(body, 0, result, 8, body.Length);
        }

        string tmp = IdxPath + ".sprite-studio.tmp";
        File.WriteAllBytes(tmp, result);
        File.Move(tmp, IdxPath, true);
    }

    public static int ExpectedPakIndex(string fileName)
    {
        var bytes = Encoding.Default.GetBytes(fileName);
        int sum = 0;
        foreach (var b in bytes) sum += b;
        return sum & 0x0F;
    }

    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    private static void DesTransform(byte[] data, bool encrypt)
    {
        using var des = DES.Create();
        des.Key = DesKey;
        des.Mode = CipherMode.ECB;
        des.Padding = PaddingMode.None;
        using var transform = encrypt ? des.CreateEncryptor() : des.CreateDecryptor();
        int blocks = data.Length / 8;
        var block = new byte[8];
        for (int i = 0; i < blocks; i++)
        {
            int p = i * 8;
            Buffer.BlockCopy(data, p, block, 0, 8);
            var r = transform.TransformFinalBlock(block, 0, 8);
            Buffer.BlockCopy(r, 0, data, p, 8);
        }
    }

    public void Dispose() { }

    internal sealed class Entry
    {
        public long Offset;
        public int FileSize;
        public int CompressedSize;
        public int Flags;
        public string FileName = "";
    }
}

internal sealed record SpriteTarget(int PakIndex, string IdxPath, SpritePak Pak, SpritePak.Entry Entry, int Part);

internal static class SpriteCodec
{
    public static bool IsZlib(byte[] data)
    {
        if (data == null || data.Length < 2 || data[0] != 0x78) return false;
        return data[1] == 0x9C || data[1] == 0xDA || data[1] == 0x01 || data[1] == 0x5E;
    }

    public static byte[] DecodeIfNeeded(byte[] data)
    {
        if (!IsZlib(data)) return data;
        using var input = new MemoryStream(data);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }

    public static byte[] EncodeZlib(byte[] data)
    {
        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data, 0, data.Length);
        return output.ToArray();
    }
}
