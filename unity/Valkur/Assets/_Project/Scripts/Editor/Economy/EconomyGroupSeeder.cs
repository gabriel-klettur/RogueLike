using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.EditorTools.Economy
{
    /// <summary>
    /// Creates the shipped <see cref="EconomyGroupDefinition"/> assets and wires each vendor
    /// config to its own, so a vendor's margins finally say something about who they are.
    ///
    /// <para><b>Every vendor shipped with <c>economyGroup: {fileID: 0}</c>.</b> The whole
    /// margin half of the price pipeline was therefore dead: two of its six steps resolved to
    /// a multiplier of exactly 1, and the shop counter was the same everywhere in the world.
    /// This is the data that turns it on.</para>
    ///
    /// <para><b>Specialisation is expressed through <c>typeMargins</c>, not through a
    /// whitelist.</b> The catalogue carries six item types and 64 minerals, so "the smith
    /// deals fairly in ore" is one row here and would be sixty-four in <c>itemMargins</c>. A
    /// whitelist would say something different and worse — that the character REFUSES
    /// everything else, which turns a specialist into a wall.</para>
    ///
    /// <para><b>Creation defaults, authored values win.</b> The same contract
    /// <c>TilesetRulesetImporter</c> and the persona importer use: an existing asset is left
    /// exactly as it is, so a designer's retune survives a re-run. Use the Overwrite variant
    /// to force the shipped numbers back. Neither uses <c>Undo.RecordObject</c> — see the
    /// building-template incident in CLAUDE.md for why a bulk asset tool must not.</para>
    /// </summary>
    public static class EconomyGroupSeeder
    {
        private const string GroupFolder = "Assets/_Project/Data/Vendor/EconomyGroups";
        private const string ConfigFolder = "Assets/_Project/Data/Vendor/Configs";

        /// <summary>
        /// What the player pays / receives on the type this character actually deals in.
        /// 1.00 both ways is the reference the whole catalogue was priced against: the
        /// specialist is the honest price, and everyone else is a mark-up on it.
        /// </summary>
        private const float SpecialtyBuy = 1.00f;
        private const float SpecialtySell = 1.00f;

        /// <summary>
        /// The spread outside a character's trade. Wide enough that walking across town to the
        /// right counter is worth doing (a 30-coin gem is 40 at the wrong shop and pays 21
        /// instead of 30), narrow enough that being in a hurry is not punished.
        /// </summary>
        private const float OutsideBuy = 1.35f;
        private const float OutsideSell = 0.70f;

        /// <summary>
        /// vendorKey -> the item types that vendor trades at specialist rates.
        ///
        /// <para>The smith takes <c>mineral</c> as well as <c>blacksmith</c>, and that second
        /// entry is load-bearing rather than flavour: the 64 minerals are the entire output of
        /// the mining profession and no vendor in the game was their buyer, which is a large
        /// part of why they all shipped priced at zero. A profession whose product has no
        /// counter is a profession with no economy.</para>
        /// </summary>
        private static readonly Dictionary<string, string[]> Specialties = new Dictionary<string, string[]>
        {
            { "vendor_cheff_gatita",     new[] { "food" } },
            { "vendor_lumberjack_pavel", new[] { "lumberjack" } },
            { "vendor_blacksmith_smith", new[] { "blacksmith", "mineral" } },
            { "vendor_alchemist_valeria", new[] { "alchemy" } },
            { "vendor_mague_roberto",    new[] { "magic" } },
        };

        /// <summary>
        /// The purse a vendor is authored with, and the reason it is generous.
        ///
        /// <para>The point of a finite float is that a shop can be temporarily bought out —
        /// "the smith has no coin left this morning" — not that unloading a bag is a chore.
        /// 400 coins is roughly three <c>knight_longsword</c>s or six rarity-4 crystals, so a
        /// normal session never touches it and a deliberate liquidation does.</para>
        /// </summary>
        private const int VendorCoinFloat = 400;

        /// <summary>
        /// Seconds from an emptied shop back to a full one. Ten minutes is long enough that
        /// selling out a vendor MEANS something for the rest of the session, short enough that
        /// it is never the thing a player is waiting on — and it is well over the 5-minute
        /// vendor respawn, so a respawn is still the faster reset and nothing about this makes
        /// the world feel locked.
        /// </summary>
        private const float VendorRestockSeconds = 600f;

        [MenuItem("Valkur/Economy/Seed Economy Content")]
        public static void Seed() => Run(overwrite: false);

        [MenuItem("Valkur/Economy/Seed Economy Content (Overwrite Authored)")]
        public static void SeedOverwrite()
        {
            if (!EditorUtility.DisplayDialog(
                    "Overwrite economy content",
                    "This replaces the margins on every existing EconomyGroup asset AND every field " +
                    "of EconomyTuning with the shipped defaults, discarding any hand-tuning. " +
                    "Continue?",
                    "Overwrite", "Cancel"))
                return;
            Run(overwrite: true);
        }

        /// <summary>
        /// Where <see cref="EconomyTuning"/> lives. Under <c>Resources/</c> because the two
        /// systems that read it — <c>MarketService</c> and <c>DeathDropSystem</c> — are both
        /// <c>AddComponent</c>-ed onto bare GameObjects with no inspector slot to be wired
        /// from, the same reason <c>ProgressionCatalog</c> is there.
        /// </summary>
        private const string TuningFolder = "Assets/_Project/Resources/Economy";

        /// <summary>
        /// Creates the tuning asset if it is missing, and never touches an existing one
        /// outside the explicit Overwrite variant.
        ///
        /// <para>Its absence is not an error — <c>EconomyTuning.Active</c> hands back the
        /// shipped defaults — but it IS the difference between an Economy editor whose changes
        /// survive a restart and one whose changes vanish, so the editor says so on its status
        /// line and this is what makes the warning actionable.</para>
        /// </summary>
        private static void SeedTuning(bool overwrite, ref int created)
        {
            Directory.CreateDirectory(TuningFolder);
            string path = $"{TuningFolder}/EconomyTuning.asset";

            var tuning = AssetDatabase.LoadAssetAtPath<EconomyTuning>(path);
            if (tuning != null)
            {
                if (!overwrite) return;
                var fresh = ScriptableObject.CreateInstance<EconomyTuning>();
                EditorUtility.CopySerialized(fresh, tuning);
                Object.DestroyImmediate(fresh);
                EditorUtility.SetDirty(tuning);
                return;
            }

            tuning = ScriptableObject.CreateInstance<EconomyTuning>();
            AssetDatabase.CreateAsset(tuning, path);
            EditorUtility.SetDirty(tuning);
            created++;
        }

        private static void Run(bool overwrite)
        {
            Directory.CreateDirectory(GroupFolder);
            AssetDatabase.Refresh();

            int created = 0, retuned = 0, wired = 0;
            SeedTuning(overwrite, ref created);

            foreach (var pair in Specialties)
            {
                string vendorKey = pair.Key;
                string groupPath = $"{GroupFolder}/EG_{vendorKey}.asset";

                var group = AssetDatabase.LoadAssetAtPath<EconomyGroupDefinition>(groupPath);
                bool isNew = group == null;
                if (isNew)
                {
                    group = ScriptableObject.CreateInstance<EconomyGroupDefinition>();
                    AssetDatabase.CreateAsset(group, groupPath);
                    created++;
                }

                if (isNew || overwrite)
                {
                    group.groupKey = $"eg_{vendorKey}";
                    group.defaultMargin = new EconomyGroupDefinition.MarginEntry
                    {
                        buyMultiplier = OutsideBuy,
                        sellMultiplier = OutsideSell,
                    };
                    group.typeMargins.Clear();
                    foreach (string type in pair.Value)
                    {
                        group.typeMargins.Add(new EconomyGroupDefinition.TypeMarginEntry
                        {
                            itemType = type,
                            margin = new EconomyGroupDefinition.MarginEntry
                            {
                                buyMultiplier = SpecialtyBuy,
                                sellMultiplier = SpecialtySell,
                            },
                        });
                    }
                    EditorUtility.SetDirty(group);
                    if (!isNew) retuned++;
                }

                // The wiring is repaired on EVERY run, overwrite or not. A group asset that
                // exists but is not referenced is the exact state this seeder was written to
                // end, and it is invisible: the pipeline resolves a margin of 1 and every
                // price still looks plausible.
                string configPath = $"{ConfigFolder}/{vendorKey}.asset";
                var config = AssetDatabase.LoadAssetAtPath<VendorConfigDefinition>(configPath);
                if (config == null)
                {
                    Debug.LogWarning($"[EconomyGroupSeeder] No vendor config at {configPath}; " +
                                     $"group EG_{vendorKey} created but wired to nothing.");
                    continue;
                }
                bool changed = false;
                if (config.economyGroup != group)
                {
                    config.economyGroup = group;
                    changed = true;
                }

                // The purse and the restock clock are repaired on every run for the same
                // reason as the group reference: both ship OFF at 0 (they have to — a key
                // absent from an existing .asset deserialises to 0, and reading that as an
                // EMPTY purse would make every shipped vendor refuse to buy), so a vendor
                // that has never been seeded is indistinguishable from one deliberately left
                // unlimited. Authored non-zero values are never overwritten outside the
                // explicit Overwrite variant.
                if (config.coinFloat <= 0 || overwrite)
                {
                    config.coinFloat = VendorCoinFloat;
                    changed = true;
                }
                if (config.restockSeconds <= 0f || overwrite)
                {
                    config.restockSeconds = VendorRestockSeconds;
                    changed = true;
                }

                if (changed)
                {
                    EditorUtility.SetDirty(config);
                    wired++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[EconomyGroupSeeder] {created} group(s) created, {retuned} retuned, " +
                      $"{wired} vendor config(s) wired.");
        }
    }
}
