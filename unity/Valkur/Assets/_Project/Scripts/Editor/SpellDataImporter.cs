using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Editor tool that imports spell definitions from python/data/spells/spells.json
    /// and creates/updates SpellDefinition ScriptableObject assets + SpellCatalog.
    ///
    /// Unit conversions (Python → Unity):
    ///   Distances/radii (px)   → ÷ 16  (1 tile = 16 px = 1 world unit)
    ///   Speed (px/tick @60fps) → × 3.75 (= ×60/16 → world units/sec)
    ///   Lifetime (ticks @60fps)→ ÷ 60  (→ seconds)
    ///   Timings (seconds)      → keep as-is
    ///   Damage/HP/counts       → keep as-is
    ///   Angles (degrees)       → keep as-is
    ///
    /// Menu: Valkur > Spells > Import Spells from Python JSON
    /// </summary>
    public static partial class SpellDataImporter
    {
        private const string SPELLS_JSON_REL = "python/data/spells/spells.json";
        private const string SPELL_ASSET_DIR = "Assets/_Project/Data/Catalogs/Spells";
        private const string CATALOG_PATH    = "Assets/_Project/Data/Catalogs/SpellCatalog.asset";
        private const float PX_TO_WORLD = 1f / 16f;        // 16 px = 1 world unit
        private const float SPEED_TO_WORLD = 60f / 16f;    // px/tick → world/sec (3.75)
        private const float TICK_TO_SEC = 1f / 60f;         // tick → seconds

        [MenuItem("Valkur/Spells/Import Spells from Python JSON")]
        public static void ImportAll()
        {
            string jsonPath = ResolveJsonPath();
            if (string.IsNullOrEmpty(jsonPath))
            {
                Debug.LogError("[SpellDataImporter] Could not find spells.json. Expected at: " + SPELLS_JSON_REL);
                return;
            }

            string json = File.ReadAllText(jsonPath);
            var spellsDict = ParseSpellsJson(json);
            if (spellsDict == null || spellsDict.Count == 0)
            {
                Debug.LogError("[SpellDataImporter] Failed to parse spells.json or it is empty.");
                return;
            }

            // Add hardcoded hostile_slash_giant (malformed in JSON — embedded inside fireball's VFX block)
            if (!spellsDict.ContainsKey("hostile_slash_giant"))
                spellsDict["hostile_slash_giant"] = BuildHostileSlashGiant();

            if (!AssetDatabase.IsValidFolder(SPELL_ASSET_DIR))
                EnsureFolder(SPELL_ASSET_DIR);

            int created = 0, updated = 0;
            var allAssets = new List<SpellDefinition>();

            foreach (var kv in spellsDict)
            {
                string key = kv.Key;
                var data = kv.Value;
                string assetPath = $"{SPELL_ASSET_DIR}/{key}.asset";

                var asset = AssetDatabase.LoadAssetAtPath<SpellDefinition>(assetPath);
                bool isNew = asset == null;
                if (isNew)
                {
                    asset = ScriptableObject.CreateInstance<SpellDefinition>();
                    asset.name = key;
                }

                ApplyValues(asset, key, data);

                if (isNew)
                {
                    AssetDatabase.CreateAsset(asset, assetPath);
                    created++;
                }
                else
                {
                    EditorUtility.SetDirty(asset);
                    updated++;
                }

                allAssets.Add(asset);
            }

            // Build SpellCatalog
            BuildCatalog(allAssets);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SpellDataImporter] Done! Created {created}, updated {updated}, total {allAssets.Count} spells. Catalog at {CATALOG_PATH}");
        }

        // ── JSON Parsing ──

        private static Dictionary<string, Dictionary<string, object>> ParseSpellsJson(string json)
        {
            // Use Unity's JsonUtility indirectly via a wrapper, or manual parsing.
            // Since Unity's JsonUtility doesn't handle Dictionary, use MiniJSON-style parsing.
            var root = Json.Deserialize(json) as Dictionary<string, object>;
            if (root == null) return null;

            var result = new Dictionary<string, Dictionary<string, object>>();
            foreach (var kv in root)
            {
                if (kv.Value is Dictionary<string, object> spellData)
                    result[kv.Key] = spellData;
            }
            return result;
        }

        // ── Value Application ──

        private static void ApplyValues(SpellDefinition spell, string key, Dictionary<string, object> data)
        {
            // Identity
            spell.spellKey = key;
            spell.displayName = GetString(data, "name", key);
            spell.type = ParseSpellType(GetString(data, "type", "projectile"));
            spell.manaCost = GetFloat(data, "mana_cost");

            // Timings
            var timings = GetDict(data, "timings");
            if (timings != null)
            {
                spell.prepareDuration = GetFloat(timings, "prepare");
                spell.channelDuration = GetFloat(timings, "channel");
                spell.cooldownDuration = GetFloat(timings, "cooldown");
            }

            // Rules
            var rules = GetDict(data, "rules");
            if (rules != null)
            {
                spell.lockCastDirection = GetBool(rules, "lock_cast_direction");
                spell.interruptible = GetBool(rules, "interruptible");
                spell.automaticCastPunish = GetFloat(rules, "automatic_cast_punish", 1f);
                spell.allowMovement = GetBool(rules, "allow_movement");
                spell.automatic = GetBool(rules, "automatic");
            }

            // Constraints
            var constraints = GetDict(data, "constraints");
            if (constraints != null)
            {
                spell.maxInstances = GetInt(constraints, "max_instances", 1);
                spell.allowOverlap = GetBool(constraints, "allow_overlap");
            }

            // Effect — varies widely by spell type
            var effect = GetDict(data, "effect");
            if (effect != null)
                ApplyEffect(spell, effect);

            // Meta
            var meta = GetDict(data, "meta");
            if (meta != null)
            {
                spell.speedMultiplier = GetFloat(meta, "speed_multiplier", 1f);
                spell.offset = GetFloat(meta, "offset") * PX_TO_WORLD;
            }

            // Telegraph
            var telegraphColor = GetArray(data, "telegraph_color");
            if (telegraphColor != null && telegraphColor.Count >= 3)
            {
                float r = GetFloat(telegraphColor, 0) / 255f;
                float g = GetFloat(telegraphColor, 1) / 255f;
                float b = GetFloat(telegraphColor, 2) / 255f;
                float a = GetFloat(data, "telegraph_alpha", 80f) / 255f;
                spell.telegraphColor = new Color(r, g, b, a);
                spell.telegraphAlpha = GetFloat(data, "telegraph_alpha", 80f);
            }

            // VFX
            var vfx = GetDict(data, "vfx");
            if (vfx != null)
                ApplyVfx(spell, vfx);

            // Particle color from meta (smoke_emitter)
            if (meta != null)
            {
                var metaColor = GetArray(meta, "particle_color");
                if (metaColor != null && metaColor.Count >= 3)
                {
                    spell.particleColor = new Color(
                        GetFloat(metaColor, 0) / 255f,
                        GetFloat(metaColor, 1) / 255f,
                        GetFloat(metaColor, 2) / 255f, 1f);
                }
            }
        }

    }
}