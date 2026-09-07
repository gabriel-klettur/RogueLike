using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spawners;

namespace Valkur.Tests.EditMode.Game.Spawners
{
    /// <summary>
    /// Both directions of <c>spawners_instances.json</c>, and the v1 → v2 freeze.
    ///
    /// <para>The fixture asserts the COMPOSITION — write then read — rather than either half,
    /// because a serializer and a parser that are each internally consistent and disagree with
    /// each other is precisely the shape of <c>SPAWNER_COORDINATE_SPACE_DRIFT</c>: the save
    /// wrote absolute world coordinates into a field the loader read as zone-relative, both
    /// halves were correct on their own, and the spawners marched 150 tiles per restart.</para>
    /// </summary>
    [TestFixture]
    public class SpawnerInstanceSerializerTests
    {
        private static SpawnerTemplateData MakePreset(string id)
        {
            var preset = ScriptableObject.CreateInstance<SpawnerTemplateData>();
            preset.templateId = id;
            return preset;
        }

        private static SpawnerInstanceRecord MakeRecord(SpawnerInstanceConfig config)
            => new SpawnerInstanceRecord
            {
                TemplateId = "camp",
                Zone       = "Forest",
                Tile       = new Vector2Int(12, 34),
                InstanceId = "camp_Forest_12_34",
                Config     = config,
                HadConfig  = config != null
            };

        private static SpawnerInstanceRecord RoundTrip(SpawnerInstanceRecord record)
        {
            string json = SpawnerInstanceSerializer.Serialize(new[] { record });
            var parsed = SpawnerInstanceSerializer.ParseAll(json);
            Assert.That(parsed, Is.Not.Null, "The serializer produced JSON its own parser rejects.");
            Assert.That(parsed.Count, Is.EqualTo(1));
            return parsed[0];
        }

        // ── Identity ────────────────────────────────────────────────────────────

        [Test]
        public void Identity_SurvivesTheRoundTrip()
        {
            var back = RoundTrip(MakeRecord(new SpawnerInstanceConfig()));

            Assert.That(back.TemplateId, Is.EqualTo("camp"));
            Assert.That(back.Zone,       Is.EqualTo("Forest"));
            Assert.That(back.Tile,       Is.EqualTo(new Vector2Int(12, 34)));
            Assert.That(back.InstanceId, Is.EqualTo("camp_Forest_12_34"));
        }

        [Test]
        public void EveryConfigField_SurvivesTheRoundTrip()
        {
            // Every value deliberately differs from its default, so a field the writer forgets
            // comes back as the default and fails rather than passing by coincidence.
            var config = new SpawnerInstanceConfig
            {
                triggerType                 = TriggerType.Auto,
                triggerRadius               = 17.5f,
                autoStart                   = false,
                proximityRearms             = true,
                spawnMode                   = SpawnMode.Burst,
                cooldownSeconds             = 2.25f,
                betweenWavesCooldownSeconds = 9.5f,
                advanceOn                   = AdvanceOn.Cooldown,
                maxActive                   = 7,
                persistent                  = true,
                restartOnDone               = true,
                restartCooldownSeconds      = 120f,
                spawnRadius                 = 13,
                spawnerShape                = SpawnerShape.Circle,
                levelBonus                  = 4,
                scaleWithPlayerLevel        = 0.35f,
                defendLeashRadius           = 6.5f,
                fsmSetOverride              = "Monster_Caster",
            };

            var back = RoundTrip(MakeRecord(config)).Config;

            Assert.That(back.triggerType,                 Is.EqualTo(TriggerType.Auto));
            Assert.That(back.triggerRadius,               Is.EqualTo(17.5f).Within(1e-4f));
            Assert.That(back.autoStart,                   Is.False);
            Assert.That(back.proximityRearms,             Is.True);
            Assert.That(back.spawnMode,                   Is.EqualTo(SpawnMode.Burst));
            Assert.That(back.cooldownSeconds,             Is.EqualTo(2.25f).Within(1e-4f));
            Assert.That(back.betweenWavesCooldownSeconds, Is.EqualTo(9.5f).Within(1e-4f));
            Assert.That(back.advanceOn,                   Is.EqualTo(AdvanceOn.Cooldown));
            Assert.That(back.maxActive,                   Is.EqualTo(7));
            Assert.That(back.persistent,                  Is.True);
            Assert.That(back.restartOnDone,               Is.True);
            Assert.That(back.restartCooldownSeconds,      Is.EqualTo(120f).Within(1e-4f));
            Assert.That(back.spawnRadius,                 Is.EqualTo(13));
            Assert.That(back.spawnerShape,                Is.EqualTo(SpawnerShape.Circle));
            Assert.That(back.levelBonus,                  Is.EqualTo(4));
            Assert.That(back.scaleWithPlayerLevel,        Is.EqualTo(0.35f).Within(1e-4f));
            Assert.That(back.defendLeashRadius,           Is.EqualTo(6.5f).Within(1e-4f));
            Assert.That(back.fsmSetOverride,              Is.EqualTo("Monster_Caster"));
        }

