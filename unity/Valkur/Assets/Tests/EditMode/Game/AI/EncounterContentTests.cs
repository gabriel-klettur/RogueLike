using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Spawners;

namespace Valkur.Tests.EditMode.Game.AI
{
    /// <summary>
    /// The encounters themselves, and the difficulty layer that sizes them.
    ///
    /// <para><b>WHY THIS FIXTURE EXISTS.</b> The AI layer is 8,000 lines and it was driven, in
    /// normal play, by nothing at all: the shipped world held SEVEN placed spawners, six of them
    /// vendor respawns and the seventh a template whose wave list was empty. Every monster in
    /// the last audit had to be summoned from the console. An AI nobody meets is not an AI, and
    /// the failure is invisible from the code — every system is present, correct and
    /// unreachable.</para>
    ///
    /// <para>So this asserts the CONTENT, not the machinery: that hostile encounters exist, that
    /// each one names monsters and a template that really are in the catalogs, that its tiles
    /// are inside the zone they claim (the coordinate-space drift this project has already been
    /// bitten by), and that the difficulty knobs resolve to something other than "unchanged".</para>
    /// </summary>
    public class EncounterContentTests
    {
        private const string InstancesPath = "StreamingAssets/Spawners/spawners_instances.json";
        private const string TemplateDir = "Assets/_Project/Data/Catalogs/Spawners";
        private const int ZoneTiles = 50;

        private static string AssetsRoot => Path.GetDirectoryName(Application.dataPath) + "/Assets";

        private static List<SpawnerTemplateData> _templates;
        private static List<Dictionary<string, object>> _placements;

