using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// The shipped roster's repeat stretches (<see cref="CastVariant.repeatFrom"/> +
    /// <see cref="CastVariant.repeatFrameCount"/>), read off the assets on disk.
    ///
    /// <para>A stretch is what a held spell loops instead of cycling the whole cast through its
    /// rest pose. It is authored per animation by looking at the frames, so nothing enforces it
    /// at import time: a new character wave arrives with every stretch at zero and plays exactly
    /// like the defect this fixes, with nothing failing. These tests are the thing that fails.</para>
    ///
    /// <para>Two rules, both structural rather than about any particular number: every
    /// SPELLCASTING animation authors a stretch, and a stretch fits inside the frames it names.
    /// A third keeps two mechanisms apart — a variant that ends in a held pose (the dash) cannot
    /// also repeat, because the repeat decides what the tail does and the hold would be ignored.</para>
    /// </summary>
    public class PlayerCastRepeatDataTests
    {
        private const string PlayersFolder = "Assets/_Project/Data/Catalogs/Players";

        /// <summary>Key prefixes of the animations that ARE spellcasts — what a player repeats.
        /// Equips, charges, slashes and summons are one-off gestures and are deliberately absent.</summary>
        private static readonly string[] SpellcastPrefixes = { "spell_", "staff_cast_", "bard_" };

        private static IEnumerable<(string owner, CastVariant variant)> AllCastVariants()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:PlayerDefinition", new[] { PlayersFolder }))
            {
                var def = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (def?.assetConfig == null) continue;

                if (def.assetConfig.castVariants != null)
                    foreach (var v in def.assetConfig.castVariants)
                        if (v != null) yield return (def.name, v);

                if (def.assetConfig.loadouts == null) continue;
                foreach (var loadout in def.assetConfig.loadouts)
                {
                    if (loadout?.castVariants == null) continue;
                    foreach (var v in loadout.castVariants)
                        if (v != null) yield return ($"{def.name}[{loadout.key}]", v);
                }
            }
        }

        private static bool IsSpellcast(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            foreach (string prefix in SpellcastPrefixes)
                if (key.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        [Test]
        public void EverySpellcastAnimation_AuthorsARepeatStretch()
        {
            var missing = new List<string>();
            int inspected = 0;
            foreach ((string owner, CastVariant v) in AllCastVariants())
            {
                if (!IsSpellcast(v.key)) continue;
                inspected++;
                if (!v.HasRepeat) missing.Add($"{owner}.{v.key}");
            }

            Assert.That(inspected, Is.GreaterThan(0), "no spellcast variants found — the fixture is reading nothing");
            Assert.IsEmpty(missing,
                "these spellcasts have no repeat stretch, so a held spell cycles them through their " +
                "rest pose between shots. Look at the frames and set repeatFrom/repeatFrameCount on " +
                "the climax of the gesture:\n" + string.Join("\n", missing));
        }

        [Test]
        public void EveryStretch_FitsInsideItsOwnFrames()
        {
            var problems = new List<string>();
            foreach ((string owner, CastVariant v) in AllCastVariants())
            {
                if (!v.HasRepeat) continue;
                int perDirection = v.sheets != null ? v.sheets.Count / 8 : 0;
                if (perDirection <= 0)
                    problems.Add($"{owner}.{v.key}: a stretch on a variant with no linear frames");
                else if (v.repeatFrom < 0 || v.repeatFrom + v.repeatFrameCount > perDirection)
                    problems.Add($"{owner}.{v.key}: frames {v.repeatFrom}..{v.repeatFrom + v.repeatFrameCount - 1} " +
                                 $"of {perDirection}");
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void AHeldPose_NeverAlsoRepeats()
        {
            var problems = new List<string>();
            foreach ((string owner, CastVariant v) in AllCastVariants())
                if (v.HasRepeat && v.holdLastFrame)
                    problems.Add($"{owner}.{v.key}");

            Assert.IsEmpty(problems,
                "holdLastFrame and a repeat stretch both decide what the end of the animation does, " +
                "and the repeat wins silently:\n" + string.Join("\n", problems));
        }
    }
}