        // ── Roster ──────────────────────────────────────────────────────────────

        [Test]
        public void TheRoster_KeepsItsWaveGrouping()
        {
            var config = new SpawnerInstanceConfig
            {
                waves = new List<WaveDefinition>
                {
                    new WaveDefinition { spawns = { new WaveSpawnEntry { entityId = "barbol",  count = 3, spreadRadius = 4f } } },
                    new WaveDefinition { spawns = {
                        new WaveSpawnEntry { entityId = "dark_elven", count = 2, spreadRadius = 5f },
                        new WaveSpawnEntry { kind = "building", entityId = "1382", count = 1, spreadRadius = 3f },
                    } },
                }
            };

            var back = RoundTrip(MakeRecord(config)).Config;

            Assert.That(back.waves.Count, Is.EqualTo(2), "A flat roster list must regroup by wave index.");
            Assert.That(back.waves[0].spawns.Count, Is.EqualTo(1));
            Assert.That(back.waves[1].spawns.Count, Is.EqualTo(2));

            Assert.That(back.waves[0].spawns[0].entityId, Is.EqualTo("barbol"));
            Assert.That(back.waves[0].spawns[0].count,    Is.EqualTo(3));

            // The building dispatch is the only reason `kind` is not decoration.
            Assert.That(back.waves[1].spawns[1].kind,     Is.EqualTo("building"));
            Assert.That(back.waves[1].spawns[1].entityId, Is.EqualTo("1382"));
        }

        /// <summary>
        /// A config with no roster reads back EMPTY, never as the preset's. A config that
        /// exists is complete by construction; falling back to the preset here would be the
        /// live link copy-on-place removes, reintroduced at the one point nobody looks.
        /// </summary>
        [Test]
        public void AnEmptyRoster_ReadsBackEmpty()
        {
            var back = RoundTrip(MakeRecord(new SpawnerInstanceConfig())).Config;

            Assert.That(back.waves, Is.Not.Null);
            Assert.That(back.waves, Is.Empty);
        }

        // ── Defaults are omitted, and against the CLASS default ─────────────────

        [Test]
        public void ADefaultConfig_WritesAlmostNothing()
        {
            string json = SpawnerInstanceSerializer.Serialize(new[] { MakeRecord(new SpawnerInstanceConfig()) });

            foreach (string key in new[] { "triggerRadius", "autoStart", "cooldown",
                                           "betweenWaves", "spawnRadius", "shape", "trigger" })
            {
                Assert.That(json, Does.Not.Contain($"\"{key}\""),
                    $"'{key}' equals the class default and must not be written.");
            }

            Assert.That(json, Does.Contain("\"v\": 2"), "Every config block declares its schema version.");
        }

        /// <summary>
        /// The property the omission rule exists for: a placement frozen against a preset does
        /// not move when that preset is retuned afterwards. Omitting values that merely AGREE
        /// with the preset would break exactly this and nothing else would notice.
        /// </summary>
        [Test]
        public void APlacement_DoesNotFollowItsPresetAfterFreezing()
        {
            var preset = MakePreset("camp");
            preset.cooldownSeconds = 3f;

            try
            {
                var record = new SpawnerInstanceRecord { TemplateId = "camp", Zone = "Forest" };
                var catalog = ScriptableObject.CreateInstance<SpawnerTemplateCatalog>();
                catalog.UpsertTemplate(preset);

                Assert.That(SpawnerInstanceSerializer.Freeze(new[] { record }, catalog), Is.True);
                string json = SpawnerInstanceSerializer.Serialize(new[] { record });

                // The preset moves AFTER the freeze. The placement must not.
                preset.cooldownSeconds = 99f;

                var back = SpawnerInstanceSerializer.ParseAll(json)[0];
                Assert.That(back.Config.cooldownSeconds, Is.EqualTo(3f).Within(1e-4f),
                    "The placement followed its preset — copy-on-place is not holding.");

                Object.DestroyImmediate(catalog);
            }
            finally
            {
                Object.DestroyImmediate(preset);
            }
        }

        // ── Freeze ──────────────────────────────────────────────────────────────

