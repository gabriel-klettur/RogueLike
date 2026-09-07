using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spawners;

namespace Valkur.Tests.EditMode.Game.Spawners
{
    /// <summary>
    /// The shipped rosters, checked against the shipped catalogues.
    ///
    /// <para><c>WaveSpawnEntry.entityId</c> is a plain string and nothing validated it, so a
    /// typo cost exactly one console warning at spawn time and a camp that never filled —
    /// invisible until somebody walked to that clearing. The roster picker makes the illegal
    /// value unrepresentable going forward; this covers the data already on disk, which no
    /// picker can reach.</para>
    ///
    /// <para><c>SpawnerFileIntegrityTests</c> is the sibling that checks WHERE a spawner is
    /// (zone membership, round trip, tile collisions). This one checks WHAT it holds.</para>
    /// </summary>
    [TestFixture]
    public class ShippedSpawnerRosterTests
    {
        private const string CatalogPath =
            "Assets/_Project/Data/Catalogs/Spawners/SpawnerTemplateCatalog.asset";
        private const string MonstersPath =
            "Assets/_Project/Data/Catalogs/Monsters/MonsterCatalog.asset";
        private const string InstancesPath =
            "Assets/StreamingAssets/Spawners/spawners_instances.json";

        private SpawnerTemplateCatalog _catalog;
        private HashSet<string> _monsterKeys;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<SpawnerTemplateCatalog>(CatalogPath);

            var monsters = AssetDatabase.LoadAssetAtPath<MonsterCatalog>(MonstersPath);
            _monsterKeys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            if (monsters?.Definitions != null)
            {
                foreach (var def in monsters.Definitions)
                    if (def != null && !string.IsNullOrEmpty(def.monsterKey))
                        _monsterKeys.Add(def.monsterKey);
            }
        }

        /// <summary>
        /// Guards every assertion below against passing vacuously. A fixture that walks an
        /// empty list is worse than an absent one: it reports coverage it does not have, which
        /// is what the two PPU invariants did before they were caught.
        /// </summary>
        [Test]
        public void TheCataloguesLoad()
        {
            Assert.That(_catalog, Is.Not.Null, $"No SpawnerTemplateCatalog at '{CatalogPath}'.");
            Assert.That(_catalog.Templates.Count, Is.GreaterThan(0));
            Assert.That(_monsterKeys.Count, Is.GreaterThan(0), $"No monsters loaded from '{MonstersPath}'.");
        }

        [Test]
        public void EveryPresetRosterEntry_NamesSomethingThatExists()
        {
            Assert.That(_catalog, Is.Not.Null);

            var broken = new List<string>();
            int inspected = 0;

            foreach (var preset in _catalog.Templates)
            {
                if (preset?.waves == null) continue;

                for (int w = 0; w < preset.waves.Count; w++)
                {
                    var wave = preset.waves[w];
                    if (wave?.spawns == null) continue;

                    foreach (var entry in wave.spawns)
                    {
                        if (entry == null) continue;
                        inspected++;

                        if (string.IsNullOrEmpty(entry.entityId))
                        {
                            broken.Add($"{preset.templateId} wave {w}: empty entityId");
                            continue;
                        }

                        // A building entry carries a numeric BuildingTemplateData id, which
                        // is not in the monster catalogue. Checking it against the wrong list
                        // is how a fish shoal gets reported as a missing monster.
                        if (string.Equals(entry.kind, "building", System.StringComparison.OrdinalIgnoreCase))
                        {
                            if (!int.TryParse(entry.entityId, out _))
                                broken.Add($"{preset.templateId} wave {w}: kind=building needs a " +
                                           $"numeric template id, got '{entry.entityId}'");
                            continue;
                        }

                        if (!_monsterKeys.Contains(entry.entityId))
                            broken.Add($"{preset.templateId} wave {w}: '{entry.entityId}' is in no " +
                                       "MonsterCatalog entry");
                    }
                }
            }

            Assert.That(inspected, Is.GreaterThan(0), "No roster entries inspected.");
            Assert.That(broken, Is.Empty,
                "These roster entries name something that does not exist. Each is a camp that " +
                "spawns nothing and warns once:\n  " + string.Join("\n  ", broken));
        }

        [Test]
        public void EveryPresetRosterEntry_SpawnsAPositiveCount()
        {
            Assert.That(_catalog, Is.Not.Null);

            var broken = (from preset in _catalog.Templates
                          where preset?.waves != null
                          from wave in preset.waves
                          where wave?.spawns != null
                          from entry in wave.spawns
                          where entry != null && entry.count <= 0
                          select $"{preset.templateId}: '{entry.entityId}' count={entry.count}")
                         .ToList();

            Assert.That(broken, Is.Empty,
                "A count of zero is an entry that costs a cooldown tick and produces nothing:\n  " +
                string.Join("\n  ", broken));
        }

