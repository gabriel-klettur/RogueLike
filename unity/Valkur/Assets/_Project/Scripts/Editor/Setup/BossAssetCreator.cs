#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Editor utility that materialises a sample <see cref="BossDefinition"/>
    /// asset so designers have a working template to clone, and so the
    /// runtime path (BossConfigurator + BossPhaseController) has at least
    /// one real asset exercising it during play sessions.
    ///
    /// The sample defines a 3-phase boss with sane defaults for each phase:
    ///   Phase 1 (HP 1.0): single fireball every 4s — opening rotation.
    ///   Phase 2 (HP 0.5): fireball + iceball alternating, 2.5s cadence.
    ///   Phase 3 (HP 0.2): adds meteor every cast, 1.8s cadence — desperation.
    /// HP / damage tuning lives on the base monster + spell defs; this
    /// asset only describes which spells fire when.
    /// </summary>
    public static class BossAssetCreator
    {
        private const string AssetDir  = "Assets/_Project/Data/Bosses";
        private const string AssetPath = AssetDir + "/SampleBoss.asset";

        [MenuItem("Valkur/Combat/Create or Refresh Sample Boss Asset", priority = 200)]
        public static void CreateOrRefresh()
        {
            EnsureDirectory();

            var def = AssetDatabase.LoadAssetAtPath<BossDefinition>(AssetPath);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<BossDefinition>();
                AssetDatabase.CreateAsset(def, AssetPath);
            }

            // The sample uses spell keys that any project shipping with
            // the standard Valkur catalog will resolve at runtime.
            // Designers fork this asset and edit phases / spells per boss.
            def.phases = new[]
            {
                new BossDefinition.Phase
                {
                    hpThreshold     = 1.0f,
                    label           = "Opening",
                    autoCastList    = new[] { "fireball" },
                    autoCastPeriod  = 4f,
                    activationSfxId = string.Empty, // intro phase needs no sting
                },
                new BossDefinition.Phase
                {
                    hpThreshold     = 0.5f,
                    label           = "Frenzy",
                    autoCastList    = new[] { "fireball", "iceball" },
                    autoCastPeriod  = 2.5f,
                    // A REAL catalog id, not a plausible-looking one. AudioCatalog.asset holds
                    // no "spell_*" entry at all, so every id of that shape resolves to nothing
                    // but one warning — this seeded "spell_firework_launch", which
                    // BossDefinitionDataIntegrityTests forbids by name. The shipped
                    // SampleBoss.asset was repaired once and this creator was not, so re-running
                    // the menu item put the fault straight back and turned that test red.
                    activationSfxId = "barbol_attack_2",
                },
                new BossDefinition.Phase
                {
                    hpThreshold     = 0.2f,
                    label           = "Final Stand",
                    autoCastList    = new[] { "fireball", "iceball", "meteor" },
                    autoCastPeriod  = 1.8f,
                    activationSfxId = "barbol_damage_1",   // see the phase above
                },
            };

            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[BossAssetCreator] Sample BossDefinition written at {AssetPath}. " +
                      "Wire it onto a boss prefab via BossConfigurator + BossPhaseController + NPCAutoCast. " +
                      "Spell keys must exist in the active SpellCatalog (warning logs flag missing ones).");
        }

        private static void EnsureDirectory()
        {
            if (!Directory.Exists(AssetDir))
            {
                Directory.CreateDirectory(AssetDir);
                AssetDatabase.Refresh();
            }
        }
    }
}
#endif
