using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Pins the shipped cast-variant reservations — the data half of the rule that
    /// <see cref="Valkur.Tests.EditMode.Game.UI.DirectionalAnimatorCastVariantTests"/> pins
    /// the code half of.
    ///
    /// The dwarf ships two: fireball plays <c>spell_3</c> (the <c>dwarf_spellcasting_3</c>
    /// sheet) and healing_aura plays <c>spell_2</c>. It is worth a test because the
    /// reservation is a single small field on a large generated ScriptableObject and the two
    /// ways it can be lost are both silent: <c>PlayerFramesImporter.ApplyCastVariants</c>
    /// REBUILDS the variant list on every re-import (it carries the reservation across by
    /// key — if that ever regresses, the field simply disappears), and a spell key typo
    /// falls back to the generic rotation, which looks like the feature was never wired
    /// rather than like an error.
    ///
    /// Asserted as a characteristic, not as a literal list: any variant may be reserved, but
    /// a reserved variant must name a spell that exists, and no two variants of one character
    /// may claim the same spell.
    /// </summary>
    public class PlayerCastVariantReservationTests
    {
        private const string PlayerCatalog = "Assets/_Project/Data/Catalogs/Players";
        private const string SpellCatalogRoot = "Assets/_Project/Data/Catalogs/Spells";

        private static readonly string[] Players =
            { "dwarf", "barbarian", "elven", "mague", "valkyrie" };

        private static PlayerDefinition Load(string key)
        {
            var def = AssetDatabase.LoadAssetAtPath<PlayerDefinition>($"{PlayerCatalog}/{key}.asset");
            Assert.IsNotNull(def, $"PlayerDefinition '{key}.asset' should exist.");
            return def;
        }

        private static CastVariant ClaimantOf(PlayerDefinition def, string spell)
        {
            CastVariant claimed = null;
            foreach (CastVariant v in def.assetConfig.castVariants)
            {
                if (v != null && v.ClaimsSpell(spell)) claimed = v;
            }
            return claimed;
        }

        [TestCase("fireball",     "spell_3")]
        [TestCase("healing_aura", "spell_2")]
        public void Dwarf_ReservesTheAuthoredVariant(string spell, string variantKey)
        {
            var def = Load("dwarf");
            Assert.IsNotNull(def.assetConfig, "dwarf assetConfig must not be null.");
            Assert.IsNotNull(def.assetConfig.castVariants, "dwarf must carry cast variants.");

            CastVariant claimed = ClaimantOf(def, spell);

            Assert.IsNotNull(claimed,
                $"No dwarf cast variant claims '{spell}'. Without the claim the spell takes " +
                "whatever the rotation hands it, which is the behaviour this exists to replace.");
            Assert.AreEqual(variantKey, claimed.key,
                $"'{spell}' is authored against {variantKey}.");
            Assert.IsTrue(claimed.IsReservedForSpell,
                "A variant that claims a spell must also report itself reserved, or the " +
                "rotation keeps handing it out to every other spell.");
        }

        [TestCase("fireball",     "spell_3")]
        [TestCase("healing_aura", "spell_2")]
        public void Dwarf_ReservedVariant_CarriesAFullEightDirectionSheetList(
            string spell, string variantKey)
        {
            var def = Load("dwarf");
            CastVariant claimed = ClaimantOf(def, spell);
            Assert.IsNotNull(claimed, $"'{spell}' should be claimed by {variantKey}.");

            Assert.IsNotNull(claimed.sheets, "The reserved variant needs frames to play.");
            Assert.Greater(claimed.sheets.Count, 0);
            Assert.AreEqual(0, claimed.sheets.Count % 8,
                "A linear sheet list is eight contiguous per-direction buckets, so its length " +
                "is a multiple of 8. A ragged list renders a different frame count per facing " +
                $"— got {claimed.sheets.Count}.");
            Assert.Greater(claimed.sheets.Count / 8, 1,
                "A single frame per direction cannot show 'the full movement': the whole point " +
                "of reserving this variant is that all of its frames play.");

            foreach (Sprite s in claimed.sheets)
                Assert.IsNotNull(s, "A null hole inside a variant is a blank frame mid-cast.");
        }

        [Test]
        public void NoCharacter_ClaimsTheSameSpellTwice()
        {
            foreach (string key in Players)
            {
                var def = AssetDatabase.LoadAssetAtPath<PlayerDefinition>($"{PlayerCatalog}/{key}.asset");
                if (def?.assetConfig?.castVariants == null) continue;

                var seen = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (CastVariant v in def.assetConfig.castVariants)
                {
                    if (v?.spellKeys == null) continue;
                    foreach (string spell in v.spellKeys)
                    {
                        if (string.IsNullOrEmpty(spell)) continue;
                        seen.TryGetValue(spell, out string firstClaimant);
                        Assert.IsFalse(seen.ContainsKey(spell),
                            $"'{key}' has two cast variants claiming '{spell}' " +
                            $"('{firstClaimant}' and '{v.key}'). The lookup takes the first " +
                            "match, so the second is unreachable art.");
                        seen[spell] = v.key;
                    }
                }
            }
        }

        [Test]
        public void EveryReservedSpellKey_NamesASpellThatExists()
        {
            var known = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:SpellDefinition", new[] { SpellCatalogRoot }))
            {
                var spell = AssetDatabase.LoadAssetAtPath<SpellDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (spell != null && !string.IsNullOrEmpty(spell.spellKey))
                    known.Add(spell.spellKey);
            }
            if (known.Count == 0)
                Assert.Ignore($"No SpellDefinition assets under {SpellCatalogRoot} — nothing to check against.");

            var unknown = new List<string>();
            foreach (string key in Players)
            {
                var def = AssetDatabase.LoadAssetAtPath<PlayerDefinition>($"{PlayerCatalog}/{key}.asset");
                if (def?.assetConfig?.castVariants == null) continue;

                foreach (CastVariant v in def.assetConfig.castVariants)
                {
                    if (v?.spellKeys == null) continue;
                    foreach (string spell in v.spellKeys)
                    {
                        if (!string.IsNullOrEmpty(spell) && !known.Contains(spell))
                            unknown.Add($"{key}.{v.key} -> '{spell}'");
                    }
                }
            }

            Assert.IsEmpty(unknown,
                "A reservation naming a spell that does not exist is dead: the spell never " +
                "casts, so the animation never plays and the variant sits out of the rotation " +
                "for nothing.\n" + string.Join("\n", unknown));
        }
    }
}
