using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Gameplay.Spells.Core
{
    /// <summary>
    /// Guards the per-caster melee reach scalar.
    ///
    /// <para>A slash's radius comes off its <see cref="SpellDefinition"/>, which knows nothing
    /// about who is casting, so every playable class swung <c>slash_regular</c> at the same
    /// 2.6 units however big it was. <see cref="MeleeReachScale"/> multiplies that by the
    /// caster's own <c>MeleeRange</c> stat against the 1.5 baseline every class authors.</para>
    ///
    /// <para>The dangerous half is what it must NOT touch. Monsters already express reach by
    /// authoring a separate slash asset, and their <c>meleeRange</c> runs 0 to 7 — so applying
    /// the scalar to them would swing the seven vendors at radius zero and take
    /// <c>boss_barbol_slash</c> from 6.5 to 30.3 units. The neutral cases are therefore
    /// asserted harder here than the scaling one.</para>
    /// </summary>
    public class MeleeReachScaleTests
    {
        private const string PlayerCatalog = "Assets/_Project/Data/Catalogs/Players";
        private const string SpellCatalog = "Assets/_Project/Data/Catalogs/Spells";

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private GameObject Caster(string tag, float? reach)
        {
            var go = new GameObject("reach_probe");
            _spawned.Add(go);
            if (tag != null) go.tag = tag;
            if (reach.HasValue) go.AddComponent<MeleeCombat>().SetRange(reach.Value);
            return go;
        }

        // ── The neutral cases: everything that is not a tuned player ─────────

        /// <summary>
        /// The one that matters most. A monster's reach is already baked into its own slash
        /// asset, so scaling again double-counts — and the shipped spread makes that fatal
        /// rather than subtle.
        /// </summary>
        [TestCase(0f, TestName = "Untagged caster at a vendor's reach 0 stays neutral")]
        [TestCase(1.8f, TestName = "Untagged caster at mon1's reach 1.8 stays neutral")]
        [TestCase(3f, TestName = "Untagged caster at a barbol's reach 3 stays neutral")]
        [TestCase(7f, TestName = "Untagged caster at barbol_boss's reach 7 stays neutral")]
        public void NonPlayerCaster_IsAlwaysNeutral(float reach)
        {
            Assert.That(MeleeReachScale.For(Caster(null, reach).transform), Is.EqualTo(1f),
                $"A caster without the Player tag must scale by exactly 1 whatever its reach " +
                $"({reach}). Monsters author their reach in their own slash asset " +
                "(hostile_slash 2.4, hostile_slash_giant 5.0, boss_barbol_slash 6.5); scaling " +
                "that by meleeRange/1.5 applies the same quantity twice. At reach 7 it would " +
                $"take boss_barbol_slash to {6.5f * (7f / 1.5f):0.0} units.");
        }

        [Test]
        public void NullCaster_IsNeutral()
        {
            Assert.That(MeleeReachScale.For(null), Is.EqualTo(1f));
        }

        [Test]
        public void PlayerWithoutMeleeCombat_IsNeutral()
        {
            // PlayerStats seeds MeleeCombat on its own bootstrap order, so a player really can
            // be observed before the component exists. Neutral, never zero.
            Assert.That(MeleeReachScale.For(Caster("Player", null).transform), Is.EqualTo(1f));
        }

        [Test]
        public void PlayerWithUnseededReach_IsNeutral()
        {
            var go = Caster("Player", 1f);
            // Force the unseeded state SetRange's own clamp would otherwise prevent.
            typeof(MeleeCombat)
                .GetField("range", System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)
                .SetValue(go.GetComponent<MeleeCombat>(), 0f);

            Assert.That(MeleeReachScale.For(go.transform), Is.EqualTo(1f),
                "A non-positive reach is a component that has not been seeded, not a request " +
                "for a zero-radius swing.");
        }

        /// <summary>
        /// The whole shipped roster sits on the baseline, so this change must move nothing
        /// that already existed. If this fails, every melee class silently changed reach.
        /// </summary>
        [Test]
        public void PlayerAtTheBaseline_IsNeutral()
        {
            Assert.That(MeleeReachScale.For(Caster("Player", MeleeReachScale.BaselineReach).transform),
                Is.EqualTo(1f).Within(1e-5f));
        }

        // ── The scaling case ────────────────────────────────────────────────

        [Test]
        public void PlayerAboveTheBaseline_ScalesProportionally()
        {
            float scale = MeleeReachScale.For(Caster("Player", 2.16f).transform);
            Assert.That(scale, Is.EqualTo(2.16f / 1.5f).Within(1e-5f));
            Assert.That(2.6f * scale, Is.EqualTo(3.744f).Within(1e-3f),
                "slash_regular's authored 2.6 must resolve to 3.744 for a caster at 2.16.");
        }

        // ── Composition against the shipped data ────────────────────────────

        /// <summary>
        /// The three constants that must agree, and the reason: a class authoring no
        /// <c>meleeRange</c> falls back to <c>PlayerStats</c>'s literal, and if that differed
        /// from the baseline the fallback itself would scale the swing.
        /// </summary>
        [Test]
        public void BaselineReach_MatchesThePlayerDefinitionDefault()
        {
            Assert.That(ScriptableObject.CreateInstance<PlayerDefinition>().meleeRange,
                Is.EqualTo(MeleeReachScale.BaselineReach).Within(1e-5f),
                "PlayerDefinition.meleeRange's default and MeleeReachScale.BaselineReach are " +
                "the same number in two files. A class that authors nothing would otherwise " +
                "silently scale.");
        }

        /// <summary>
        /// Walks the shipped classes rather than naming them, so a class added later is
        /// covered without editing this. Deliberately asserts the RESOLVED reach, not the
        /// authored field — that composition is the thing that can be wrong while both halves
        /// read correctly.
        /// </summary>
        [Test]
        public void EveryShippedMeleeClass_ResolvesAReachInAPlayableBand()
        {
            var slash = AssetDatabase.LoadAssetAtPath<SpellDefinition>($"{SpellCatalog}/slash_regular.asset");
            Assert.IsNotNull(slash, "slash_regular.asset is the player's default swing.");

            var report = new List<string>();
            var failures = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:PlayerDefinition", new[] { PlayerCatalog }))
            {
                var def = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (def == null || def.meleeRange <= 0f) continue;   // mague/valkyrie author none

                float resolved = slash.hitRadius * (def.meleeRange / MeleeReachScale.BaselineReach);
                report.Add($"  {def.playerKey}: meleeRange {def.meleeRange} -> {resolved:0.000} u");

                // A swing shorter than the authored radius means somebody dropped a class
                // BELOW the baseline, which shortens a reach the whole catalogue was tuned at.
                if (resolved < slash.hitRadius - 1e-4f)
                    failures.Add($"  {def.playerKey} resolves {resolved:0.000} u, under the " +
                                 $"authored {slash.hitRadius} — meleeRange {def.meleeRange} is " +
                                 $"below the {MeleeReachScale.BaselineReach} baseline.");

                // And an upper bound, because reach is quadratic in swept area: 2x the
                // baseline is 4x the area, which stops being a character trait.
                if (def.meleeRange > MeleeReachScale.BaselineReach * 2f)
                    failures.Add($"  {def.playerKey} authors meleeRange {def.meleeRange}, over " +
                                 $"twice the {MeleeReachScale.BaselineReach} baseline — that is " +
                                 "4x the swept area, not a size difference.");
            }

            Assert.IsNotEmpty(report, "No shipped class authors a meleeRange, so this asserted nothing.");
            Assert.That(failures, Is.Empty,
                string.Join("\n", failures) + "\n\nResolved reaches:\n" + string.Join("\n", report));
        }

        // ── The wiring ──────────────────────────────────────────────────────

        /// <summary>
        /// A correct helper nobody calls is the authored-and-inert shape this project has
        /// shipped a dozen times — and it is exactly what <c>StatKind.MeleeRange</c> WAS
        /// before this change: a stat with a display name, a description and 0.2 units per
        /// point, whose only reader for a player was a debug overlay. The scan is the cheap
        /// half of not repeating that.
        /// </summary>
        [Test]
        public void SlashExecutor_AppliesTheScale()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath,
                "_Project/Scripts/Gameplay/Spells/Executors/SlashExecutor.cs"));
            Assert.IsTrue(File.Exists(path), $"SlashExecutor.cs not found at '{path}'.");

            string src = File.ReadAllText(path);
            Assert.IsTrue(Regex.IsMatch(src, @"hitRadius\s*\*=\s*MeleeReachScale\.For\("),
                "SlashExecutor must scale hitRadius by MeleeReachScale.For(ctx.Caster). " +
                "Without that call the helper is correct and unwired, and StatKind.MeleeRange " +
                "goes back to reaching nothing but CombatRangeVisualizer.");

            // Ordering: the scale has to land before the arc is spawned, or the damage sweep
            // and the drawn edge take different radii.
            int scaled = src.IndexOf("hitRadius *= MeleeReachScale.For(", System.StringComparison.Ordinal);
            int spawned = src.IndexOf("RegularSlashAttack.Spawn(", System.StringComparison.Ordinal);
            Assert.That(scaled, Is.GreaterThan(-1).And.LessThan(spawned),
                "The scale must be applied before the slash is spawned — SlashAttack derives " +
                "its sweep from the radius it is handed, so a late multiply would leave the " +
                "drawn arc and the damaged arc at different sizes.");
        }
    }
}
