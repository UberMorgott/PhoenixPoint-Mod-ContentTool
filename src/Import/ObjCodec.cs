using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Morgott.ContentTool.Import
{
    internal readonly struct ObjVector2
    {
        internal ObjVector2(float x, float y) { X = x; Y = y; }
        internal float X { get; }
        internal float Y { get; }
    }

    internal readonly struct ObjVector3
    {
        internal ObjVector3(float x, float y, float z) { X = x; Y = y; Z = z; }
        internal float X { get; }
        internal float Y { get; }
        internal float Z { get; }
    }

    internal readonly struct ObjVertex
    {
        internal ObjVertex(int position, int texture = -1, int normal = -1)
        {
            Position = position;
            Texture = texture;
            Normal = normal;
        }

        internal int Position { get; }
        internal int Texture { get; }
        internal int Normal { get; }
    }

    internal readonly struct ObjTriangle
    {
        internal ObjTriangle(ObjVertex a, ObjVertex b, ObjVertex c, int group)
        {
            A = a;
            B = b;
            C = c;
            Group = group;
        }

        internal ObjVertex A { get; }
        internal ObjVertex B { get; }
        internal ObjVertex C { get; }
        internal int Group { get; }
    }

    internal sealed class ObjDocument
    {
        internal List<ObjVector3> Positions { get; } = new List<ObjVector3>();
        internal List<ObjVector2> TextureCoordinates { get; } = new List<ObjVector2>();
        internal List<ObjVector3> Normals { get; } = new List<ObjVector3>();
        internal List<ObjTriangle> Triangles { get; } = new List<ObjTriangle>();
    }

    /// <summary>
    /// FINAL-PLAN 12.1: the OBJ importer, ported from ResourceReplacer
    /// (<c>pp-native\src\ObjCodec.cs</c>) unchanged apart from the namespace. Deliberately free of
    /// UnityEngine types so it runs offline under tests\ObjCodecTests, the same shape as TargetPath.
    /// </summary>
    internal static class ObjCodec
    {
        // A .obj arrives from a folder a player can drop any file into, so it carries its own
        // ceilings rather than letting a hostile file grow a List<> until the game dies.
        private const int MaxTextLength = 64 * 1024 * 1024;   // chars: 128 MB once each is a UTF-16 char
        private const int MaxVertices = 1000000;
        private const int MaxTriangles = 6000000 / 3;

        internal static ObjDocument Parse(string text)
        {
            if (text == null)
                throw new FormatException("Line 0: OBJ text is null; provide geometry with faces.");
            if (text.Length > MaxTextLength)
                throw new FormatException("Line 0: the file is " + text.Length.ToString(CultureInfo.InvariantCulture) +
                    " characters, past the " + MaxTextLength.ToString(CultureInfo.InvariantCulture) +
                    " limit this mod will read; decimate the mesh and export it again.");

            ObjDocument document = new ObjDocument();
            var groups = new Dictionary<string, int>(StringComparer.Ordinal);
            int group = 0;
            int nextGroup = 0;
            int lineNumber = 0;
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    int comment = line.IndexOf('#');
                    if (comment >= 0)
                        line = line.Substring(0, comment);
                    string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0)
                        continue;

                    try
                    {
                        switch (parts[0])
                        {
                            case "v":
                                RequireCount(parts, 4, "vertex requires three finite values");
                                document.Positions.Add(new ObjVector3(Number(parts[1]), Number(parts[2]), Number(parts[3])));
                                Cap(document.Positions.Count, MaxVertices, "vertices");
                                break;
                            case "vt":
                                RequireCount(parts, 3, "texture coordinate requires two finite values");
                                document.TextureCoordinates.Add(new ObjVector2(Number(parts[1]), Number(parts[2])));
                                Cap(document.TextureCoordinates.Count, MaxVertices, "texture coordinates");
                                break;
                            case "vn":
                                RequireCount(parts, 4, "normal requires three finite values");
                                document.Normals.Add(new ObjVector3(Number(parts[1]), Number(parts[2]), Number(parts[3])));
                                Cap(document.Normals.Count, MaxVertices, "normals");
                                break;
                            case "f":
                                if (parts.Length < 4)
                                    throw new FormatException("face requires at least three vertices");
                                if (groups.Count == 0 && nextGroup == 0)
                                    nextGroup = 1; // Reserve group zero for faces before the first named group.
                                var vertices = new ObjVertex[parts.Length - 1];
                                for (int i = 1; i < parts.Length; i++)
                                    vertices[i - 1] = Vertex(parts[i], document);
                                for (int i = 1; i < vertices.Length - 1; i++)
                                    document.Triangles.Add(new ObjTriangle(vertices[0], vertices[i], vertices[i + 1], group));
                                Cap(document.Triangles.Count, MaxTriangles, "triangles");
                                break;
                            case "g":
                            case "o":
                            case "usemtl":
                                if (parts.Length < 2)
                                    throw new FormatException(parts[0] + " requires a group name");
                                string key = parts[0] + " " + string.Join(" ", parts, 1, parts.Length - 1);
                                if (!groups.TryGetValue(key, out group))
                                {
                                    group = nextGroup++;
                                    groups.Add(key, group);
                                }
                                break;
                        }
                    }
                    catch (FormatException exception)
                    {
                        throw new FormatException("Line " + lineNumber.ToString(CultureInfo.InvariantCulture) + ": " + exception.Message, exception);
                    }
                }
            }

            if (document.Triangles.Count == 0)
                throw new FormatException("Line " + lineNumber.ToString(CultureInfo.InvariantCulture) + ": no faces; add at least one valid face.");
            return document;
        }

        internal static string Write(ObjDocument document)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            var output = new StringBuilder();
            foreach (ObjVector3 value in document.Positions)
                output.Append("v ").Append(Format(value.X)).Append(' ').Append(Format(value.Y)).Append(' ').Append(Format(value.Z)).Append('\n');
            foreach (ObjVector2 value in document.TextureCoordinates)
                output.Append("vt ").Append(Format(value.X)).Append(' ').Append(Format(value.Y)).Append('\n');
            foreach (ObjVector3 value in document.Normals)
                output.Append("vn ").Append(Format(value.X)).Append(' ').Append(Format(value.Y)).Append(' ').Append(Format(value.Z)).Append('\n');

            int group = int.MinValue;
            foreach (ObjTriangle triangle in document.Triangles)
            {
                if (triangle.Group != group)
                {
                    group = triangle.Group;
                    output.Append("g submesh_").Append(group.ToString(CultureInfo.InvariantCulture)).Append('\n');
                }
                output.Append("f ").Append(WriteVertex(triangle.A)).Append(' ').Append(WriteVertex(triangle.B)).Append(' ').Append(WriteVertex(triangle.C)).Append('\n');
            }
            return output.ToString();
        }

        internal static string Checksum(ObjDocument document)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(document.Positions.Count);
                foreach (ObjVector3 value in document.Positions) { writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z); }
                writer.Write(document.TextureCoordinates.Count);
                foreach (ObjVector2 value in document.TextureCoordinates) { writer.Write(value.X); writer.Write(value.Y); }
                writer.Write(document.Normals.Count);
                foreach (ObjVector3 value in document.Normals) { writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z); }
                writer.Write(document.Triangles.Count);
                foreach (ObjTriangle triangle in document.Triangles)
                {
                    WriteVertexBytes(writer, triangle.A);
                    WriteVertexBytes(writer, triangle.B);
                    WriteVertexBytes(writer, triangle.C);
                    writer.Write(triangle.Group);
                }
                writer.Flush();
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(stream.ToArray());
                    var result = new StringBuilder(16);
                    for (int i = 0; i < 8; i++)
                        result.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                    return result.ToString();
                }
            }
        }

        private static ObjVertex Vertex(string token, ObjDocument document)
        {
            string[] indices = token.Split('/');
            if (indices.Length < 1 || indices.Length > 3 || indices[0].Length == 0 || (indices.Length == 2 && indices[1].Length == 0))
                throw new FormatException("malformed face vertex '" + token + "'; use v, v/vt, v//vn, or v/vt/vn");
            int position = Index(indices[0], document.Positions.Count, "position");
            int texture = indices.Length > 1 && indices[1].Length > 0 ? Index(indices[1], document.TextureCoordinates.Count, "texture") : -1;
            int normal = indices.Length > 2 && indices[2].Length > 0 ? Index(indices[2], document.Normals.Count, "normal") : -1;
            if (indices.Length == 3 && indices[1].Length == 0 && indices[2].Length == 0)
                throw new FormatException("malformed face vertex '" + token + "'; missing normal index");
            return new ObjVertex(position, texture, normal);
        }

        private static int Index(string text, int count, string kind)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value == 0)
                throw new FormatException(kind + " index '" + text + "' is malformed; use a non-zero integer");
            int resolved = value > 0 ? value - 1 : count + value;
            if (resolved < 0 || resolved >= count)
                throw new FormatException(kind + " index '" + text + "' is out of range; define the referenced value first");
            return resolved;
        }

        private static float Number(string text)
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || float.IsNaN(value) || float.IsInfinity(value))
                throw new FormatException("value '" + text + "' must be finite");
            return value;
        }

        private static void Cap(int count, int limit, string what)
        {
            if (count > limit)
                throw new FormatException("the file holds more than " + limit.ToString(CultureInfo.InvariantCulture) + " " + what +
                    ", past the limit this mod will read; decimate the mesh and export it again");
        }

        private static void RequireCount(string[] parts, int count, string cause)
        {
            if (parts.Length != count)
                throw new FormatException(cause);
        }

        private static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static string WriteVertex(ObjVertex vertex)
        {
            string position = (vertex.Position + 1).ToString(CultureInfo.InvariantCulture);
            if (vertex.Texture < 0 && vertex.Normal < 0)
                return position;
            string texture = vertex.Texture < 0 ? "" : (vertex.Texture + 1).ToString(CultureInfo.InvariantCulture);
            if (vertex.Normal < 0)
                return position + "/" + texture;
            return position + "/" + texture + "/" + (vertex.Normal + 1).ToString(CultureInfo.InvariantCulture);
        }

        private static void WriteVertexBytes(BinaryWriter writer, ObjVertex vertex)
        {
            writer.Write(vertex.Position);
            writer.Write(vertex.Texture);
            writer.Write(vertex.Normal);
        }
    }
}
