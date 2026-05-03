using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    public static partial class SpellDataImporter
    {

        private static string GetString(Dictionary<string, object> dict, string key, string fallback = "")
        {
            if (dict != null && dict.TryGetValue(key, out var val) && val != null)
                return val.ToString();
            return fallback;
        }

        private static float GetFloat(Dictionary<string, object> dict, string key, float fallback = 0f)
        {
            if (dict != null && dict.TryGetValue(key, out var val) && val != null)
                return ToFloat(val);
            return fallback;
        }

        private static float GetFloat(List<object> list, int index, float fallback = 0f)
        {
            if (list != null && index < list.Count && list[index] != null)
                return ToFloat(list[index]);
            return fallback;
        }

        private static int GetInt(Dictionary<string, object> dict, string key, int fallback = 0)
        {
            if (dict != null && dict.TryGetValue(key, out var val) && val != null)
                return Mathf.RoundToInt(ToFloat(val));
            return fallback;
        }

        private static bool GetBool(Dictionary<string, object> dict, string key, bool fallback = false)
        {
            if (dict != null && dict.TryGetValue(key, out var val))
            {
                if (val is bool b) return b;
                if (val is string s) return s.Equals("true", StringComparison.OrdinalIgnoreCase);
                return ToFloat(val) != 0f;
            }
            return fallback;
        }

        private static Dictionary<string, object> GetDict(Dictionary<string, object> dict, string key)
        {
            if (dict != null && dict.TryGetValue(key, out var val) && val is Dictionary<string, object> d)
                return d;
            return null;
        }

        private static List<object> GetArray(Dictionary<string, object> dict, string key)
        {
            if (dict != null && dict.TryGetValue(key, out var val) && val is List<object> list)
                return list;
            return null;
        }

        private static float ToFloat(object val)
        {
            if (val is double d) return (float)d;
            if (val is float f) return f;
            if (val is long l) return l;
            if (val is int i) return i;
            if (val is string s && float.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                return parsed;
            return 0f;
        }

        // ── Path Helpers ──

        private static string ResolveJsonPath()
        {
            // Try relative to project root (workspace root is 2 levels up from Assets)
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            string candidate = Path.Combine(projectRoot, SPELLS_JSON_REL.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;

            // Try from dataPath directly
            candidate = Path.Combine(Application.dataPath, "..", "..", "..", SPELLS_JSON_REL.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;

            // Fallback: try workspace patterns
            string[] searchPaths = {
                Path.Combine(projectRoot, "python", "data", "spells", "spells.json"),
                Path.Combine(Application.dataPath, "..", "..", "python", "data", "spells", "spells.json"),
            };
            foreach (var p in searchPaths)
            {
                string full = Path.GetFullPath(p);
                if (File.Exists(full)) return full;
            }

            return null;
        }

        private static void EnsureFolder(string assetPath)
        {
            var parts = assetPath.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        // ── MiniJSON (embedded lightweight JSON parser) ──

        /// <summary>
        /// Minimal JSON parser. Returns Dictionary&lt;string,object&gt; for objects,
        /// List&lt;object&gt; for arrays, double for numbers, string, bool, or null.
        /// </summary>
        private static class Json
        {
            public static object Deserialize(string json)
            {
                if (string.IsNullOrEmpty(json)) return null;
                return new Parser(json).ParseValue();
            }

            private sealed class Parser
            {
                private readonly string _json;
                private int _pos;

                public Parser(string json) { _json = json; _pos = 0; }

                public object ParseValue()
                {
                    SkipWhitespace();
                    if (_pos >= _json.Length) return null;
                    char c = _json[_pos];
                    if (c == '{') return ParseObject();
                    if (c == '[') return ParseArray();
                    if (c == '"') return ParseString();
                    if (c == 't' || c == 'f') return ParseBool();
                    if (c == 'n') return ParseNull();
                    return ParseNumber();
                }

                private Dictionary<string, object> ParseObject()
                {
                    var dict = new Dictionary<string, object>();
                    _pos++; // skip '{'
                    SkipWhitespace();
                    if (_pos < _json.Length && _json[_pos] == '}') { _pos++; return dict; }

                    while (_pos < _json.Length)
                    {
                        SkipWhitespace();
                        string key = ParseString();
                        SkipWhitespace();
                        if (_pos < _json.Length && _json[_pos] == ':') _pos++;
                        SkipWhitespace();
                        object val = ParseValue();
                        dict[key] = val;
                        SkipWhitespace();
                        if (_pos < _json.Length && _json[_pos] == ',') { _pos++; continue; }
                        if (_pos < _json.Length && _json[_pos] == '}') { _pos++; break; }
                        break; // malformed
                    }
                    return dict;
                }

                private List<object> ParseArray()
                {
                    var list = new List<object>();
                    _pos++; // skip '['
                    SkipWhitespace();
                    if (_pos < _json.Length && _json[_pos] == ']') { _pos++; return list; }

                    while (_pos < _json.Length)
                    {
                        SkipWhitespace();
                        list.Add(ParseValue());
                        SkipWhitespace();
                        if (_pos < _json.Length && _json[_pos] == ',') { _pos++; continue; }
                        if (_pos < _json.Length && _json[_pos] == ']') { _pos++; break; }
                        break;
                    }
                    return list;
                }

                private string ParseString()
                {
                    if (_pos >= _json.Length || _json[_pos] != '"') return "";
                    _pos++; // skip opening "
                    int start = _pos;
                    var sb = new System.Text.StringBuilder();
                    while (_pos < _json.Length)
                    {
                        char c = _json[_pos];
                        if (c == '\\')
                        {
                            _pos++;
                            if (_pos < _json.Length)
                            {
                                char esc = _json[_pos];
                                switch (esc)
                                {
                                    case '"': sb.Append('"'); break;
                                    case '\\': sb.Append('\\'); break;
                                    case '/': sb.Append('/'); break;
                                    case 'n': sb.Append('\n'); break;
                                    case 'r': sb.Append('\r'); break;
                                    case 't': sb.Append('\t'); break;
                                    case 'u':
                                        if (_pos + 4 < _json.Length)
                                        {
                                            string hex = _json.Substring(_pos + 1, 4);
                                            sb.Append((char)Convert.ToInt32(hex, 16));
                                            _pos += 4;
                                        }
                                        break;
                                    default: sb.Append(esc); break;
                                }
                            }
                        }
                        else if (c == '"')
                        {
                            _pos++; // skip closing "
                            return sb.ToString();
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        _pos++;
                    }
                    return sb.ToString();
                }

                private object ParseNumber()
                {
                    int start = _pos;
                    if (_pos < _json.Length && _json[_pos] == '-') _pos++;
                    while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;
                    if (_pos < _json.Length && _json[_pos] == '.')
                    {
                        _pos++;
                        while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;
                    }
                    if (_pos < _json.Length && (_json[_pos] == 'e' || _json[_pos] == 'E'))
                    {
                        _pos++;
                        if (_pos < _json.Length && (_json[_pos] == '+' || _json[_pos] == '-')) _pos++;
                        while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;
                    }
                    string numStr = _json.Substring(start, _pos - start);
                    if (double.TryParse(numStr, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double d))
                        return d;
                    return 0.0;
                }

                private bool ParseBool()
                {
                    if (_json.Substring(_pos, Math.Min(4, _json.Length - _pos)) == "true")
                    { _pos += 4; return true; }
                    if (_json.Substring(_pos, Math.Min(5, _json.Length - _pos)) == "false")
                    { _pos += 5; return false; }
                    _pos++;
                    return false;
                }

                private object ParseNull()
                {
                    if (_pos + 4 <= _json.Length && _json.Substring(_pos, 4) == "null")
                    { _pos += 4; return null; }
                    _pos++;
                    return null;
                }

                private void SkipWhitespace()
                {
                    while (_pos < _json.Length && char.IsWhiteSpace(_json[_pos]))
                        _pos++;
                }
            }
        }
    }
}