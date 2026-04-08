using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    public static partial class PythonDataMigrator
    {

        /// <summary>
        /// Check if the report already has an Error-level entry for the given entity key.
        /// </summary>
        private static bool HasErrorForKey(MigrationReport report, string entityKey)
        {
            foreach (var e in report.Entries)
            {
                if (e.Severity == MigrationSeverity.Error && e.EntityKey == entityKey)
                    return true;
            }
            return false;
        }

        #region Helpers

        /// <summary>
        /// Resolve directional sprites from a no-sets animation state block.
        /// Reads s/se/e/ne/n/nw/w/sw keys, resolves Python paths to Unity Sprite assets.
        /// </summary>
        private static DirectionalSprites ResolveDirectionalSprites(
            Dictionary<string, object> noSets, string stateName,
            MigrationReport report, string source, string entityKey)
        {
            var ds = new DirectionalSprites();
            var stateBlock = noSets.GetValueOrDefault(stateName) as Dictionary<string, object>;
            if (stateBlock == null) return ds;

            ds.south = ResolvePythonSpritePath(stateBlock.GetValueOrDefault("s") as string, report, source, entityKey);
            ds.southEast = ResolvePythonSpritePath(stateBlock.GetValueOrDefault("se") as string, report, source, entityKey);
            ds.east = ResolvePythonSpritePath(stateBlock.GetValueOrDefault("e") as string, report, source, entityKey);
            ds.northEast = ResolvePythonSpritePath(stateBlock.GetValueOrDefault("ne") as string, report, source, entityKey);
            ds.north = ResolvePythonSpritePath(stateBlock.GetValueOrDefault("n") as string, report, source, entityKey);
            ds.northWest = ResolvePythonSpritePath(stateBlock.GetValueOrDefault("nw") as string, report, source, entityKey);
            ds.west = ResolvePythonSpritePath(stateBlock.GetValueOrDefault("w") as string, report, source, entityKey);
            ds.southWest = ResolvePythonSpritePath(stateBlock.GetValueOrDefault("sw") as string, report, source, entityKey);

            return ds;
        }

        /// <summary>
        /// Convert a Python asset path like "assets/npc/monsters/barbol/barbol_1_down.png"
        /// to a Unity Sprite at "Assets/_Project/Art/NPC/Monsters/barbol/barbol_1_down.png".
        /// </summary>
        private static Sprite ResolvePythonSpritePath(string pythonPath, MigrationReport report, string source, string entityKey)
        {
            if (string.IsNullOrEmpty(pythonPath)) return null;

            // Python path: "assets/npc/monsters/barbol/barbol_1_down.png"
            // Unity path:  "Assets/_Project/Art/NPC/Monsters/barbol/barbol_1_down.png"
            string unityPath = pythonPath;

            // Strip leading "assets/" prefix
            if (unityPath.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                unityPath = unityPath.Substring("assets/".Length);

            // Capitalize path segments to match Unity folder naming:
            // "npc/monsters/barbol/barbol_1_down.png" -> "NPC/Monsters/barbol/barbol_1_down.png"
            string[] parts = unityPath.Split('/');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].Length > 0)
                {
                    // Capitalize first letter only for known folder segments
                    if (parts[i] == "npc") parts[i] = "NPC";
                    else if (parts[i] == "monsters") parts[i] = "Monsters";
                    else if (parts[i] == "players") parts[i] = "Players";
                }
            }

            unityPath = "Assets/_Project/Art/" + string.Join("/", parts);

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(unityPath);
            if (sprite == null)
            {
                report.AddWarning(source, entityKey, $"Sprite not found at '{unityPath}' (python: '{pythonPath}')");
            }
            return sprite;
        }

        private static SpellType ParseSpellType(string s)
        {
            return s?.ToLower() switch
            {
                "projectile" => SpellType.Projectile,
                "slash" => SpellType.Slash,
                "area" => SpellType.Area,
                "dash" => SpellType.Dash,
                "teleport" => SpellType.Teleport,
                "beam" => SpellType.Beam,
                "smoke" => SpellType.Smoke,
                "wall" => SpellType.Wall,
                "trap" => SpellType.Trap,
                "shield" => SpellType.Shield,
                "boomerang" => SpellType.Boomerang,
                "meteor" => SpellType.Meteor,
                _ => SpellType.Projectile
            };
        }

        private static int GetInt(Dictionary<string, object> d, string key, int def = 0)
        {
            if (d.TryGetValue(key, out var v) && v != null)
                return Convert.ToInt32(v);
            return def;
        }

        private static float GetFloat(Dictionary<string, object> d, string key, float def = 0f)
        {
            if (d.TryGetValue(key, out var v) && v != null)
                return Convert.ToSingle(v);
            return def;
        }

        private static bool GetBool(Dictionary<string, object> d, string key, bool def = false)
        {
            if (d.TryGetValue(key, out var v) && v != null)
                return Convert.ToBoolean(v);
            return def;
        }

        #endregion

        #region Serialization helpers (JsonUtility fallback)

        [Serializable]
        private class HostilesRoot
        {
            public HostilesContainer hostiles;
        }

        [Serializable]
        private class HostilesContainer
        {
            public Dictionary<string, object> classes;
        }

        #endregion
    }

    // MiniJson is defined in AudioCatalogImporter.cs (same assembly).

    /// <summary>
    /// Extension for Dictionary to provide GetValueOrDefault for older C# versions.
    /// </summary>
    public static class DictionaryExtensions
    {
        public static TValue GetValueOrDefault<TKey, TValue>(
            this Dictionary<TKey, TValue> dict, TKey key, TValue defaultValue = default)
        {
            return dict.TryGetValue(key, out var value) ? value : defaultValue;
        }
    }
}
