using System;
using System.Collections.Generic;

namespace Valkur.Editor
{
    /// <summary>
    /// Minimal JSON parser that handles nested dicts and arrays.
    /// Unity's JsonUtility doesn't support Dictionary; this provides basic support.
    /// </summary>
    internal static class MiniJson
    {
        public static object Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int index = 0;
            return ParseValue(json, ref index);
        }

        public static string Serialize(object obj)
        {
            if (obj == null) return "null";
            var sb = new System.Text.StringBuilder();
            SerializeValue(obj, sb);
            return sb.ToString();
        }

        private static void SerializeValue(object obj, System.Text.StringBuilder sb)
        {
            if (obj == null)                          { sb.Append("null"); return; }
            if (obj is string s)                      { SerializeString(s, sb); return; }
            if (obj is bool b)                        { sb.Append(b ? "true" : "false"); return; }
            if (obj is IDictionary<string, object> d) { SerializeDict(d, sb); return; }
            if (obj is IList<object> list)            { SerializeList(list, sb); return; }
            // numeric
            sb.Append(Convert.ToString(obj, System.Globalization.CultureInfo.InvariantCulture));
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

        private static void SerializeDict(IDictionary<string, object> dict, System.Text.StringBuilder sb)
        {
            sb.Append('{');
            bool first = true;
            foreach (var kv in dict)
            {
                if (!first) sb.Append(',');
                first = false;
                SerializeString(kv.Key, sb);
                sb.Append(':');
                SerializeValue(kv.Value, sb);
            }
            sb.Append('}');
        }

        private static void SerializeList(IList<object> list, System.Text.StringBuilder sb)
        {
            sb.Append('[');
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                SerializeValue(list[i], sb);
            }
            sb.Append(']');
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
        }

        private static object ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) return null;

            switch (json[index])
            {
                case '{': return ParseObject(json, ref index);
                case '[': return ParseArray(json, ref index);
                case '"': return ParseString(json, ref index);
                case 't': case 'f': return ParseBool(json, ref index);
                case 'n': return ParseNull(json, ref index);
                default:  return ParseNumber(json, ref index);
            }
        }

        private static Dictionary<string, object> ParseObject(string json, ref int index)
        {
            var dict = new Dictionary<string, object>();
            index++; // skip {
            SkipWhitespace(json, ref index);

            while (index < json.Length && json[index] != '}')
            {
                int before = index;
                SkipWhitespace(json, ref index);
                string key = ParseString(json, ref index);
                if (key == null) return null;                  // key was not a string
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ':') index++;
                SkipWhitespace(json, ref index);
                object value = ParseValue(json, ref index);
                dict[key] = value;
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',') index++;
                // No progress means the input is not JSON. Without this guard a value the
                // number parser cannot consume (`[x`) is re-read forever — the runtime twin
                // of this class hung for 20 s and died of OutOfMemory on exactly that shape.
                if (index == before) return null;
            }
            if (index < json.Length) index++; // skip }
            return dict;
        }

        private static List<object> ParseArray(string json, ref int index)
        {
            var list = new List<object>();
            index++; // skip [
            SkipWhitespace(json, ref index);

            while (index < json.Length && json[index] != ']')
            {
                int before = index;
                list.Add(ParseValue(json, ref index));
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',') index++;
                if (index == before) return null;              // no progress: not JSON
            }
            if (index < json.Length) index++; // skip ]
            return list;
        }

        private static string ParseString(string json, ref int index)
        {
            if (json[index] != '"') return null;
            index++; // skip opening "

            var sb = new System.Text.StringBuilder();
            while (index < json.Length)
            {
                char c = json[index++];
                if (c == '"') return sb.ToString();
                if (c == '\\' && index < json.Length)
                {
                    char esc = json[index++];
                    switch (esc)
                    {
                        case '"': case '\\': case '/': sb.Append(esc); break;
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'u':
                            if (index + 4 <= json.Length)
                            {
                                string hex = json.Substring(index, 4);
                                sb.Append((char)Convert.ToInt32(hex, 16));
                                index += 4;
                            }
                            break;
                        default: sb.Append(esc); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static object ParseNumber(string json, ref int index)
        {
            int start = index;
            if (json[index] == '-') index++;
            while (index < json.Length && (char.IsDigit(json[index]) || json[index] == '.' || json[index] == 'e' || json[index] == 'E' || json[index] == '+' || json[index] == '-'))
            {
                if ((json[index] == '-' || json[index] == '+') && index > start + 1 &&
                    json[index - 1] != 'e' && json[index - 1] != 'E')
                    break;
                index++;
            }
            string numStr = json.Substring(start, index - start);
            if (numStr.Contains(".") || numStr.Contains("e") || numStr.Contains("E"))
            {
                if (double.TryParse(numStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double d))
                    return d;
            }
            else
            {
                if (long.TryParse(numStr, out long l)) return l;
            }
            return 0;
        }

        private static object ParseBool(string json, ref int index)
        {
            if (json.Substring(index).StartsWith("true", StringComparison.Ordinal))
            { index += 4; return true; }
            if (json.Substring(index).StartsWith("false", StringComparison.Ordinal))
            { index += 5; return false; }
            return null;
        }

        private static object ParseNull(string json, ref int index)
        {
            if (json.Substring(index).StartsWith("null", StringComparison.Ordinal))
            { index += 4; return null; }
            return null;
        }
    }
}
