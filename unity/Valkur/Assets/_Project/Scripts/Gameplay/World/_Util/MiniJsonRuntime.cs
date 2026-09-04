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

            // Set the first time the input stops being JSON. Every container checks it after
            // each child and unwinds, and Parse returns null when it is set — so a caller
            // cannot tell "the file said null" from "the file was garbage" (neither could the
            // original MiniJSON), but it CAN rely on Deserialize returning, which it could not.
            private bool _failed;

            private Parser(string jsonString) { _json = new StringReader(jsonString); }

            public static object Parse(string jsonString)
            {
                using var instance = new Parser(jsonString);
                object result = instance.ParseValue();
                return instance._failed ? null : result;
            }

            public void Dispose() { _json?.Dispose(); _json = null; }

            private bool AtEnd => _json.Peek() == -1;

            private char PeekChar
            {
                get
                {
                    int c = _json.Peek();
                    return c == -1 ? '\0' : (char)c;
                }
            }

            /// <summary>
            /// '\0' at the end of the input. The previous version cast <c>Read()</c>'s -1 to
            /// <c>'\uFFFF'</c>, which is not a quote and not a backslash, so an unterminated
            /// string appended it to a StringBuilder forever — measured at 20 seconds of
            /// wall clock and then OutOfMemory, on the input <c>{ this is not valid json</c>.
            /// Eighteen loaders parse hand-editable files through this class; any one of
            /// them corrupted by a stray keystroke used to freeze the game that way.
            /// </summary>
            private char NextChar
            {
                get
                {
                    int c = _json.Read();
                    return c == -1 ? '\0' : (char)c;
                }
            }

            private string NextWord
            {
                get
                {
                    var sb = new System.Text.StringBuilder();
                    while (!AtEnd && !IsWordBreak(PeekChar))
                        sb.Append(NextChar);
                    return sb.ToString();
                }
            }

            private static bool IsWordBreak(char c) =>
                char.IsWhiteSpace(c) || WORD_BREAK.IndexOf(c) != -1;

            private object Fail()
            {
                _failed = true;
                return null;
            }

            private object ParseValue()
            {
                EatWhitespace();
                if (AtEnd) return Fail();
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
                    if (AtEnd) { Fail(); return null; }           // unterminated object
                    if (PeekChar == '}') { _json.Read(); return dict; }
                    if (PeekChar == ',') { _json.Read(); continue; }

                    string key = ParseString();
                    if (key == null) { Fail(); return null; }     // key was not a string

                    EatWhitespace();
                    if (PeekChar != ':') { Fail(); return null; }
                    _json.Read();

                    object value = ParseValue();
                    if (_failed) return null;
                    dict[key] = value;
                }
            }

            private List<object> ParseArray()
            {
                var list = new List<object>();
                _json.Read(); // skip '['
                while (true)
                {
                    EatWhitespace();
                    if (AtEnd) { Fail(); return null; }           // unterminated array
                    if (PeekChar == ']') { _json.Read(); return list; }
                    if (PeekChar == ',') { _json.Read(); continue; }

                    object value = ParseValue();
                    if (_failed) return null;
                    list.Add(value);
                }
            }

            /// <summary>
            /// Null when the next thing is not a string — the caller decides whether that is
            /// a failure (an object key) or a real null value would have gone through
            /// <see cref="ParseByToken"/> instead.
            /// </summary>
            private string ParseString()
            {
                if (PeekChar != '"') return null;
                var sb = new System.Text.StringBuilder();
                _json.Read(); // skip opening quote
                while (true)
                {
                    if (AtEnd) { Fail(); return null; }           // unterminated string
                    char c = NextChar;
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        if (AtEnd) { Fail(); return null; }
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
                                for (int i = 0; i < 4; i++)
                                {
                                    if (AtEnd) { Fail(); return null; }
                                    hex[i] = NextChar;
                                }
                                if (!int.TryParse(new string(hex), System.Globalization.NumberStyles.HexNumber,
                                        System.Globalization.CultureInfo.InvariantCulture, out int code))
                                { Fail(); return null; }
                                sb.Append((char)code);
                                break;
                            default:
                                // An escape this parser does not know. The old code silently
                                // dropped the character; keep that leniency rather than fail a
                                // file that has loaded for months.
                                break;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }

            private object ParseByToken(string token)
            {
                // An empty word means the next character was a structural one in a place
                // that needed a value (`{"a":}`, `[,]`, `:` at top level). Returning it as a
                // string, as the old code did, is what let the enclosing loop spin without
                // consuming anything.
                if (token.Length == 0) return Fail();
                if (token == "null") return null;
                if (token == "true") return true;
                if (token == "false") return false;
                if (long.TryParse(token, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out long l))
                    return l;
                if (double.TryParse(token, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double d))
                    return d;
                return token;   // bare word: kept as a string, as before
            }

            private void EatWhitespace()
            {
                while (!AtEnd && char.IsWhiteSpace(PeekChar)) _json.Read();
            }
        }
    }
}
