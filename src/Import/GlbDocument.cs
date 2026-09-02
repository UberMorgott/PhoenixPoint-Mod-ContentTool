using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Morgott.ContentTool.Import
{
    /// <summary>
    /// Lossless GLB container: reads and rewrites a .glb without interpreting its glTF content,
    /// so it can handle files GlbReader refuses (Draco, meshopt, unknown extensions). An unmutated
    /// document writes its ORIGINAL JSON chunk bytes verbatim - byte equality, not semantic equality.
    /// Re-serialization fires only when Dirty is set.
    /// </summary>
    internal sealed class GlbDocument
    {
        private const uint Magic = 0x46546C67;      // 'glTF'
        private const uint JsonType = 0x4E4F534A;   // 'JSON'
        private const uint BinType = 0x004E4942;    // 'BIN\0'

        // --- stored state ---
        internal uint Version { get; }
        // ponytail: key order is Dictionary insertion order, which holds because nothing is ever
        // removed from these objects - only reassigned. Upgrade to OrderedDictionary the day a
        // mutation has to DELETE a key.
        /// <summary>The parsed JSON chunk (Dictionary from Json.Parse). Mutating it is only written
        /// out once Dirty is set, so whoever mutates it says so.</summary>
        internal Dictionary<string, object> Json { get; }
        /// <summary>The raw JSON chunk bytes as read from disk. Used for verbatim write when !Dirty.</summary>
        private readonly byte[] originalJsonBytes;
        /// <summary>Mutable BIN chunk. Replaced wholesale by Trim. Null when the file carries none.</summary>
        internal byte[] Bin { get; set; }
        /// <summary>Trailing chunks after BIN, kept verbatim (type + data pairs).</summary>
        private readonly List<(uint type, byte[] data)> trailing;
        internal bool Dirty { get; set; }

        /// <summary>Read a .glb from bytes.</summary>
        internal static GlbDocument Load(byte[] bytes) => new GlbDocument(bytes);

        /// <summary>Read a .glb from a file path.</summary>
        internal static GlbDocument Load(string path) => new GlbDocument(File.ReadAllBytes(path));

        private GlbDocument(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12)
                throw new FormatException("this is not a .glb: a file has to carry at least the 12-byte header");
            if (U32(bytes, 0) != Magic)
                throw new FormatException("this is not a .glb: the file does not start with 'glTF'");
            Version = U32(bytes, 4);
            if (Version != 2)
                throw new FormatException("this .glb declares version " + Version + "; only version 2 is a glTF binary");
            uint total = U32(bytes, 8);
            if (total != bytes.Length)
                throw new FormatException("this .glb is truncated or padded: the header declares " + total +
                                          " bytes and the file holds " + bytes.Length);

            trailing = new List<(uint, byte[])>();
            int at = 12;
            while (at < bytes.Length)
            {
                if (at + 8 > bytes.Length)
                    throw new FormatException("this .glb is truncated: a chunk header at " + at + " is cut short");
                uint length = U32(bytes, at);
                uint type = U32(bytes, at + 4);
                at += 8;
                if (length > bytes.Length - at)
                    throw new FormatException("this .glb is truncated: the chunk at " + (at - 8) + " declares " +
                                              length + " bytes and only " + (bytes.Length - at) + " remain");
                var data = new byte[length];
                Buffer.BlockCopy(bytes, at, data, 0, (int)length);
                at += (int)length;

                if (originalJsonBytes == null)
                {
                    if (type != JsonType)
                        throw new FormatException("this .glb does not start with its JSON chunk; the first chunk " +
                                                  "is type 0x" + type.ToString("x8") + " where 'JSON' was expected");
                    originalJsonBytes = data;
                }
                else if (Bin == null && type == BinType) Bin = data;
                else trailing.Add((type, data));
            }
            if (originalJsonBytes == null)
                throw new FormatException("this .glb carries no JSON chunk at all");

            Json = Parsed(originalJsonBytes);
        }

        /// <summary>Write the document to bytes. Uses originalJsonBytes when !Dirty.</summary>
        internal byte[] Write()
        {
            byte[] json = Pad(Dirty ? Encoding.UTF8.GetBytes(new JsonWriter().Val(Json).ToString())
                                    : originalJsonBytes, 0x20);
            byte[] bin = Bin == null ? null : Pad(Bin, 0x00);

            long total = 12 + 8 + (long)json.Length;
            if (bin != null) total += 8 + bin.Length;
            foreach ((uint type, byte[] data) chunk in trailing) total += 8 + chunk.data.Length;
            if (total > uint.MaxValue)
                throw new InvalidOperationException("this .glb would be " + total + " bytes; the container's " +
                                                    "length field only reaches " + uint.MaxValue);

            var bytes = new byte[total];
            int at = 0;
            Put(bytes, ref at, Magic);
            Put(bytes, ref at, Version);
            Put(bytes, ref at, (uint)total);
            Chunk(bytes, ref at, JsonType, json);
            if (bin != null) Chunk(bytes, ref at, BinType, bin);
            foreach ((uint type, byte[] data) chunk in trailing) Chunk(bytes, ref at, chunk.type, chunk.data);
            return bytes;
        }

        /// <summary>Write to a file path.</summary>
        internal void Write(string path) => File.WriteAllBytes(path, Write());

        private static Dictionary<string, object> Parsed(byte[] jsonBytes)
        {
            // The class and the Json property share a name, so the namespace has to be spelled out.
            object value = Morgott.ContentTool.Import.Json.Parse(Encoding.UTF8.GetString(jsonBytes), 128);
            if (!(value is Dictionary<string, object> root))
                throw new FormatException("this .glb's JSON chunk is not an object, so it describes no glTF");
            return root;
        }

        /// <summary>The same array when it is already 4-byte aligned, a padded copy otherwise.</summary>
        private static byte[] Pad(byte[] data, byte filler)
        {
            int slack = (4 - data.Length % 4) % 4;
            if (slack == 0) return data;
            var padded = new byte[data.Length + slack];
            Buffer.BlockCopy(data, 0, padded, 0, data.Length);
            for (int i = data.Length; i < padded.Length; i++) padded[i] = filler;
            return padded;
        }

        private static void Chunk(byte[] bytes, ref int at, uint type, byte[] data)
        {
            Put(bytes, ref at, (uint)data.Length);
            Put(bytes, ref at, type);
            Buffer.BlockCopy(data, 0, bytes, at, data.Length);
            at += data.Length;
        }

        private static void Put(byte[] bytes, ref int at, uint value)
        {
            bytes[at++] = (byte)value;
            bytes[at++] = (byte)(value >> 8);
            bytes[at++] = (byte)(value >> 16);
            bytes[at++] = (byte)(value >> 24);
        }

        private static uint U32(byte[] bytes, int at) =>
            (uint)(bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24));
    }
}
