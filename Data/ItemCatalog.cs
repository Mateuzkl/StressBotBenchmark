using System.Buffers.Binary;

namespace StressBotBenchmark.Data;

public readonly record struct ItemFlags(bool ExtraByte, bool Ground, bool Blocking, bool OnTop);

// Immutable, shared between bots. Reads only metadata from the server's local
// OTB; the file itself is never copied into scripts or committed to the repo.
public sealed class ItemCatalog
{
    private readonly Dictionary<ushort, ItemFlags> items;
    public int Count => items.Count;
    public static ItemCatalog? Current { get; private set; }
    private ItemCatalog(Dictionary<ushort, ItemFlags> items) => this.items = items;
    public ItemFlags Get(ushort id) => items.TryGetValue(id, out var flags) ? flags :
        throw new InvalidDataException($"Client item {id} is absent from the configured OTB.");
    public static void Load(string path)
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("OTB exceeds the metadata size limit.");
        Current = Parse(File.ReadAllBytes(path));
    }

    public static ItemCatalog Parse(byte[] bytes)
    {
        if (bytes.Length < 7 || bytes.Length > 16 * 1024 * 1024 ||
            !(bytes.AsSpan(0, 4).SequenceEqual(new byte[4]) ||
              bytes.AsSpan(0, 4).SequenceEqual("OTBI"u8)))
            throw new InvalidDataException("Invalid items.otb signature or size.");
        int position = 4;
        var entries = new Dictionary<ushort, ItemFlags>();
        ReadNode(0);
        if (position != bytes.Length || entries.Count == 0)
            throw new InvalidDataException("Empty or trailing OTB data.");
        return new ItemCatalog(entries);

        byte Read() => position < bytes.Length ? bytes[position++] : throw new InvalidDataException("Truncated OTB.");
        void ReadNode(int depth)
        {
            if (depth > 2 || Read() != 0xFE) throw new InvalidDataException("Invalid OTB node.");
            byte group = Read();
            var properties = new List<byte>();
            while (position < bytes.Length && bytes[position] != 0xFE && bytes[position] != 0xFF)
            {
                byte value = Read();
                properties.Add(value == 0xFD ? Read() : value);
            }
            if (depth == 1)
            {
                var data = properties.ToArray();
                if (data.Length < 4) throw new InvalidDataException("Missing OTB flags.");
                uint flags = BinaryPrimitives.ReadUInt32LittleEndian(data);
                ushort id = 0;
                for (int offset = 4; offset < data.Length;)
                {
                    if (data.Length - offset < 3) throw new InvalidDataException("Truncated OTB attribute.");
                    byte attr = data[offset++];
                    int length = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
                    offset += 2;
                    if (length > data.Length - offset) throw new InvalidDataException("Truncated OTB value.");
                    if (attr == 0x11)
                    {
                        if (length != 2) throw new InvalidDataException("Invalid OTB client ID.");
                        id = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
                    }
                    offset += length;
                }
                if (id != 0) entries[id] = new ItemFlags((flags & (1 << 7)) != 0 || group is 11 or 12,
                    group == 1, (flags & 5) != 0, (flags & (1 << 13)) != 0);
            }
            while (position < bytes.Length && bytes[position] == 0xFE) ReadNode(depth + 1);
            if (Read() != 0xFF) throw new InvalidDataException("Unterminated OTB node.");
        }
    }
}