        [OneTimeSetUp]
        public void LoadOnce()
        {
            _templates = AssetDatabase.FindAssets("t:SpawnerTemplateData", new[] { TemplateDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<SpawnerTemplateData>)
                .Where(t => t != null)
                .ToList();

            _placements = new List<Dictionary<string, object>>();
            string json = File.ReadAllText(Path.Combine(AssetsRoot, InstancesPath));
            if (Valkur.Gameplay.World.MiniJsonRuntime.Deserialize(json) is List<object> rows)
                foreach (var row in rows)
                    if (row is Dictionary<string, object> d) _placements.Add(d);
        }

        private static string Str(Dictionary<string, object> d, string key)
            => d.TryGetValue(key, out var v) && v != null ? v.ToString() : "";

        private static bool IsVendorRespawn(Dictionary<string, object> d)
            => Str(d, "template_id").StartsWith("vendor_");

        private static SpawnerTemplateData Template(string id)
            => _templates.FirstOrDefault(t => t != null && t.templateId == id);

        /// <summary>Every monster key a template can produce.</summary>
        private static IEnumerable<string> MonsterKeysOf(SpawnerTemplateData t)
        {
            if (t?.waves == null) yield break;
            foreach (var wave in t.waves)
            {
                if (wave?.spawns == null) continue;
                foreach (var spawn in wave.spawns)
                    if (spawn != null && spawn.kind == "monster" && !string.IsNullOrEmpty(spawn.entityId))
                        yield return spawn.entityId;
            }
        }

        // ── There are fights ─────────────────────────────────────────────────────

        [Test]
        public void TheWorldContainsHostileEncounters()
        {
            int hostile = _placements.Count(p => !IsVendorRespawn(p));
            Assert.That(hostile, Is.GreaterThanOrEqualTo(8),
                "The shipped world once held one non-vendor spawner and its wave list was " +
                "empty, so nothing in normal play ever drove the AI. This is the floor under " +
                "that: an AI layer with no encounters is unreachable content, not a feature.");
        }

        [Test]
        public void NoHostileTemplateHasAnEmptyWaveList()
        {
            var placedIds = _placements.Where(p => !IsVendorRespawn(p))
                                       .Select(p => Str(p, "template_id")).Distinct();
            foreach (var id in placedIds)
            {
                var t = Template(id);
                Assert.IsNotNull(t, $"Placement references template '{id}', which does not exist.");
                Assert.That(t.waves, Is.Not.Null.And.Not.Empty,
                    $"'{id}' is placed in the world and spawns nothing. That is exactly how " +
                    "survival_10 shipped: a real spawner, correctly wired, producing no monster.");
                Assert.That(MonsterKeysOf(t).Any(), Is.True,
                    $"'{id}' has waves but no monster entries.");
            }
        }

        [Test]
        public void EveryPlacementNamesATemplateThatExists()
        {
            foreach (var p in _placements)
                Assert.IsNotNull(Template(Str(p, "template_id")),
                    $"Placement '{Str(p, "id")}' names template '{Str(p, "template_id")}', " +
                    "which is not in the catalog — the spawner loads and produces nothing, " +
                    "with one warning nobody is watching for.");
        }

        [Test]
        public void EveryWaveNamesAMonsterThatExists()
        {
            var known = AssetDatabase.FindAssets("t:MonsterDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<MonsterDefinition>)
                .Where(m => m != null)
                .Select(m => m.monsterKey)
                .ToHashSet();

            foreach (var t in _templates)
                foreach (var key in MonsterKeysOf(t))
                    Assert.IsTrue(known.Contains(key),
                        $"Template '{t.templateId}' spawns '{key}', which is not a monsterKey " +
                        "in any shipped definition.");
        }

        [Test]
        public void EveryPlacementIsInsideTheZoneItClaims()
        {
            // The coordinate space is the thing this project has already lost months to:
            // spawners saved absolute world coordinates into a zone-relative field and came back
            // 150 tiles away, once per restart. A tile outside 0..49 is that bug, visible.
            foreach (var p in _placements)
            {
                if (!p.TryGetValue("tile", out var raw) || !(raw is List<object> tile) || tile.Count < 2)
                    Assert.Fail($"Placement '{Str(p, "id")}' has no usable tile.");

                var list = (List<object>)p["tile"];
                int col = System.Convert.ToInt32(list[0]);
                int row = System.Convert.ToInt32(list[1]);

                Assert.IsTrue(SpawnerTileMapping.IsInsideZone(col, row, ZoneTiles, ZoneTiles),
                    $"Placement '{Str(p, "id")}' sits at tile ({col},{row}) in a " +
                    $"{ZoneTiles}x{ZoneTiles} zone — that is data already written in the wrong " +
                    "coordinate space.");
            }
        }

        [Test]
        public void NoPlacementIdIsUsedTwice()
        {
            var seen = new HashSet<string>();
            foreach (var p in _placements)
            {
                string id = Str(p, "id");
                Assert.IsTrue(seen.Add(id), $"Duplicate placement id '{id}'.");
            }
        }

        [Test]
        public void HostileEncountersUseMoreThanOneMonsterKind()
        {
            // A caster behind two melee is the composition desiredRange, the aggro shout, the
            // threat table and the engagement ring were all built for. Packs of one kind exercise
            // none of it.
            int mixed = _templates.Count(t =>
                t.waves != null && MonsterKeysOf(t).Distinct().Count() > 1);

            Assert.That(mixed, Is.GreaterThanOrEqualTo(5),
                "Most of the tactical layer only does anything when a pack is heterogeneous.");
        }

        // ── Difficulty ───────────────────────────────────────────────────────────

        [Test]
        public void DifficultyIsNeutralUnlessAuthored()
        {
            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            try
            {
                def.level = 1;
                Assert.AreEqual(1, EncounterDifficulty.ResolveLevel(def, 0, 0f),
                    "Every spawner shipped before the difficulty layer authors 0 and 0, and must " +
                    "resolve to exactly what it always did.");
            }
            finally { Object.DestroyImmediate(def); }
        }

        [Test]
        public void APlaceBonusRaisesTheLevel()
        {
            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            try
            {
                def.level = 1;
                Assert.AreEqual(6, EncounterDifficulty.ResolveLevel(def, 5, 0f));
            }
            finally { Object.DestroyImmediate(def); }
        }

        [Test]
        public void TheLevelIsClamped()
        {
            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            try
            {
                def.level = 1;
                Assert.AreEqual(EncounterDifficulty.MaxLevel,
                    EncounterDifficulty.ResolveLevel(def, 9999, 0f),
                    "HP growth is summed per level, so an unbounded level is an unbounded loop " +
                    "as well as unbounded balance.");
            }
            finally { Object.DestroyImmediate(def); }
        }

        [Test]
        public void EveryHostileActuallyGrowsWithLevel()
        {
            // The plumbing was all present before and resolved to "unchanged" on every spawn,
            // because no asset authored growth. A difficulty system one authored number away
            // from existing is indistinguishable from none.
            var hostiles = AssetDatabase.FindAssets("t:MonsterDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<MonsterDefinition>)
                .Where(m => m != null && m.stats.faction == "EVIL")
                .ToList();

            Assert.IsNotEmpty(hostiles);
            foreach (var m in hostiles)
            {
                var baseline = m.GetScaledStats(1);
                var levelled = m.GetScaledStats(5);
                Assert.That(levelled.hp, Is.GreaterThan(baseline.hp),
                    $"{m.monsterKey} ignores level entirely — neither levelScaling nor " +
                    "levelHpGrowth is authored, so every encounter difficulty resolves to the " +
                    "same monster.");
            }
        }

        [Test]
        public void NeutralsAreNotScaled()
        {
            var neutrals = AssetDatabase.FindAssets("t:MonsterDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<MonsterDefinition>)
                .Where(m => m != null && m.stats.faction == "NEUTRAL")
                .ToList();

            foreach (var m in neutrals)
                Assert.AreEqual(m.GetScaledStats(1).hp, m.GetScaledStats(10).hp,
                    $"{m.monsterKey} is a vendor or a villager. Levelling one means nothing and " +
                    "would only make it harder to kill by accident.");
        }

        [Test]
        public void ProportionalGrowthWorksAtBothEndsOfTheRoster()
        {
            // The reason growth is proportional rather than a shared absolute curve: the
            // bestiary spans 10 hp to 10,000, and one authored hpPerLevel cannot serve both —
            // it nearly triples the smallest and is a rounding error on the largest.
            var all = AssetDatabase.FindAssets("t:MonsterDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<MonsterDefinition>)
                .Where(m => m != null && m.stats.faction == "EVIL" && m.stats.hp > 0)
                .ToList();

            foreach (var m in all)
            {
                float ratio = m.GetScaledStats(5).hp / (float)m.GetScaledStats(1).hp;
                Assert.That(ratio, Is.GreaterThan(1.05f).And.LessThan(3f),
                    $"{m.monsterKey} grows {ratio:0.00}x from level 1 to 5 — outside the band " +
                    "that keeps a levelled monster recognisably the same monster.");
            }
        }
    }
}
