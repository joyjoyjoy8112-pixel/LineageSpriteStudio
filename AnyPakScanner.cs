using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace LineageSpriteStudio;

internal sealed class AnyPakScanner : IDisposable
{
    internal sealed record Rec(string FileName, long Offset, int FileSize, int CompressedSize, int Flags);

    private static readonly byte[] DesKey = { 0x7e, 0x21, 0x40, 0x23, 0x25, 0x5e, 0x24, 0x3c };

    public string IdxPath { get; }
    public string PakPath { get; }
    public string Format { get; private set; } = "UNKNOWN";
    public bool DesEncrypted { get; private set; }
    public List<Rec> Entries { get; private set; } = new();

    public AnyPakScanner(string idxPath)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        IdxPath = idxPath;
        PakPath = Path.ChangeExtension(idxPath, ".pak");
        if (!File.Exists(PakPath)) throw new FileNotFoundException("PAK 없음", PakPath);
        Load();
    }

    private void Load()
    {
        byte[] data = File.ReadAllBytes(IdxPath);

        if (Starts(data, "_EXTB$"))
        {
            Format = "_EXTB$";
            Entries = ParseExtB(data);
            return;
        }

        if (Starts(data, "_EXT"))
        {
            Format = "_EXT";
            int count = data.Length >= 8 ? BitConverter.ToInt32(data, 4) : -1;
            if (count < 0 || data.Length < 8L + count * 128L) throw new InvalidDataException("잘못된 _EXT");

            byte[] body = new byte[count * 128];
            Buffer.BlockCopy(data, 8, body, 0, body.Length);
            var plain = ParseFixed(body, count, 128, 16, 112, extLayout: true);
            if (plain != null) { Entries = plain; return; }

            DesTransform(body, false);
            var dec = ParseFixed(body, count, 128, 16, 112, extLayout: true);
            if (dec == null) throw new InvalidDataException("_EXT/DES 해석 실패");
            DesEncrypted = true;
            Entries = dec;
            return;
        }

        if (Starts(data, "_IDX"))
        {
            Format = "_IDX";
            int count = data.Length >= 8 ? BitConverter.ToInt32(data, 4) : -1;
            if (count <= 0 || data.Length < 8L + count * 32L) throw new InvalidDataException("잘못된 _IDX");

            byte[] body = new byte[count * 32];
            Buffer.BlockCopy(data, 8, body, 0, body.Length);
            var plain = ParseFixed(body, count, 32, 4, 20, extLayout: false);
            if (plain != null) { Entries = plain; return; }

            DesTransform(body, false);
            var dec = ParseFixed(body, count, 32, 4, 20, extLayout: false);
            if (dec == null) throw new InvalidDataException("_IDX/DES 해석 실패");
            DesEncrypted = true;
            Entries = dec;
            return;
        }

        if (Starts(data, "_RMS"))
        {
            Format = "_RMS";
            const int entrySize = 276;
            int count = data.Length >= 8 ? BitConverter.ToInt32(data, 4) : -1;
            if (count < 0 || data.Length != 8L + count * entrySize) throw new InvalidDataException("잘못된 _RMS");
            var list = new List<Rec>(count);
            for (int i = 0; i < count; i++)
            {
                int p = 8 + i * entrySize;
                long offset = BitConverter.ToUInt32(data, p);
                int size = BitConverter.ToInt32(data, p + 4);
                int csize = BitConverter.ToInt32(data, p + 8);
                int flags = BitConverter.ToInt32(data, p + 12);
                string name = NullString(data, p + 16, 260);
                if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("_RMS 이름 오류");
                list.Add(new Rec(name, offset, size, csize, flags));
            }
            Entries = list;
            return;
        }

        // 28-byte legacy: plaintext or DES-encrypted index.
        if (data.Length >= 32)
        {
            int count = BitConverter.ToInt32(data, 0);
            if (count > 0 && count <= 200000 && data.Length == 4L + count * 28L)
            {
                Format = "LEGACY28";
                byte[] body = new byte[count * 28];
                Buffer.BlockCopy(data, 4, body, 0, body.Length);

                var plain = ParseLegacy28(body, count);
                if (plain != null) { Entries = plain; return; }

                var copy = (byte[])body.Clone();
                DesTransform(copy, false);
                var dec = ParseLegacy28(copy, count);
                if (dec != null)
                {
                    Format = "LEGACY28+DES";
                    DesEncrypted = true;
                    Entries = dec;
                    return;
                }

                Format = "LEGACY28(L1?)";
                throw new InvalidDataException("Legacy 28-byte 형식이지만 L1 암호화 가능성 있음");
            }
        }

        string head = Convert.ToHexString(data.Take(Math.Min(16, data.Length)).ToArray());
        throw new InvalidDataException($"미지원 HEAD={head}");
    }

    public byte[] Extract(Rec e)
    {
        int stored = (e.Flags == 2 && e.CompressedSize > 0) ? e.CompressedSize
            : (Format == "_EXTB$" && e.CompressedSize > 0 ? e.CompressedSize : e.FileSize);
        if (stored < 0) throw new InvalidDataException("음수 크기");

        byte[] raw = new byte[stored];
        using var fs = new FileStream(PakPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Position = e.Offset;
        fs.ReadExactly(raw);

        if (DesEncrypted && (Format == "_EXT" || Format == "_IDX"))
            DesTransform(raw, false);

        if ((Format == "_EXT" || Format == "_RMS") && e.Flags == 2 && e.CompressedSize > 0)
            return Brotli(raw);

        if (Format == "_EXTB$" && e.CompressedSize > 0)
        {
            try { return Brotli(raw); }
            catch
            {
                try
                {
                    if (raw.Length > 2)
                    {
                        using var input = new MemoryStream(raw, 2, raw.Length - 2);
                        using var def = new DeflateStream(input, CompressionMode.Decompress);
                        using var output = new MemoryStream();
                        def.CopyTo(output);
                        return output.ToArray();
                    }
                }
                catch { }
            }
        }

        return raw;
    }

    private static List<Rec> ParseExtB(byte[] data)
    {
        int pos = 6;
        if (pos + 4 > data.Length) throw new InvalidDataException("_EXTB$ header");
        int count = BitConverter.ToInt32(data, pos); pos += 4;
        if (count < 0 || count > 500000) throw new InvalidDataException("_EXTB$ count");

        var list = new List<Rec>(count);
        for (int i = 0; i < count; i++)
        {
            if (pos + 12 > data.Length) throw new InvalidDataException("_EXTB$ truncated");
            long off = BitConverter.ToUInt32(data, pos); pos += 4;
            int size = BitConverter.ToInt32(data, pos); pos += 4;
            int csize = BitConverter.ToInt32(data, pos); pos += 4;
            int start = pos;
            while (pos < data.Length && data[pos] != 0) pos++;
            if (pos >= data.Length) throw new InvalidDataException("_EXTB$ filename");
            string name = Encoding.Default.GetString(data, start, pos - start);
            pos++;
            list.Add(new Rec(name, off, size, csize, csize > 0 ? 2 : 0));
        }
        return list;
    }

    private static List<Rec>? ParseFixed(byte[] body, int count, int stride, int nameOffset, int nameLength, bool extLayout)
    {
        try
        {
            var list = new List<Rec>(count);
            for (int i = 0; i < count; i++)
            {
                int p = i * stride;
                long off = BitConverter.ToUInt32(body, p);
                int size, csize = 0, flags = 0;
                if (extLayout)
                {
                    size = BitConverter.ToInt32(body, p + 4);
                    csize = BitConverter.ToInt32(body, p + 8);
                    flags = BitConverter.ToInt32(body, p + 12);
                }
                else
                {
                    size = BitConverter.ToInt32(body, p + 24);
                    flags = BitConverter.ToInt32(body, p + 28);
                }

                string name = NullString(body, p + nameOffset, nameLength);
                if (!ValidName(name)) return null;
                list.Add(new Rec(name, off, size, csize, flags));
            }
            return list;
        }
        catch { return null; }
    }

    private static List<Rec>? ParseLegacy28(byte[] body, int count)
    {
        try
        {
            var list = new List<Rec>(count);
            for (int i = 0; i < count; i++)
            {
                int p = i * 28;
                long off = BitConverter.ToUInt32(body, p);
                string name = NullString(body, p + 4, 20);
                int size = BitConverter.ToInt32(body, p + 24);
                if (!ValidName(name)) return null;
                list.Add(new Rec(name, off, size, 0, 0));
            }
            return list;
        }
        catch { return null; }
    }

    private static bool ValidName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        char c = s[0];
        return char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '\\' || c == '/';
    }

    private static string NullString(byte[] data, int offset, int max)
    {
        int end = offset;
        int lim = Math.Min(data.Length, offset + max);
        while (end < lim && data[end] != 0) end++;
        return Encoding.Default.GetString(data, offset, Math.Max(0, end - offset));
    }

    private static bool Starts(byte[] data, string s)
    {
        if (data.Length < s.Length) return false;
        for (int i = 0; i < s.Length; i++) if (data[i] != (byte)s[i]) return false;
        return true;
    }

    private static byte[] Brotli(byte[] raw)
    {
        using var input = new MemoryStream(raw);
        using var br = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        br.CopyTo(output);
        return output.ToArray();
    }

    private static void DesTransform(byte[] data, bool encrypt)
    {
        using var des = DES.Create();
        des.Key = DesKey;
        des.Mode = CipherMode.ECB;
        des.Padding = PaddingMode.None;

        using var transform = encrypt ? des.CreateEncryptor() : des.CreateDecryptor();
        int blocks = data.Length / 8;
        byte[] block = new byte[8];
        for (int i = 0; i < blocks; i++)
        {
            int p = i * 8;
            Buffer.BlockCopy(data, p, block, 0, 8);
            byte[] r = transform.TransformFinalBlock(block, 0, 8);
            Buffer.BlockCopy(r, 0, data, p, 8);
        }
    }

    public void Dispose() { }
}
