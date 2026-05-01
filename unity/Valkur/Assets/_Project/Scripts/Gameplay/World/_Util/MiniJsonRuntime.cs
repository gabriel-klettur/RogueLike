using System.Collections.Generic;
using System.IO;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Minimal runtime JSON parser (MiniJSON variant for non-editor code).
    /// Needed because the editor MiniJson is in the Valkur.Editor assembly.
    /// Used by: OverlayLoader, WorldLoader, BuildingLoader, BuildingCollisionLoader,
    /// ZoneDatabaseLoader, ParticleInstancesLoader, SpawnerInstanceLoader.
    /// </summary>
    public static class MiniJsonRuntime
    {
        public static object Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            return Parser.Parse(json);
        }

        // ── Serializer ────────────────────────────────────────────────────────
        // Mirrors Editor/MiniJson.Serialize; needed at runtime by FSM editor
        // (and anything else that round-trips JSON outside the Editor assembly).

        public static string Serialize(object obj, bool pretty = false)
        {
            if (obj == null) return "null";
            var sb = new System.Text.StringBuilder();
            SerializeValue(obj, sb, pretty, 0);
            return sb.ToString();
        }

        private static void SerializeValue(object obj, System.Text.StringBuilder sb, bool pretty, int depth)
        {
            if (obj == null)                          { sb.Append("null"); return; }
            if (obj is string s)                      { SerializeString(s, sb); return; }
            if (obj is bool b)                        { sb.Append(b ? "true" : "false"); return; }
            if (obj is System.Collections.IDictionary dict)
            {
                SerializeDict(dict, sb, pretty, depth);
                return;
            }
            if (obj is System.Collections.IEnumerable list && !(obj is string))
            {
                SerializeList(list, sb, pretty, depth);
                return;
            }
            if (obj is float f)
                sb.Append(f.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            else if (obj is double dv)
                sb.Append(dv.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            else
                sb.Append(System.Convert.ToString(obj, System.Globalization.CultureInfo.InvariantCulture));
        }

        private static void SerializeString(string s, System.Text.StringBuilder sb)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private static void Indent(System.Text.StringBuilder sb, bool pretty, int depth)
        {
            if (!pretty) return;
            sb.Append('\n');
            for (int i = 0; i < depth; i++) sb.Append("  ");
        }

        private static void SerializeDict(System.Collections.IDictionary dict, System.Text.StringBuilder sb, bool pretty, int depth)
        {
            sb.Append('{');
            bool first = true;
            // Sort keys (parity with Python json.dump sort_keys=True)
            var keys = new List<string>();
            foreach (var k in dict.Keys) keys.Add(System.Convert.ToString(k));
            keys.Sort(System.StringComparer.Ordinal);
            foreach (var key in keys)
            {
                if (!first) sb.Append(',');
                first = false;
                Indent(sb, pretty, depth + 1);
                SerializeString(key, sb);
                sb.Append(pretty ? ": " : ":");
                SerializeValue(dict[key], sb, pretty, depth + 1);
            }
            if (!first) Indent(sb, pretty, depth);
            sb.Append('}');
        }

        private static void SerializeList(System.Collections.IEnumerable list, System.Text.StringBuilder sb, bool pretty, int depth)
        {
            sb.Append('[');
            bool first = true;
            foreach (var item in list)
            {
                if (!first) sb.Append(',');
                first = false;
                Indent(sb, pretty, depth + 1);
                SerializeValue(item, sb, pretty, depth + 1);
            }
            if (!first) Indent(sb, pretty, depth);
            sb.Append(']');
        }

        private sealed class Parser : System.IDisposable
        {
            private const string WORD_BREAK = "{}[],:\"";
            private StringReader _json;

            private Parser(string jsonString) { _json = new StringReader(jsonString); }

            public static object Parse(string jsonString)
            {
                using var instance = new Parser(jsonString);
                return instance.ParseValue();
            }

            public void Dispose() { _json?.Dispose(); _json = null; }

            private char PeekChar
            {
                get
                {
                    int c = _json.Peek();
                    return c == -1 ? '\0' : (char)c;
                }
            }

            private char NextChar => (char)_json.Read();

            private string NextWord
            {
                get
                {
                    var sb = new System.Text.StringBuilder();
                    while (!IsWordBreak(PeekChar) && PeekChar != '\0')
                        sb.Append(NextChar);
                    return sb.ToString();
                }
            }

            private static bool IsWordBreak(char c) =>
                char.IsWhiteSpace(c) || WORD_BREAK.IndexOf(c) != -1;

            private object ParseValue()
            {
                EatWhitespace();
                switch (PeekChar)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    default: return ParseByToken(NextWord);
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                var dict = new Dictionary<string, object>();
                _json.Read(); // skip '{'
                while (true)
                {
                    EatWhitespace();
                    if (PeekChar == '}') { _json.Read(); return dict; }
                    if (PeekChar == ',') { _json.Read(); continue; }
                    string key = ParseString();
                    EatWhitespace();
                    if (PeekChar == ':') _json.Read();
                    dict[key] = ParseValue();
                }
            }

            private List<object> ParseArray()
            {
                var list = new List<object>();
                _json.Read(); // skip '['
                while (true)
                {
                    EatWhitespace();
                    if (PeekChar == ']') { _json.Read(); return list; }
                    if (PeekChar == ',') { _json.Read(); continue; }
                    list.Add(ParseValue());
                }
            }

            private string ParseString()
            {
                var sb = new System.Text.StringBuilder();
                _json.Read(); // skip opening quote
                while (true)
                {
                    char c = NextChar;
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        char esc = NextChar;
                        switch (esc)
                        {
                            case '"': case '\\': case '/': sb.Append(esc); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                var hex = new char[4];
                                for (int i = 0; i < 4; i++) hex[i] = NextChar;
                                sb.Append((char)System.Convert.ToInt32(new string(hex), 16));
                                break;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }

            private static object ParseByToken(string token)
            {
                if (token == "null") return null;
                if (token == "true") return true;
                if (token == "false") return false;
                if (long.TryParse(token, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out long l))
                    return l;
                if (double.TryParse(token, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double d))
                    return d;
                return token;
            }

            private void EatWhitespace()
            {
                while (char.IsWhiteSpace(PeekChar)) _json.Read();
            }
        }
    }
}