        /// <summary>
        /// A spawner with an empty roster sits in Active forever producing nothing. It is a
        /// live defect rather than a state, and it is the one `survival_10` shipped in for
        /// months while pointing at a wave table that was never built.
        /// </summary>
        [Test]
        public void NoPreset_ShipsWithAnEmptyRoster()
        {
            Assert.That(_catalog, Is.Not.Null);

            var empty = _catalog.Templates
                .Where(p => p != null)
                .Where(p => p.waves == null || p.waves.Count == 0 ||
                            p.waves.All(w => w?.spawns == null || w.spawns.Count == 0))
                .Select(p => p.templateId)
                .ToList();

            Assert.That(empty, Is.Empty,
                "These presets produce nothing at all: " + string.Join(", ", empty));
        }

        /// <summary>
        /// <c>persistent</c> exempts every entity a spawner produces from the distance-based
        /// despawn sweep. It is a live exemption, not a convenience, and it became easier to
        /// set when it moved per-placement — so the shipped set is worth pinning. Today it is
        /// exactly the six vendor respawns: a banker must not evaporate when the player walks
        /// away, and nothing else has that claim.
        /// </summary>
        [Test]
        public void OnlyVendorPresets_CarryTheDespawnExemption()
        {
            Assert.That(_catalog, Is.Not.Null);

            var unexpected = _catalog.Templates
                .Where(p => p != null && p.persistent)
                .Select(p => p.templateId)
                .Where(id => id == null || id.IndexOf("vendor", System.StringComparison.OrdinalIgnoreCase) < 0)
                .ToList();

            Assert.That(unexpected, Is.Empty,
                "A world of exempt monsters is a leak with no error. If one of these really " +
                "should never despawn, say so here: " + string.Join(", ", unexpected));
        }

        // ── The instances file ──────────────────────────────────────────────────

        [Test]
        public void TheShippedInstancesFile_Parses()
        {
            string absolute = Path.Combine(Directory.GetCurrentDirectory(), InstancesPath);
            Assert.That(File.Exists(absolute), $"No instances file at '{InstancesPath}'.");

            var records = SpawnerInstanceSerializer.ParseAll(File.ReadAllText(absolute));

            Assert.That(records, Is.Not.Null, "The shipped instances file does not parse.");
            Assert.That(records.Count, Is.GreaterThan(0));
        }

        [Test]
        public void EveryShippedInstance_NamesAPresetThatExists()
        {
            Assert.That(_catalog, Is.Not.Null);

            string absolute = Path.Combine(Directory.GetCurrentDirectory(), InstancesPath);
            var records = SpawnerInstanceSerializer.ParseAll(File.ReadAllText(absolute));
            Assert.That(records, Is.Not.Null);

            var broken = records
                .Where(r => r != null && _catalog.GetById(r.TemplateId) == null)
                .Select(r => $"{r.InstanceId} -> '{r.TemplateId}'")
                .ToList();

            Assert.That(broken, Is.Empty,
                "These placements point at a preset the catalogue does not hold. Once a row " +
                "owns a config that is survivable, but a v1 row is refused outright:\n  " +
                string.Join("\n  ", broken));
        }

        /// <summary>
        /// Every roster entry a shipped PLACEMENT carries, once the file is v2. Presets and
        /// placements diverge by design after copy-on-place, so validating one says nothing
        /// about the other — and the placement is the half that actually runs.
        /// </summary>
        [Test]
        public void EveryShippedInstanceRosterEntry_NamesSomethingThatExists()
        {
            string absolute = Path.Combine(Directory.GetCurrentDirectory(), InstancesPath);
            var records = SpawnerInstanceSerializer.ParseAll(File.ReadAllText(absolute));
            Assert.That(records, Is.Not.Null);

            var broken = new List<string>();
            int inspected = 0;

            foreach (var record in records)
            {
                if (record?.Config?.waves == null) continue;

                foreach (var wave in record.Config.waves)
                {
                    if (wave?.spawns == null) continue;
                    foreach (var entry in wave.spawns)
                    {
                        if (entry == null || string.IsNullOrEmpty(entry.entityId)) continue;
                        inspected++;

                        if (string.Equals(entry.kind, "building", System.StringComparison.OrdinalIgnoreCase))
                        {
                            if (!int.TryParse(entry.entityId, out _))
                                broken.Add($"{record.InstanceId}: building id '{entry.entityId}'");
                            continue;
                        }

                        if (!_monsterKeys.Contains(entry.entityId))
                            broken.Add($"{record.InstanceId}: '{entry.entityId}'");
                    }
                }
            }

            // Deliberately NOT asserting inspected > 0: before the v2 migration runs, no
            // placement carries a roster of its own and zero is the correct answer. Asserting
            // otherwise would make this fixture fail for a reason that has nothing to do with
            // the data it checks.
            Assert.That(broken, Is.Empty,
                $"({inspected} entries inspected) These placed rosters name something that " +
                "does not exist:\n  " + string.Join("\n  ", broken));
        }
    }
}
