using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace BlazorPackageResolver;

internal static class Lp01ArchiveIntegrity
{
    private const int MaxUncompressedBytes = 64 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    internal static readonly Encoding Cp437 = CreateCp437();
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(index =>
    {
        var crc = (uint)index;
        for (var bit = 0; bit < 8; bit++)
            crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0u);
        return crc;
    }).ToArray();

    internal static bool Validate(ReadOnlySpan<byte> bytes, IReadOnlyList<ZipArchiveEntry> entries)
    {
        if (!TryEnd(bytes, out var start, out var size, out var count) || entries.Count != count)
            return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        var cursor = (long)start;
        for (var index = 0; index < count; index++)
        {
            if (cursor > (long)start + size - 46 || !Signature(bytes, cursor, 0x02014b50))
                return false;
            var head = bytes.Slice((int)cursor);
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(head[8..]);
            var method = BinaryPrimitives.ReadUInt16LittleEndian(head[10..]);
            var crc = BinaryPrimitives.ReadUInt32LittleEndian(head[16..]);
            var compressed = BinaryPrimitives.ReadUInt32LittleEndian(head[20..]);
            var uncompressed = BinaryPrimitives.ReadUInt32LittleEndian(head[24..]);
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(head[28..]);
            var extra = BinaryPrimitives.ReadUInt16LittleEndian(head[30..]);
            var comment = BinaryPrimitives.ReadUInt16LittleEndian(head[32..]);
            var local = BinaryPrimitives.ReadUInt32LittleEndian(head[42..]);
            var next = cursor + 46L + nameLength + extra + comment;
            if (nameLength == 0 || nameLength > 2048 || next > (long)start + size ||
                local > bytes.Length - 30 || !Signature(bytes, local, 0x04034b50) ||
                compressed == uint.MaxValue || uncompressed == uint.MaxValue)
                return false;
            var rawName = bytes.Slice((int)cursor + 46, nameLength);
            var localHead = bytes.Slice((int)local);
            var localFlags = BinaryPrimitives.ReadUInt16LittleEndian(localHead[6..]);
            var localMethod = BinaryPrimitives.ReadUInt16LittleEndian(localHead[8..]);
            var localCrc = BinaryPrimitives.ReadUInt32LittleEndian(localHead[14..]);
            var localCompressed = BinaryPrimitives.ReadUInt32LittleEndian(localHead[18..]);
            var localUncompressed = BinaryPrimitives.ReadUInt32LittleEndian(localHead[22..]);
            var localNameLength = BinaryPrimitives.ReadUInt16LittleEndian(localHead[26..]);
            var localExtraLength = BinaryPrimitives.ReadUInt16LittleEndian(localHead[28..]);
            if ((flags & 1) != 0 || localFlags != flags || localMethod != method ||
                localNameLength != nameLength ||
                (long)local + 30 + localNameLength + localExtraLength > bytes.Length ||
                ((flags & 8) == 0 &&
                    (localCrc != crc || localCompressed != compressed ||
                     localUncompressed != uncompressed)) ||
                ((flags & 8) != 0 &&
                    (localCrc is not 0 && localCrc != crc ||
                     localCompressed is not 0 && localCompressed != compressed ||
                     localUncompressed is not 0 && localUncompressed != uncompressed)) ||
                !rawName.SequenceEqual(bytes.Slice((int)local + 30, localNameLength)))
                return false;
            string name;
            try { name = ((flags & 0x800) != 0 ? StrictUtf8 : Cp437).GetString(rawName); }
            catch (DecoderFallbackException) { return false; }
            if (name.Length > 512 || name.Any(ch => ch < 32 || ch == 127) ||
                !names.Add(Lp01CaseFold.Fold(name)) ||
                entries[index].FullName != name || entries[index].Length != uncompressed ||
                entries[index].CompressedLength != compressed || entries[index].Crc32 != crc)
                return false;
            cursor = next;
        }
        if (cursor != (long)start + size) return false;

        long inspected = 0;
        var chunk = new byte[8192];
        foreach (var entry in entries)
        {
            if (entry.Length > MaxUncompressedBytes - inspected) return false;
            uint crc = uint.MaxValue;
            long length = 0;
            using var stream = entry.Open();
            int read;
            while ((read = stream.Read(chunk)) > 0)
            {
                length += read;
                if (length > entry.Length || inspected + length > MaxUncompressedBytes)
                    return false;
                foreach (var value in chunk.AsSpan(0, read))
                    crc = CrcTable[(int)((crc ^ value) & 0xff)] ^ (crc >> 8);
            }
            if (length != entry.Length || ~crc != entry.Crc32) return false;
            inspected += length;
        }
        return true;
    }

    private static Encoding CreateCp437()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(437, EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
    }

    private static bool Signature(ReadOnlySpan<byte> bytes, long offset, uint signature) =>
        offset >= 0 && offset <= bytes.Length - 4 &&
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice((int)offset, 4)) == signature;

    private static bool TryEnd(ReadOnlySpan<byte> bytes, out uint start, out uint size, out int count)
    {
        start = size = 0;
        count = 0;
        for (var end = bytes.Length - 22; end >= Math.Max(0, bytes.Length - 65557); end--)
        {
            if (!Signature(bytes, end, 0x06054b50) ||
                end + 22 + BinaryPrimitives.ReadUInt16LittleEndian(bytes[(end + 20)..]) != bytes.Length)
                continue;
            count = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(end + 10)..]);
            size = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(end + 12)..]);
            start = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(end + 16)..]);
            return count <= 4096 && size <= 8 * 1024 * 1024 &&
                (ulong)start + size <= (ulong)end;
        }
        return false;
    }
}
