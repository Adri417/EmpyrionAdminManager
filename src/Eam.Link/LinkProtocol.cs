// Shared between the EAM engine (.NET 8) and the EAM game mod (.NET Framework 4.8 / Unity Mono).
// Keep this file free of anything newer than C# 9 / .NET Framework 4.8 APIs.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Eam.Link
{
    /// <summary>
    /// EAM link: local TCP (127.0.0.1 only) between the engine and the mod running inside the game.
    /// 1. mod  → engine: "EAM-LINK &lt;version&gt; &lt;nonce&gt;"
    /// 2. engine → mod: "AUTH &lt;hex HMAC-SHA256(key, nonce)&gt;"
    /// 3. mod  → engine: "OK" (or closes the connection)
    /// Then one JSON object per line in both directions.
    /// </summary>
    public static class LinkProtocol
    {
        public const int Version = 1;
        public const string Greeting = "EAM-LINK";
        public const int MaxLineLength = 1024 * 1024;

        public static string NewNonce()
        {
            var b = new byte[24];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return ToHex(b);
        }

        public static string Sign(string key, string nonce)
        {
            using (var h = new HMACSHA256(Encoding.UTF8.GetBytes(key ?? "")))
                return ToHex(h.ComputeHash(Encoding.UTF8.GetBytes(nonce ?? "")));
        }

        /// <summary>Constant-time comparison of two signatures.</summary>
        public static bool SameSignature(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            var diff = 0;
            for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static string ToHex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (var x in b) sb.Append(x.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }

    /// <summary>
    /// Tiny JSON writer/reader with no dependency, for the mod side
    /// (Unity's Mono has no System.Text.Json). Objects are Dictionary&lt;string, object&gt;,
    /// arrays are List&lt;object&gt;, numbers are double, plus string, bool and null.
    /// </summary>
    public static class MiniJson
    {
        public static string Write(object value)
        {
            var sb = new StringBuilder(256);
            WriteValue(sb, value);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string s) { WriteString(sb, s); return; }
            if (v is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (v is IDictionary<string, object> d)
            {
                sb.Append('{');
                var first = true;
                foreach (var kv in d)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, kv.Key);
                    sb.Append(':');
                    WriteValue(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            if (v is System.Collections.IEnumerable e)
            {
                sb.Append('[');
                var first = true;
                foreach (var x in e)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, x);
                }
                sb.Append(']');
                return;
            }
            if (v is float f) { WriteNumber(sb, f); return; }
            if (v is double db) { WriteNumber(sb, db); return; }
            if (v.GetType().IsPrimitive || v is decimal)
            {
                sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
                return;
            }
            WriteString(sb, v.ToString());
        }

        private static void WriteNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return; }
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < ' ' || ch == (char)0x2028 || ch == (char)0x2029) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
        }

        public static object Parse(string json)
        {
            var i = 0;
            var v = ParseValue(json, ref i, 0);
            SkipWs(json, ref i);
            if (i != json.Length) throw new FormatException("json_trailing");
            return v;
        }

        public static Dictionary<string, object> ParseObject(string json) =>
            Parse(json) as Dictionary<string, object> ?? throw new FormatException("json_not_object");

        private static object ParseValue(string s, ref int i, int depth)
        {
            if (depth > 32) throw new FormatException("json_too_deep");
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("json_eof");
            var c = s[i];
            if (c == '{')
            {
                i++;
                var d = new Dictionary<string, object>();
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipWs(s, ref i);
                    var k = ParseString(s, ref i);
                    SkipWs(s, ref i);
                    Expect(s, ref i, ':');
                    d[k] = ParseValue(s, ref i, depth + 1);
                    SkipWs(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    Expect(s, ref i, '}');
                    return d;
                }
            }
            if (c == '[')
            {
                i++;
                var l = new List<object>();
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(ParseValue(s, ref i, depth + 1));
                    SkipWs(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    Expect(s, ref i, ']');
                    return l;
                }
            }
            if (c == '"') return ParseString(s, ref i);
            if (Match(s, ref i, "true")) return true;
            if (Match(s, ref i, "false")) return false;
            if (Match(s, ref i, "null")) return null;
            var start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (start == i) throw new FormatException("json_unexpected");
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static string ParseString(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                var c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                var e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("json_escape");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("json_unterminated_string");
        }

        private static void SkipWs(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        private static void Expect(string s, ref int i, char c)
        {
            if (i >= s.Length || s[i] != c) throw new FormatException("json_expected_" + c);
            i++;
        }

        private static bool Match(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
            i += word.Length;
            return true;
        }

        // Helpers to read parsed objects safely.
        public static string Str(this Dictionary<string, object> d, string k) =>
            d != null && d.TryGetValue(k, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;

        public static double Num(this Dictionary<string, object> d, string k, double fallback = 0) =>
            d != null && d.TryGetValue(k, out var v) && v is double x ? x : fallback;

        /// <summary>Number if present, otherwise null (for optional fields).</summary>
        public static double? OptNum(this Dictionary<string, object> d, string k) =>
            d != null && d.TryGetValue(k, out var v) && v is double x ? x : (double?)null;

        public static List<object> List(this Dictionary<string, object> d, string k) =>
            d != null && d.TryGetValue(k, out var v) ? v as List<object> : null;
    }
}
