using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CompareVPN.Tools.IspChecker.Tests;

/// <summary>
/// Writes small location files for tests, laid out like the DB-IP "IP to City Lite" file, so no test depends on the
/// real download.
///
/// In plain terms, here is what happens:
///   1. Each block of addresses is turned into a path of left/right steps (one per bit) in a tree, ending at its
///      place's details.
///   2. The details are written in the file format's own compact encoding.
///   3. The tree, the details and a short description of the file are written one after another.
///   It only supports what the tests need: no overlapping blocks, and records small enough for 24-bit pointers.
/// </summary>
internal static class TestLocationFile
{
    // The file's description starts after this marker, as in every file of this format.
    private static readonly byte[] DescriptionMarker = [0xAB, 0xCD, 0xEF, .. "MaxMind.com"u8];

    /// <summary>The details DB-IP stores for one block of addresses. Leave a value null to leave it out.</summary>
    public static Dictionary<string, object> Place(string? city, string? region, string? countryCode, double? latitude, double? longitude)
    {
        var place = new Dictionary<string, object>();
        if (city is not null) place["city"] = Names(city);
        if (region is not null) place["subdivisions"] = new object[] { Names(region) };
        if (countryCode is not null) place["country"] = new Dictionary<string, object> { ["iso_code"] = countryCode };
        if (latitude is not null && longitude is not null)
        {
            place["location"] = new Dictionary<string, object> { ["latitude"] = latitude.Value, ["longitude"] = longitude.Value };
        }

        return place;
    }

    /// <summary>
    /// Writes a file at <paramref name="path"/> where each block (e.g. <c>"8.8.8.0/24"</c> or <c>"2001:db8::/32"</c>)
    /// leads to its place's details.
    /// </summary>
    public static void Write(string path, params (string Block, Dictionary<string, object> Place)[] blocks)
    {
        // Step 1 and 2: Build the tree and encode each place's details.
        var details = new MemoryStream();
        var root = new Node();
        foreach (var (block, place) in blocks)
        {
            var offset = (int)details.Position;
            Encode(details, place);
            Insert(root, ToBits(block), offset);
        }

        // Step 3: Tree, a 16-byte gap, the details, then the file's description.
        var nodes = Number(root);
        using var file = File.Create(path);
        WriteTree(file, nodes);
        file.Write(new byte[16]);
        details.WriteTo(file);
        file.Write(DescriptionMarker);
        Encode(file, Description(nodes.Count));
    }

    private static Dictionary<string, object> Names(string english) =>
        new() { ["names"] = new Dictionary<string, object> { ["en"] = english } };

    private static Dictionary<string, object> Description(int nodeCount) => new()
    {
        ["node_count"] = (uint)nodeCount,
        ["record_size"] = (ushort)24,
        ["ip_version"] = (ushort)6,
        ["database_type"] = "DBIP-City-Lite",
        ["languages"] = new object[] { "en" },
        ["binary_format_major_version"] = (ushort)2,
        ["binary_format_minor_version"] = (ushort)0,
        ["build_epoch"] = 1_793_500_000UL,
        ["description"] = new Dictionary<string, object> { ["en"] = "Test location file" },
    };

    // ───── Helpers for the tree (step 1) ─────

    /// <summary>One fork in the tree. Each side holds another fork, the offset of a place's details, or nothing.</summary>
    private sealed class Node
    {
        public readonly object?[] Sides = new object?[2];
    }

    /// <summary>
    /// The bits that pick out a block, as written in an address-family-wide tree: IPv4 blocks sit under 96 zero bits,
    /// which is where readers of this format look for IPv4 addresses.
    /// </summary>
    private static bool[] ToBits(string block)
    {
        var parts = block.Split('/');
        var address = IPAddress.Parse(parts[0]);
        var prefix = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        var isV4 = address.AddressFamily == AddressFamily.InterNetwork;
        var bytes = isV4 ? [.. new byte[12], .. address.GetAddressBytes()] : address.GetAddressBytes();
        var bitCount = isV4 ? 96 + prefix : prefix;

        // Bit i is in byte i / 8, counting from the left within that byte.
        return [.. Enumerable.Range(0, bitCount).Select(i => (bytes[i / 8] & (0x80 >> (i % 8))) != 0)];
    }

