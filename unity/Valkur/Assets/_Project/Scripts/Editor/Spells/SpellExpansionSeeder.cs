using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.EditorTools.Spells
{
    /// <summary>
    /// Creates the 27 expansion <see cref="SpellDefinition"/> assets from
    /// <see cref="SpellExpansionSeeds"/> and registers them in <c>SpellCatalog</c>.
    ///
    /// <para>CREATION DEFAULTS, AUTHORED VALUES WIN. A spell that already exists is left
    /// exactly as it is — the seeder fills fields only on the asset it just created. That is
    /// the same contract <c>TilesetRulesetImporter</c>, the persona importer and the
    /// progression seeder use, and it is what makes the tool safe to re-run after a designer
    /// has retuned something in the Inspector.</para>
    ///
    /// <para>NO <c>Undo.RecordObject</c>, deliberately. <c>BuildingPropImporter</c> created
    /// 193 template assets and recorded each for undo; they landed on the GLOBAL editor undo
    /// stack, and the first thing that popped it — the EditMode suite, which exercises the
    /// runtime editors' undo — reverted all 193 in memory to their empty creation state while
    /// the correct data sat on disk. <c>EditorUtility.SetDirty</c> alone is the right tool for
    /// data an operator re-runs rather than undoes.</para>
    /// </summary>
    internal static class SpellExpansionSeeder
    {
        private const string SpellFolder = "Assets/_Project/Data/Catalogs/Spells";
        private const string CatalogPath = "Assets/_Project/Data/Catalogs/SpellCatalog.asset";

        [MenuItem("Valkur/Spells/Seed Expansion Spells")]
        public static void Seed() => Run(overwrite: false);

        [MenuItem("Valkur/Spells/Seed Expansion Spells (Overwrite Authored)")]
        public static void SeedOverwrite()
        {
            if (!EditorUtility.DisplayDialog(
                    "Overwrite authored spell data?",
                    "This REPLACES every field on all 27 expansion spells with the values in " +
                    "SpellExpansionSeeds, discarding anything retuned in the Inspector.\n\n" +
                    "The non-overwriting variant is the one you almost always want.",
                    "Overwrite", "Cancel"))
                return;

            Run(overwrite: true);
        }

        private static void Run(bool overwrite)
        {
            EnsureFolder(SpellFolder);

            var catalog = AssetDatabase.LoadAssetAtPath<SpellCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError($"[SpellExpansion] No SpellCatalog at {CatalogPath}. Nothing seeded.");
                return;
            }

            var existing = catalog.AllSpells != null
                ? new List<SpellDefinition>(catalog.AllSpells.Where(s => s != null))
                : new List<SpellDefinition>();

            int created = 0, refreshed = 0, untouched = 0;

            foreach (var spec in SpellExpansionSeeds.All)
            {
                string path = $"{SpellFolder}/{spec.Key}.asset";
                var def = AssetDatabase.LoadAssetAtPath<SpellDefinition>(path);
                bool isNew = def == null;

                if (isNew)
                {
                    def = ScriptableObject.CreateInstance<SpellDefinition>();
                    AssetDatabase.CreateAsset(def, path);
                    created++;
                }
                else if (!overwrite)
                {
                    untouched++;
                }
                else
                {
                    refreshed++;
                }

                if (isNew || overwrite)
                {
                    Apply(def, spec);
                    EditorUtility.SetDirty(def);
                }

                if (!existing.Contains(def)) existing.Add(def);
            }

            catalog.SetSpells(existing.ToArray());
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SpellExpansion] {created} created, {refreshed} overwritten, " +
                      $"{untouched} left as authored. Catalog now holds {existing.Count} spells.");
        }

        private static void Apply(SpellDefinition def, SpellExpansionSeeds.Spec s)
        {
            def.spellKey = s.Key;
            def.displayName = s.Name;
            def.type = s.Type;
            // Every one of the 27 is player content. The nineteen AnimationProbe spells are
            // the only ones in the catalog that deliberately carry None.
            def.audience = SpellAudience.Player;

            def.manaCost = s.Mana;
            def.cooldownDuration = s.Cooldown;
            def.damage = s.Damage;
            def.speed = s.Speed;
            def.range = s.Range;
            def.radius = s.Radius;
            def.hitRadius = s.HitRadius;
            def.duration = s.Duration;
            def.damagePerTick = s.DamagePerTick;
            def.tickPeriod = s.TickPeriod;
            def.healPerTick = s.HealPerTick;
            def.lifetime = s.Lifetime;
            def.distance = s.Distance;
            def.knockback = s.Knockback;
            def.scale = s.Scale;
            def.maxInstances = s.MaxInstances;
            def.spawnAtMouse = s.SpawnAtMouse;
            def.usesAttackAnimation = s.UsesAttackAnimation;
            def.element = s.Element;
            def.particleColor = s.Swatch;
            def.castAnchor = s.Anchor;

            def.pierceCount = s.PierceCount;
            def.pierceDamageFalloff = s.PierceFalloff;
            def.homingStrength = s.HomingStrength;
            def.homingRange = s.HomingRange;
            def.projectileCount = s.ProjectileCount;
            def.spreadDegrees = s.SpreadDegrees;

            def.chargeMaxSeconds = s.ChargeMaxSeconds;
            def.chargeMinFraction = s.ChargeMinFraction;
            def.chargeDamageMultiplier = s.ChargeDamageMultiplier;
            def.chargeScaleMultiplier = s.ChargeScaleMultiplier;
            def.explosionRadius = s.ExplosionRadius;
            def.explosionDamage = s.ExplosionDamage;

            def.statModifiers = s.Mods;
            def.buffKey = s.BuffKey;

            def.wallWidth = s.WallWidth;
            def.wallHeight = s.WallHeight;
            def.wallHP = s.WallHP;
            def.blockProjectiles = s.BlockProjectiles;
            def.blockUnits = s.BlockUnits;

            def.totemKind = s.TotemKind;
            def.summonTemplate = s.SummonTemplate;
            def.summonCount = s.SummonCount;
            def.summonDuration = s.SummonDuration;

            def.ttl = s.Ttl;
            def.followCaster = s.FollowCaster;

            def.statusApplications = s.Statuses;
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
