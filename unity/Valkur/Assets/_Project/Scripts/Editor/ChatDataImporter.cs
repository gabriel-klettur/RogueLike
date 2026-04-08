using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Imports chat personas from python/data/chat/ → NPCPersonaDefinition SOs.
    /// Imports assignments.json → ChatAssignmentCatalog SO.
    /// Menu: Valkur > Chat > Import Personas from Python JSON
    /// Menu: Valkur > Chat > Import Assignments from Python JSON
    /// </summary>
    public static class ChatDataImporter
    {
        private const string PERSONA_OUTPUT_DIR = "Assets/_Project/Data/ChatPersonas";
        private const string CATALOG_PATH = "Assets/_Project/Data/ChatAssignmentCatalog.asset";

        [MenuItem("Valkur/Chat/Import Personas from Python JSON")]
        public static void ImportPersonas()
        {
            string personasDir = FindPythonDataDir("chat/personas");
            if (personasDir == null)
            {
                Debug.LogError("[ChatDataImporter] python/data/chat/personas/ not found.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(PERSONA_OUTPUT_DIR))
            {
                EnsureFolder(PERSONA_OUTPUT_DIR);
            }

            int count = 0;
            foreach (string file in Directory.GetFiles(personasDir, "*.json"))
            {
                string json = File.ReadAllText(file);
                string fileName = Path.GetFileNameWithoutExtension(file);
                string assetPath = $"{PERSONA_OUTPUT_DIR}/Persona_{fileName}.asset";

                var persona = AssetDatabase.LoadAssetAtPath<NPCPersonaDefinition>(assetPath);
                if (persona == null)
                {
                    persona = ScriptableObject.CreateInstance<NPCPersonaDefinition>();
                    AssetDatabase.CreateAsset(persona, assetPath);
                }

                ParsePersonaJson(json, fileName, persona);
                EditorUtility.SetDirty(persona);
                count++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[ChatDataImporter] Imported {count} NPC personas.");
        }

        [MenuItem("Valkur/Chat/Import Assignments from Python JSON")]
        public static void ImportAssignments()
        {
            string assignFile = FindPythonDataFile("chat/assignments.json");
            if (assignFile == null)
            {
                Debug.LogError("[ChatDataImporter] python/data/chat/assignments.json not found.");
                return;
            }

            string json = File.ReadAllText(assignFile);
            var assignments = ParseAssignmentsJson(json);

            var catalog = AssetDatabase.LoadAssetAtPath<ChatAssignmentCatalog>(CATALOG_PATH);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ChatAssignmentCatalog>();
                AssetDatabase.CreateAsset(catalog, CATALOG_PATH);
            }
            catalog.assignments.Clear();

            foreach (var kvp in assignments)
            {
                string entityName = kvp.Key;
                string personaId = kvp.Value.persona_id;
                string role = kvp.Value.role;
                float chatRange = kvp.Value.chat_range > 0 ? kvp.Value.chat_range : 10f;

                // Find matching persona SO
                string assetPath = $"{PERSONA_OUTPUT_DIR}/Persona_{personaId}.asset";
                var persona = AssetDatabase.LoadAssetAtPath<NPCPersonaDefinition>(assetPath);

                catalog.assignments.Add(new ChatAssignmentCatalog.ChatAssignment
                {
                    entityName = entityName,
                    persona = persona,
                });

                // Update persona fields if found
                if (persona != null)
                {
                    persona.role = role ?? persona.role;
                    persona.chatRange = chatRange;
                    EditorUtility.SetDirty(persona);
                }
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ChatDataImporter] Imported {catalog.assignments.Count} chat assignments.");
        }

        private static void ParsePersonaJson(string json, string fileName, NPCPersonaDefinition persona)
        {
            persona.personaId = fileName;

            // Parse key fields manually (JsonUtility doesn't handle mixed structures well)
            persona.displayName = ExtractString(json, "name") ?? fileName;
            persona.tone = ExtractString(json, "tone") ?? "";
            persona.greeting = ExtractString(json, "greeting") ?? "";

            // Style
            string verbosity = ExtractString(json, "verbosity");
            if (!string.IsNullOrEmpty(verbosity)) persona.verbosity = verbosity;

            string emoji = ExtractString(json, "emoji");
            persona.useEmoji = emoji != "false";

            string sentencesMax = ExtractString(json, "sentences_max");
            if (int.TryParse(sentencesMax, out int maxS)) persona.maxSentences = maxS;

            // Negotiation discount limits
            persona.discountLimits.Clear();
            int negIdx = json.IndexOf("\"negotiation\"", StringComparison.Ordinal);
            if (negIdx >= 0)
            {
                int discIdx = json.IndexOf("\"discount_limits\"", negIdx);
                if (discIdx >= 0)
                {
                    int braceStart = json.IndexOf('{', discIdx + 17);
                    if (braceStart >= 0)
                    {
                        int depth = 1;
                        int braceEnd = braceStart + 1;
                        while (braceEnd < json.Length && depth > 0)
                        {
                            if (json[braceEnd] == '{') depth++;
                            else if (json[braceEnd] == '}') depth--;
                            braceEnd++;
                        }
                        string block = json.Substring(braceStart + 1, braceEnd - braceStart - 2);
                        ParseDiscountBlock(block, persona.discountLimits);
                    }
                }
            }

            // Allowed types
            persona.allowedItemTypes.Clear();
            int knIdx = json.IndexOf("\"allowed_types\"", StringComparison.Ordinal);
            if (knIdx >= 0)
            {
                int arrStart = json.IndexOf('[', knIdx);
                int arrEnd = json.IndexOf(']', arrStart);
                if (arrStart >= 0 && arrEnd > arrStart)
                {
                    string arrStr = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
                    foreach (string part in arrStr.Split(','))
                    {
                        string t = part.Trim().Trim('"');
                        if (!string.IsNullOrEmpty(t))
                            persona.allowedItemTypes.Add(t);
                    }
                }
            }
        }

        private static void ParseDiscountBlock(string block, List<NPCPersonaDefinition.DiscountEntry> list)
        {
            // Simple key-value pairs: "food_apple": 0.10, "default": 0.05
            int pos = 0;
            while (pos < block.Length)
            {
                int keyStart = block.IndexOf('"', pos);
                if (keyStart < 0) break;
                int keyEnd = block.IndexOf('"', keyStart + 1);
                if (keyEnd < 0) break;
                string key = block.Substring(keyStart + 1, keyEnd - keyStart - 1);

                int colon = block.IndexOf(':', keyEnd);
                if (colon < 0) break;

                int valStart = colon + 1;
                int valEnd = block.IndexOf(',', valStart);
                if (valEnd < 0) valEnd = block.Length;
                string valStr = block.Substring(valStart, valEnd - valStart).Trim();

                if (float.TryParse(valStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float val))
                {
                    list.Add(new NPCPersonaDefinition.DiscountEntry { itemKey = key, maxDiscount = val });
                }

                pos = valEnd + 1;
            }
        }

        private static Dictionary<string, AssignmentData> ParseAssignmentsJson(string json)
        {
            var result = new Dictionary<string, AssignmentData>();
            // Format: {"EntityName": {"persona_id": "...", "role": "...", "chat_range": 2}, ...}
            int pos = json.IndexOf('{') + 1;
            while (pos < json.Length)
            {
                int keyStart = json.IndexOf('"', pos);
                if (keyStart < 0) break;
                int keyEnd = json.IndexOf('"', keyStart + 1);
                if (keyEnd < 0) break;
                string entityName = json.Substring(keyStart + 1, keyEnd - keyStart - 1);

                int objStart = json.IndexOf('{', keyEnd);
                if (objStart < 0) break;
                int depth = 1;
                int objEnd = objStart + 1;
                while (objEnd < json.Length && depth > 0)
                {
                    if (json[objEnd] == '{') depth++;
                    else if (json[objEnd] == '}') depth--;
                    objEnd++;
                }
                string objJson = json.Substring(objStart, objEnd - objStart);

                var data = new AssignmentData
                {
                    persona_id = ExtractStringFrom(objJson, "persona_id") ?? "",
                    role = ExtractStringFrom(objJson, "role") ?? "generic",
                    chat_range = ExtractFloat(objJson, "chat_range", 10f),
                };
                result[entityName] = data;
                pos = objEnd;
            }
            return result;
        }

        private static string ExtractString(string json, string key) => ExtractStringFrom(json, key);

        private static string ExtractStringFrom(string json, string key)
        {
            string search = $"\"{key}\"";
            int idx = json.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0) return null;

            int colon = json.IndexOf(':', idx + search.Length);
            if (colon < 0) return null;

            // Skip whitespace
            int valStart = colon + 1;
            while (valStart < json.Length && (json[valStart] == ' ' || json[valStart] == '\t')) valStart++;

            if (valStart >= json.Length || json[valStart] != '"') return null;
            int valEnd = json.IndexOf('"', valStart + 1);
            if (valEnd < 0) return null;

            return json.Substring(valStart + 1, valEnd - valStart - 1);
        }

        private static float ExtractFloat(string json, string key, float fallback)
        {
            string search = $"\"{key}\"";
            int idx = json.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0) return fallback;

            int colon = json.IndexOf(':', idx + search.Length);
            if (colon < 0) return fallback;

            int valStart = colon + 1;
            while (valStart < json.Length && json[valStart] == ' ') valStart++;

            int valEnd = valStart;
            while (valEnd < json.Length && (char.IsDigit(json[valEnd]) || json[valEnd] == '.' || json[valEnd] == '-'))
                valEnd++;

            if (float.TryParse(json.Substring(valStart, valEnd - valStart),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float val))
                return val;

            return fallback;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string FindPythonDataDir(string relativePath)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string candidate = Path.Combine(dir, "python", "data", relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(candidate)) return candidate;
            return null;
        }

        private static string FindPythonDataFile(string relativePath)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string candidate = Path.Combine(dir, "python", "data", relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
            return null;
        }

        [Serializable]
        private class AssignmentData
        {
            public string persona_id;
            public string role;
            public float chat_range;
        }
    }
}
