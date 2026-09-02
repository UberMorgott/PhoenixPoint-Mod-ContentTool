using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Morgott.ContentTool.Import
{
    /// <summary>
    /// Just enough JSON to read a glTF chunk, the mirror of <see cref="JsonWriter"/>. Total: every
    /// malformed input leaves as a <see cref="FormatException"/> naming the offset, and the nesting
    /// depth is capped so a file made of ten thousand open brackets cannot overflow the stack.
    /// </summary>
    internal static class Json
    {
        internal static object Parse(string text, int maxDepth)
        {
            if (text == null) throw Fail(0, "there is nothing to read");
            int at = 0;
            object value = Read(text, ref at, 0, maxDepth);
            Space(text, ref at);
            if (at != text.Length) throw Fail(at, "there is leftover text after the end of the description");
            return value;
        }

        private static object Read(string text, ref int at, int depth, int maxDepth)
        {
            if (depth > maxDepth) throw Fail(at, "the description nests deeper than " + maxDepth + " levels");
            Space(text, ref at);
            if (at >= text.Length) throw Fail(at, "the description ends early");
            char c = text[at];
            if (c == '{')
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                at++;
                Space(text, ref at);
                if (Peek(text, at) == '}') { at++; return result; }
                while (true)
                {
                    Space(text, ref at);
                    if (Peek(text, at) != '"') throw Fail(at, "a name was expected");
                    string key = Text(text, ref at);
                    Space(text, ref at);
                    if (Peek(text, at) != ':') throw Fail(at, "a ':' was expected");
                    at++;
                    result[key] = Read(text, ref at, depth + 1, maxDepth);
                    Space(text, ref at);
                    char next = Peek(text, at);
                    at++;
                    if (next == ',') continue;
                    if (next == '}') return result;
                    throw Fail(at - 1, "a ',' or '}' was expected");
                }
            }
            if (c == '[')
            {
                var result = new List<object>();
                at++;
                Space(text, ref at);
                if (Peek(text, at) == ']') { at++; return result; }
                while (true)
                {
                    result.Add(Read(text, ref at, depth + 1, maxDepth));
                    Space(text, ref at);
                    char next = Peek(text, at);
                    at++;
                    if (next == ',') continue;
                    if (next == ']') return result;
                    throw Fail(at - 1, "a ',' or ']' was expected");
                }
            }
            if (c == '"') return Text(text, ref at);
            if (Literal(text, ref at, "true")) return true;
            if (Literal(text, ref at, "false")) return false;
            if (Literal(text, ref at, "null")) return null;
            return Number(text, ref at);
        }

        private static char Peek(string text, int at) => at < text.Length ? text[at] : '\0';

        private static void Space(string text, ref int at)
        {
            while (at < text.Length && (text[at] == ' ' || text[at] == '\t' || text[at] == '\n' || text[at] == '\r')) at++;
        }

        private static bool Literal(string text, ref int at, string word)
        {
            if (at + word.Length > text.Length || string.CompareOrdinal(text, at, word, 0, word.Length) != 0) return false;
            at += word.Length;
            return true;
        }

        private static object Number(string text, ref int at)
        {
            int start = at;
            if (Peek(text, at) == '-') at++;
            while (at < text.Length && ((text[at] >= '0' && text[at] <= '9') || text[at] == '.' ||
                   text[at] == 'e' || text[at] == 'E' || text[at] == '+' || text[at] == '-')) at++;
            if (at == start) throw Fail(start, "a value was expected");
            if (!double.TryParse(text.Substring(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                double.IsNaN(value) || double.IsInfinity(value))
                throw Fail(start, "'" + text.Substring(start, at - start) + "' is not a finite number");
            return value;
        }

        private static string Text(string text, ref int at)
        {
            at++;                                   // the opening quote, already peeked
            var result = new StringBuilder();
            while (true)
            {
                if (at >= text.Length) throw Fail(at, "a piece of text is never closed");
                char c = text[at++];
                if (c == '"') return result.ToString();
                if (c != '\\') { result.Append(c); continue; }
                if (at >= text.Length) throw Fail(at, "a piece of text ends in an escape");
                char escape = text[at++];
                switch (escape)
                {
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case '/': result.Append('/'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u':
                        if (at + 4 > text.Length) throw Fail(at, "an escape ends early");
                        if (!int.TryParse(text.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            throw Fail(at, "'" + text.Substring(at, 4) + "' is not a character code");
                        result.Append((char)code);
                        at += 4;
                        break;
                    default: throw Fail(at - 1, "'\\" + escape + "' is not an escape JSON knows");
                }
            }
        }

        /// <summary>A JSON-chunk refusal, which any real file with a truncated or corrupt description
        /// reaches - so it carries a CODE like every other Read-path refusal, or the Doctor would have
        /// nothing to name for sixteen throw sites. The sentence is unchanged.</summary>
        private static FormatException Fail(int at, string cause) =>
            new ImportRefusedException(ImportCode.MalformedGlb,
                "the file's description is malformed at character " +
                at.ToString(CultureInfo.InvariantCulture) + ": " + cause + "; re-export it rather than editing it by hand");
    }

    /// <summary>
    /// Just enough JSON to emit a glTF chunk and a canonical sidecar. Key order is whatever the
    /// caller writes, so callers write them in a fixed order and the output is deterministic.
    /// </summary>
    internal sealed class JsonWriter
    {
        private readonly StringBuilder text = new StringBuilder();
        private bool separate;

        internal JsonWriter Obj() { Comma(); text.Append('{'); return this; }
        internal JsonWriter EndObj() { text.Append('}'); separate = true; return this; }
        internal JsonWriter Arr() { Comma(); text.Append('['); return this; }
        internal JsonWriter EndArr() { text.Append(']'); separate = true; return this; }
        internal JsonWriter Key(string name) { Comma(); Quote(name); text.Append(':'); return this; }
        internal JsonWriter Val(string value) { Comma(); Quote(value); separate = true; return this; }
        internal JsonWriter Val(int value) { Comma(); text.Append(value.ToString(CultureInfo.InvariantCulture)); separate = true; return this; }
        internal JsonWriter Val(float value) { Comma(); text.Append(value.ToString("R", CultureInfo.InvariantCulture)); separate = true; return this; }
        internal JsonWriter Val(bool value) { Comma(); text.Append(value ? "true" : "false"); separate = true; return this; }
        /// <summary>An explicit absent value. Only the sidecar uses it; glTF has no null anywhere.</summary>
        internal JsonWriter Null() { Comma(); text.Append("null"); separate = true; return this; }

        /// <summary>Write a parsed JSON value: string, double, bool, null, or a collection
        /// (Dictionary/List from Json.Parse). Integral doubles written as integers; fractional
        /// doubles use G17 for exact round-trip on .NET Framework (R does not round-trip).</summary>
        internal JsonWriter Val(object value)
        {
            if (value == null) return Null();
            if (value is string word) return Val(word);
            if (value is bool flag) return Val(flag);
            if (value is double number) return Num(number);
            if (value is Dictionary<string, object> members)
            {
                Obj();
                foreach (KeyValuePair<string, object> member in members) { Key(member.Key); Val(member.Value); }
                return EndObj();
            }
            if (value is List<object> items)
            {
                Arr();
                foreach (object item in items) Val(item);
                return EndArr();
            }
            throw new ArgumentException("a " + value.GetType().Name + " is not a JSON value");
        }

        /// <summary>A double that may be integral. Integral -> no decimal point; else the shortest
        /// spelling of 15, 16 or 17 significant digits that re-parses to the same value.</summary>
        internal JsonWriter Num(double value)
        {
            Comma();
            // The long cast is the whole point of the integral arm, so it may only be taken where a
            // long can hold the value - past that G17 writes the exponent form rather than a wrap.
            bool integral = value == Math.Floor(value) && !double.IsInfinity(value) && Math.Abs(value) < 9.2e18;
            if (integral) text.Append(((long)value).ToString(CultureInfo.InvariantCulture));
            else
            {
                // Shortest-round-trip on .NET Framework, which has no "R" that round-trips and no
                // shortest-form default: try 15, 16, then 17 significant digits and take the first
                // spelling that re-parses to the same double. G16 is not a nicety - it is the width a
                // great many doubles actually need, and skipping it spells every one of them with a
                // redundant 17th digit. That is what stops a rewritten file from being byte-identical
                // to the one it came from, since every other producer already writes the shortest form.
                string brief = null;
                for (int digits = 15; digits <= 17; digits++)
                {
                    brief = value.ToString("G" + digits, CultureInfo.InvariantCulture);
                    if (double.Parse(brief, CultureInfo.InvariantCulture) == value) break;
                }
                text.Append(brief);
            }
            separate = true;
            return this;
        }

        internal JsonWriter Vals(float[] values)
        {
            Arr();
            foreach (float value in values) Val(value);
            return EndArr();
        }

        public override string ToString() => text.ToString();

        private void Comma()
        {
            if (separate) text.Append(',');
            separate = false;
        }

        private void Quote(string value)
        {
            text.Append('"');
            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\b': text.Append("\\b"); break;
                    case '\f': text.Append("\\f"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (c < ' ') text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else text.Append(c);
                        break;
                }
            }
            text.Append('"');
        }
    }
}
