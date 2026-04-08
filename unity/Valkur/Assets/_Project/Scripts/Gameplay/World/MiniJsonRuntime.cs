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
