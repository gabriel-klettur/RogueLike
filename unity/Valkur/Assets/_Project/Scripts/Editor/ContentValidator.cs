using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Pre-build content validator that checks for missing assets, broken references,
    /// and data integrity issues across all ScriptableObjects.
    /// Maps to Python's validation passes before game start.
    /// 
    /// Run via menu: Valkur > Validation > Run All Validators
    /// </summary>
    public static class ContentValidator
    {
        [MenuItem("Valkur/Validation/Run All Validators")]
        public static void RunAll()
        {
            int totalIssues = 0;
            totalIssues += ValidateMonsterDefinitions();
            totalIssues += ValidateSpellDefinitions();
            totalIssues += ValidateItemDefinitions();
            totalIssues += ValidatePlayerDefinitions();
            totalIssues += ValidatePrefabReferences();

            if (totalIssues == 0)
                Debug.Log("[ContentValidator] All validations passed. No issues found.");
            else
                Debug.LogWarning($"[ContentValidator] Validation complete. {totalIssues} issue(s) found.");
        }

        [MenuItem("Valkur/Validation/Validate Monsters")]
        public static int ValidateMonsterDefinitions()
        {
            var assets = LoadAll<MonsterDefinition>();
            int issues = 0;

            foreach (var def in assets)
            {
                if (string.IsNullOrEmpty(def.monsterKey))
                {
                    Debug.LogWarning($"[Validator] MonsterDefinition '{def.name}' has empty monsterKey.", def);
                    issues++;
                }
                if (def.stats.hp <= 0)
                {
                    Debug.LogWarning($"[Validator] MonsterDefinition '{def.monsterKey}' has HP <= 0.", def);
                    issues++;
                }
                if (def.stats.speed <= 0)
                {
                    Debug.LogWarning($"[Validator] MonsterDefinition '{def.monsterKey}' has speed <= 0.", def);
                    issues++;
                }
            }

            Debug.Log($"[Validator] Monsters: {assets.Length} checked, {issues} issues.");
            return issues;
        }

        [MenuItem("Valkur/Validation/Validate Spells")]
        public static int ValidateSpellDefinitions()
        {
            var assets = LoadAll<SpellDefinition>();
            int issues = 0;

            foreach (var def in assets)
            {
                if (string.IsNullOrEmpty(def.spellKey))
                {
                    Debug.LogWarning($"[Validator] SpellDefinition '{def.name}' has empty spellKey.", def);
                    issues++;
                }
                if (def.type == SpellType.Projectile && def.speed <= 0)
                {
                    Debug.LogWarning($"[Validator] SpellDefinition '{def.spellKey}' is Projectile but speed <= 0.", def);
                    issues++;
                }
                if (def.cooldownDuration < 0)
                {
                    Debug.LogWarning($"[Validator] SpellDefinition '{def.spellKey}' has negative cooldown.", def);
                    issues++;
                }
            }

            Debug.Log($"[Validator] Spells: {assets.Length} checked, {issues} issues.");
            return issues;
        }

        [MenuItem("Valkur/Validation/Validate Items")]
        public static int ValidateItemDefinitions()
        {
            var assets = LoadAll<ItemDefinition>();
            int issues = 0;
            var ids = new HashSet<string>();

            foreach (var def in assets)
            {
                if (string.IsNullOrEmpty(def.itemId))
                {
                    Debug.LogWarning($"[Validator] ItemDefinition '{def.name}' has empty itemId.", def);
                    issues++;
                    continue;
                }
                if (!ids.Add(def.itemId))
                {
                    Debug.LogWarning($"[Validator] Duplicate itemId '{def.itemId}' in ItemDefinition '{def.name}'.", def);
                    issues++;
                }
                if (def.stackable && def.maxStack <= 0)
                {
                    Debug.LogWarning($"[Validator] ItemDefinition '{def.itemId}' is stackable but maxStack <= 0.", def);
                    issues++;
                }
                if (def.icon == null && def.iconSmall == null)
                {
                    Debug.LogWarning($"[Validator] ItemDefinition '{def.itemId}' has no icon assigned.", def);
                    issues++;
                }
            }

            Debug.Log($"[Validator] Items: {assets.Length} checked, {issues} issues.");
            return issues;
        }

        [MenuItem("Valkur/Validation/Validate Players")]
        public static int ValidatePlayerDefinitions()
        {
            var assets = LoadAll<PlayerDefinition>();
            int issues = 0;

            foreach (var def in assets)
            {
                if (string.IsNullOrEmpty(def.playerKey))
                {
                    Debug.LogWarning($"[Validator] PlayerDefinition '{def.name}' has empty playerKey.", def);
                    issues++;
                }
                if (def.basicSpeed <= 0)
                {
                    Debug.LogWarning($"[Validator] PlayerDefinition '{def.playerKey}' has speed <= 0.", def);
                    issues++;
                }
            }

            Debug.Log($"[Validator] Players: {assets.Length} checked, {issues} issues.");
            return issues;
        }

        [MenuItem("Valkur/Validation/Validate Prefab References")]
        public static int ValidatePrefabReferences()
        {
            int issues = 0;
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project" });

            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                // Check for missing script references
                var components = prefab.GetComponentsInChildren<Component>(true);
                foreach (var comp in components)
                {
                    if (comp == null)
                    {
                        Debug.LogWarning($"[Validator] Prefab '{path}' has missing script reference.", prefab);
                        issues++;
                        break;
                    }
                }

                // Check SpriteRenderers with null sprites (acceptable for runtime-assigned)
                var renderers = prefab.GetComponentsInChildren<SpriteRenderer>(true);
                foreach (var sr in renderers)
                {
                    if (sr != null && sr.sprite == null && !path.Contains("Placeholder"))
                    {
                        // Info only, not an issue — sprites may be assigned at runtime
                    }
                }
            }

            Debug.Log($"[Validator] Prefabs: {prefabGuids.Length} checked, {issues} issues.");
            return issues;
        }

        private static T[] LoadAll<T>() where T : ScriptableObject
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { "Assets/_Project" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(x => x != null)
                .ToArray();
        }
    }
}