    private static void Insert(Node root, bool[] bits, int detailsOffset)
    {
        var node = root;
        for (var i = 0; i < bits.Length - 1; i++)
        {
            var side = bits[i] ? 1 : 0;
            node = (Node)(node.Sides[side] ??= new Node());
        }

        node.Sides[bits[^1] ? 1 : 0] = detailsOffset;
    }

    /// <summary>Lists the forks breadth-first, so each one's number is its position in the list.</summary>
    private static List<Node> Number(Node root)
    {
        var nodes = new List<Node> { root };
        for (var i = 0; i < nodes.Count; i++)
        {
            nodes.AddRange(nodes[i].Sides.OfType<Node>());
        }

        return nodes;
    }

    /// <summary>
    /// Writes each fork as two 3-byte numbers: a fork's number, "nothing here" (the number of forks), or a place's
    /// details (the number of forks + 16 + its offset among the details).
    /// </summary>
    private static void WriteTree(Stream file, List<Node> nodes)
    {
        var numbers = nodes.Select((node, index) => (node, index)).ToDictionary(n => n.node, n => n.index);
        foreach (var node in nodes)
        {
            foreach (var side in node.Sides)
            {
                var value = side switch
                {
                    Node child => numbers[child],
                    int offset => nodes.Count + 16 + offset,
                    _ => nodes.Count,
                };
                file.Write([(byte)(value >> 16), (byte)(value >> 8), (byte)value]);
            }
        }
    }

    // ───── Helpers for encoding the details (step 2) ─────

    /// <summary>
    /// Writes one value. Each starts with a control byte: the top three bits are the type and the low five the size;
    /// types above 7 put 0 in those bits and add a byte with the type minus 7.
    /// </summary>
    private static void Encode(Stream stream, object value)
    {
        switch (value)
        {
            case string text:
                var utf8 = Encoding.UTF8.GetBytes(text);
                WriteControl(stream, type: 2, utf8.Length);
                stream.Write(utf8);
                break;
            case double number:
                WriteControl(stream, type: 3, size: 8);
                Span<byte> eight = stackalloc byte[8];
                BinaryPrimitives.WriteDoubleBigEndian(eight, number);
                stream.Write(eight);
                break;
            case ushort small:
                WriteControl(stream, type: 5, size: 2);
                stream.Write([(byte)(small >> 8), (byte)small]);
                break;
            case uint medium:
                WriteControl(stream, type: 6, size: 4);
                Span<byte> four = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(four, medium);
                stream.Write(four);
                break;
            case ulong large:
                WriteControl(stream, type: 9, size: 8);
                Span<byte> eightMore = stackalloc byte[8];
                BinaryPrimitives.WriteUInt64BigEndian(eightMore, large);
                stream.Write(eightMore);
                break;
            case Dictionary<string, object> map:
                WriteControl(stream, type: 7, map.Count);
                foreach (var (key, item) in map)
                {
                    Encode(stream, key);
                    Encode(stream, item);
                }

                break;
            case object[] list:
                WriteControl(stream, type: 11, list.Length);
                foreach (var item in list)
                {
                    Encode(stream, item);
                }

                break;
            default:
                throw new NotSupportedException($"Test location files can't hold a {value.GetType().Name}.");
        }
    }

    private static void WriteControl(Stream stream, int type, int size)
    {
        if (size >= 29)
        {
            throw new NotSupportedException("Test location files only hold short values.");
        }

        if (type <= 7)
        {
            stream.WriteByte((byte)((type << 5) | size));
        }
        else
        {
            stream.WriteByte((byte)size);
            stream.WriteByte((byte)(type - 7));
        }
    }
}
