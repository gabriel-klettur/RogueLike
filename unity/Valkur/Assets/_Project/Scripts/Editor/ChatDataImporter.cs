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
    public static partial class ChatDataImporter
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

        // ParsePersonaJson, ParseDiscountBlock, ParseAssignmentsJson, ExtractString*,
        // ExtractFloat, EnsureFolder, FindPythonData*, AssignmentData → ChatDataImporter.Parsers.cs
    }
}