        [Test]
        public void Freeze_LeavesARowWhosePresetIsMissing_Alone()
        {
            var record  = new SpawnerInstanceRecord { TemplateId = "gone", Zone = "Forest" };
            var catalog = ScriptableObject.CreateInstance<SpawnerTemplateCatalog>();

            try
            {
                Assert.That(SpawnerInstanceSerializer.Freeze(new[] { record }, catalog), Is.False,
                    "Nothing changed, so no write-back is owed.");
                Assert.That(record.Config, Is.Null,
                    "Freezing against a blank preset would replace an author's data with " +
                    "defaults, silently, on exactly the rows a broken reference already hides.");
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void Freeze_LeavesAnAlreadyMigratedRowUntouched()
        {
            var preset = MakePreset("camp");
            preset.cooldownSeconds = 3f;
            var catalog = ScriptableObject.CreateInstance<SpawnerTemplateCatalog>();
            catalog.UpsertTemplate(preset);

            try
            {
                var record = new SpawnerInstanceRecord
                {
                    TemplateId = "camp",
                    Config     = new SpawnerInstanceConfig { cooldownSeconds = 42f },
                    HadConfig  = true
                };

                Assert.That(SpawnerInstanceSerializer.Freeze(new[] { record }, catalog), Is.False);
                Assert.That(record.Config.cooldownSeconds, Is.EqualTo(42f).Within(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(preset);
            }
        }

        [Test]
        public void SerializedRecords_IncludeRowsThatCouldNotBeFrozen()
        {
            // The migration writes what it READ. A writer that emitted only the rows it
            // understood would drop a placement whose preset is missing — silently, and
            // permanently, since the file is then rewritten without it.
            var records = new[]
            {
                new SpawnerInstanceRecord { TemplateId = "gone",  Zone = "Forest", InstanceId = "orphan" },
                MakeRecord(new SpawnerInstanceConfig()),
            };

            var back = SpawnerInstanceSerializer.ParseAll(SpawnerInstanceSerializer.Serialize(records));

            Assert.That(back.Count, Is.EqualTo(2));
            Assert.That(back.Any(r => r.InstanceId == "orphan"), Is.True);
            Assert.That(back.First(r => r.InstanceId == "orphan").Config, Is.Null,
                "An unfrozen row stays v1 rather than being invented.");
        }

        // ── Robustness ──────────────────────────────────────────────────────────

        [Test]
        public void ParseAll_TellsUnreadableApartFromEmpty()
        {
            // The distinction gates a write. "No spawners" may be written over; "unreadable"
            // must never be, or a stray keystroke in a hand-edited file costs the whole map.
            Assert.That(SpawnerInstanceSerializer.ParseAll(""), Is.Empty);
            Assert.That(SpawnerInstanceSerializer.ParseAll(null), Is.Empty);
            Assert.That(SpawnerInstanceSerializer.ParseAll("{ not an array }"), Is.Null);
        }

        [Test]
        public void AnUnknownEnumName_FallsBackInsteadOfThrowing()
        {
            // A file written by a newer build must degrade, not refuse to load.
            const string json = "[{\"template_id\":\"camp\",\"zone\":\"Forest\",\"tile\":[1,2]," +
                                "\"id\":\"x\",\"config\":{\"v\":2,\"trigger\":\"Telepathy\"}}]";

            var back = SpawnerInstanceSerializer.ParseAll(json);

            Assert.That(back, Is.Not.Null);
            Assert.That(back[0].Config.triggerType, Is.EqualTo(new SpawnerInstanceConfig().triggerType));
        }

        [Test]
        public void EnumsTravelAsNames_NotOrdinals()
        {
            // An ordinal in a hand-editable file silently changes meaning the day a value is
            // inserted into the enum.
            var config = new SpawnerInstanceConfig { spawnMode = SpawnMode.Burst };
            string json = SpawnerInstanceSerializer.Serialize(new[] { MakeRecord(config) });

            Assert.That(json, Does.Contain("\"spawnMode\": \"Burst\""));
        }

        [Test]
        public void QuotesAndBackslashes_AreEscaped()
        {
            // An unescaped quote in a zone name or an entity id produces a file MiniJsonRuntime
            // then refuses — turning a naming choice into a map that will not load.
            var record = MakeRecord(new SpawnerInstanceConfig());
            record.Zone       = "he said \"forest\"";
            record.InstanceId = "back\\slash";

            var back = RoundTrip(record);

            Assert.That(back.Zone,       Is.EqualTo("he said \"forest\""));
            Assert.That(back.InstanceId, Is.EqualTo("back\\slash"));
        }

        [Test]
        public void FloatsAreWrittenInvariant_UnderACommaDecimalCulture()
        {
            // A comma decimal separator produces "2,25", which JSON reads as two values.
            var previous = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("es-ES");
            try
            {
                var config = new SpawnerInstanceConfig { cooldownSeconds = 2.25f };
                string json = SpawnerInstanceSerializer.Serialize(new[] { MakeRecord(config) });

                Assert.That(json, Does.Contain("\"cooldown\": 2.25"));
                Assert.That(SpawnerInstanceSerializer.ParseAll(json)[0].Config.cooldownSeconds,
                            Is.EqualTo(2.25f).Within(1e-4f));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }
    }
}
