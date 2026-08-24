using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// COPY ON PLACE — a placed emitter owns its configuration.
    ///
    /// A preset used to be a live link: every field in the F1 properties panel edited the
    /// shared asset, so an author tuning the emitter they had just clicked on the map watched
    /// all eighty-four placements of it change at once. It is now a starting point. An instance
    /// takes a copy when it is placed and is independent from then on, which makes the two
    /// scopes say what they mean:
    ///
    ///  • editing a PRESET decides what the NEXT placement is born with;
    ///  • editing a PLACEMENT changes that placement and nothing else;
    ///  • the two "reapply preset" actions are how the old coupling happens deliberately.
    ///
    /// Everything below is the model layer the editor drives — the panel routes edits to one or
    /// the other through <c>ParticlesRuntimeEditor.TryApplyPropertyEdit</c>, and the routing
    /// rule is the one thing here a test cannot reach without a live editor.
    /// </summary>
    [TestFixture]
    public class ParticleCopyOnPlaceTests
    {
        private const string CATALOG_PATH =
            "Assets/_Project/Data/Catalogs/Particles/ParticlePresetCatalog.asset";

        private readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Fixtures ─────────────────────────────────────────────────────────────

        /// <summary>A preset owned by the test, so no shipped asset is ever mutated.</summary>
        private ParticlePresetDefinition MakePreset(string id, float emitRate = 10f)
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            _created.Add(def);
            def.id = id;
            def.displayName = id;
            def.type = "aura";
            def.vfx = new ParticleVfxParams
            {
                kind = "aura",
                loops = true,
                emitRate = emitRate,
                lifespan = 2f,
                speed = 0.5f,
                sizeMin = 0.1f,
                sizeMax = 0.2f,
                radius = 0.5f,
                directionDegrees = -1f,
                spawnWidth = 2f,
                spawnHeight = 1f,
            };
            def.layers = new List<ParticlePresetDefinition>();
            return def;
        }

        /// <summary>Places an emitter the way the loader does: snapshot, then run the copy.</summary>
        private ParticleEmitter Place(ParticlePresetDefinition preset,
                                      ParticleInstanceOverrides overrides = default,
                                      float scale = 1f)
        {
            var go = new GameObject("PE_" + preset.id);
            _created.Add(go);

            var identity = go.AddComponent<PersistedParticleInstance>();
            var sanitized = overrides.Sanitized();
            identity.Restore(preset.id, System.Guid.NewGuid().ToString("N"), scale, sanitized);

            var config = ParticleInstanceConfig.SnapshotOf(preset, sanitized);
            identity.SetConfig(config);

            var emitter = go.AddComponent<ParticleEmitter>();
            emitter.ApplyConfig(preset, config, scale);
            return emitter;
        }

        private static float LiveRate(ParticleEmitter emitter)
            => emitter.GetComponentInChildren<ParticleSystem>(true).emission.rateOverTime.constant;

        // ── The rule ─────────────────────────────────────────────────────────────

        [Test]
        public void APlacementIsBornFromThePreset()
        {
            var preset = MakePreset("born", emitRate: 7f);
            var emitter = Place(preset);

            Assert.IsTrue(emitter.HasOwnConfig, "A placement owns its configuration.");
            Assert.AreEqual(7f, emitter.Config.vfx.emitRate, 1e-4f);
            Assert.AreEqual(7f, LiveRate(emitter), 1e-4f);
            Assert.AreNotSame(preset.vfx, emitter.Config.vfx,
                "A copy, not the asset's own block — writing through it would edit the preset.");
        }

        [Test]
        public void EditingThePreset_LeavesEveryExistingPlacementAlone()
        {
            var preset = MakePreset("shared", emitRate: 10f);
            var first = Place(preset);
            var second = Place(preset);

            preset.vfx.emitRate = 40f;
            first.ReapplyConfig();
            second.ReapplyConfig();

            Assert.AreEqual(10f, LiveRate(first), 1e-4f);
            Assert.AreEqual(10f, LiveRate(second), 1e-4f);
        }

        [Test]
        public void EditingThePreset_ReachesTheNextPlacement()
        {
            var preset = MakePreset("future", emitRate: 10f);
            Place(preset);

            preset.vfx.emitRate = 40f;
            var fresh = Place(preset);

            Assert.AreEqual(40f, LiveRate(fresh), 1e-4f,
                "The preset decides what a new placement is born with — that is what it is for.");
        }

        [Test]
        public void EditingOnePlacement_TouchesNothingElse()
        {
            var preset = MakePreset("solo", emitRate: 10f);
            var edited = Place(preset);
            var sibling = Place(preset);

            Assert.IsTrue(ParticlePresetFieldWriter.TrySetField(
                edited.Config.vfx, "vfx.emitRate", 99f, out _));
            edited.ReapplyConfig();

            Assert.AreEqual(99f, LiveRate(edited), 1e-4f);
            Assert.AreEqual(10f, LiveRate(sibling), 1e-4f, "The sibling is a different copy.");
            Assert.AreEqual(10f, preset.vfx.emitRate, 1e-4f, "And the asset is untouched.");
        }

        [Test]
        public void ReapplyingThePreset_OverwritesThePlacement()
        {
            var preset = MakePreset("reapply", emitRate: 10f);
            var emitter = Place(preset);

            ParticlePresetFieldWriter.TrySetField(emitter.Config.vfx, "vfx.emitRate", 99f, out _);
            emitter.ReapplyConfig();
            Assert.AreEqual(99f, LiveRate(emitter), 1e-4f);

            preset.vfx.emitRate = 25f;
            var fresh = ParticleInstanceConfig.SnapshotOf(preset, ParticleInstanceOverrides.None);
            emitter.ApplyConfig(preset, fresh, emitter.ScaleMultiplier);

            Assert.AreEqual(25f, LiveRate(emitter), 1e-4f,
                "The deliberate version of the coupling copy-on-place removed.");
        }

        [Test]
        public void ApplyingAPresetDirectly_DropsAnOwnedConfig()
        {
            // The F1 preview emitter is handed a different preset on every picker click, and
            // "reapply preset" goes through the same door. A config left in place would win
            // silently over what was just asked for.
            var preset = MakePreset("dropped", emitRate: 10f);
            var emitter = Place(preset);
            ParticlePresetFieldWriter.TrySetField(emitter.Config.vfx, "vfx.emitRate", 99f, out _);
            emitter.ReapplyConfig();

            emitter.ApplyPreset(preset, 1f);

            Assert.IsFalse(emitter.HasOwnConfig);
            Assert.AreEqual(10f, LiveRate(emitter), 1e-4f);
        }

        // ── Composites ───────────────────────────────────────────────────────────

        [Test]
        public void ACompositeSnapshotsEveryLayerItActuallyRuns()
        {
            var pollen = AssetDatabase.LoadAssetAtPath<ParticlePresetCatalog>(CATALOG_PATH)
                ?.GetById("flowers_pollen_soft");
            Assert.IsTrue(pollen != null, "'flowers_pollen_soft' is missing from the catalog.");

            var emitter = Place(pollen);

            int validLayers = 0;
            foreach (var layer in pollen.layers)
                if (ParticleInstanceConfig.IsSnapshotableLayer(pollen, layer)) validLayers++;

            Assert.AreEqual(validLayers, emitter.Config.LayerCount,
                "Index i of the config is index i of the systems the emitter builds; a skipped " +
                "layer here would resize or recolour one layer with another's numbers.");
            Assert.AreEqual(validLayers + 1,
                emitter.GetComponentsInChildren<ParticleSystem>(true).Length);
        }

        // ── Legacy data ──────────────────────────────────────────────────────────

        [Test]
        public void MigratingAnOlderRecord_FoldsItsSizeRatiosIntoTheSnapshot()
        {
            // v3 stored per-instance size as ratios against the preset. Copy-on-place has no
            // place for them — the config IS the size — so they are folded in as the snapshot
            // is taken, once, and never applied again.
            var preset = MakePreset("legacy");
            var overrides = new ParticleInstanceOverrides(2f, 0.5f, 1f);

            var config = ParticleInstanceConfig.SnapshotOf(preset, overrides);

            Assert.AreEqual(preset.vfx.spawnWidth * 2f, config.vfx.spawnWidth, 1e-4f);
            Assert.AreEqual(preset.vfx.spawnHeight * 0.5f, config.vfx.spawnHeight, 1e-4f);

            var emitter = Place(preset, overrides);
            Assert.IsTrue(emitter.Overrides.IsDefault,
                "Keeping the ratios beside a config that already contains them would apply " +
                "them a second time on the next rebuild.");
        }

        [Test]
        public void BakingSizeRatios_LeavesOneAnswerInTheConfig()
        {
            var preset = MakePreset("bake");
            var emitter = Place(preset);

            emitter.SetOverrides(new ParticleInstanceOverrides(2f, 1f, 1f));
            emitter.BakeOverrides();

            Assert.IsTrue(emitter.Overrides.IsDefault);
            Assert.AreEqual(preset.vfx.spawnWidth * 2f, emitter.Config.vfx.spawnWidth, 1e-4f,
                "The drag's ratio becomes the stored width; nothing multiplies it afterwards.");
        }

        // ── Persistence ──────────────────────────────────────────────────────────

        [Test]
        public void AConfigRoundTripsThroughTheWorldFile()
        {
            var preset = MakePreset("persisted", emitRate: 13f);
            var emitter = Place(preset);
            ParticlePresetFieldWriter.TrySetField(emitter.Config.vfx, "vfx.emitRate", 21f, out _);

            var identity = emitter.GetComponent<PersistedParticleInstance>();
            string json = ParticleInstanceSerializer.Serialize(
                new List<PersistedParticleInstance> { identity }, null);

            Assert.IsTrue(json.Contains("\"version\":4"));
            Assert.IsTrue(json.Contains("\"config\""));

            var records = ParticleInstanceSerializer.Deserialize(json, null);
            Assert.AreEqual(1, records.Count);
            Assert.IsNotNull(records[0].Config, "A v4 record carries its configuration.");
            Assert.AreEqual(21f, records[0].Config.vfx.emitRate, 1e-3f);
        }

        [Test]
        public void APreV4Record_ComesBackWithoutAConfig_SoTheLoaderCanSnapshotIt()
        {
            string v3 = "{\"version\":3,\"instances\":[" +
                        "{\"id\":\"abc\",\"preset_id\":\"leaf\",\"zone\":\"\",\"rel_x\":0,\"rel_y\":0," +
                        "\"scale_multiplier\":1.0,\"spawn_scale_x\":1.5,\"spawn_scale_y\":1.0," +
                        "\"reach\":1.0}]}";

            var records = ParticleInstanceSerializer.Deserialize(v3, null);

            Assert.AreEqual(1, records.Count);
            Assert.IsNull(records[0].Config,
                "No config in the file is the signal that this record predates copy-on-place; " +
                "the loader freezes it against its preset on the way in.");
            Assert.AreEqual(1.5f, records[0].Overrides.spawnScaleX, 1e-3f,
                "And its size ratios survive to be folded into that snapshot.");
        }

        [Test]
        public void EveryShippedPresetSnapshotsAndRoundTripsLosslessly()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ParticlePresetCatalog>(CATALOG_PATH);
            Assert.IsTrue(catalog != null);

            int checkedPresets = 0;
            foreach (var preset in catalog.Presets)
            {
                if (preset == null || preset.vfx == null) continue;
                checkedPresets++;

                var config = ParticleInstanceConfig.SnapshotOf(preset, ParticleInstanceOverrides.None);
                string written = ParticleVfxParamsJson.Write(config.vfx);

                var parsed = Valkur.Gameplay.World.MiniJsonRuntime.Deserialize(written)
                    as Dictionary<string, object>;
                string rewritten = ParticleVfxParamsJson.Write(ParticleVfxParamsJson.Read(parsed));

                Assert.AreEqual(written, rewritten,
                    $"'{preset.id}' does not survive a round trip through the world file — a " +
                    "field the writer emits that the reader cannot restore is a placement that " +
                    "changes shape on reload.");
            }

            Assert.Greater(checkedPresets, 100, "The catalog sweep found almost nothing.");
        }

        // ── The migration write ──────────────────────────────────────────────────

        private static ParticleInstanceRecord Record(string presetId, string zone, int x, int y,
                                                     ParticleInstanceOverrides ov = default,
                                                     ParticleInstanceConfig config = null)
            => new ParticleInstanceRecord
            {
                Guid = System.Guid.NewGuid().ToString("N"),
                PresetId = presetId,
                Zone = zone,
                RelX = x,
                RelY = y,
                ScaleMultiplier = 1f,
                Overrides = ov.Sanitized(),
                Config = config,
            };

        [Test]
        public void SerializeRecords_KeepsIdentityAndPositionExactly()
        {
            var preset = MakePreset("mig_identity");
            var records = new List<ParticleInstanceRecord>
            {
                Record("mig_identity", "Lobby", 1581, 728,
                       config: ParticleInstanceConfig.SnapshotOf(preset, ParticleInstanceOverrides.None)),
                Record("mig_identity", "zone_100_50", 59, 629,
                       config: ParticleInstanceConfig.SnapshotOf(preset, ParticleInstanceOverrides.None)),
            };

            var read = ParticleInstanceSerializer.Deserialize(
                ParticleInstanceSerializer.SerializeRecords(records), null);

            Assert.AreEqual(records.Count, read.Count);
            for (int i = 0; i < records.Count; i++)
            {
                Assert.AreEqual(records[i].Guid, read[i].Guid, "guid moved");
                Assert.AreEqual(records[i].Zone, read[i].Zone, "zone moved");
                Assert.AreEqual(records[i].RelX, read[i].RelX, "rel_x moved");
                Assert.AreEqual(records[i].RelY, read[i].RelY, "rel_y moved");
            }
        }

        [Test]
        public void SerializeRecords_CarriesTheFrozenConfigThrough()
        {
            var preset = MakePreset("mig_config", emitRate: 7.5f);
            var config = ParticleInstanceConfig.SnapshotOf(preset,
                new ParticleInstanceOverrides(2f, 0.5f, 3f));

            var read = ParticleInstanceSerializer.Deserialize(
                ParticleInstanceSerializer.SerializeRecords(
                    new List<ParticleInstanceRecord> { Record("mig_config", "Lobby", 10, 20, config: config) }),
                null);

            Assert.IsNotNull(read[0].Config, "the frozen configuration did not survive the write");
            Assert.AreEqual(ParticleVfxParamsJson.Write(config.vfx),
                            ParticleVfxParamsJson.Write(read[0].Config.vfx),
                            "the configuration drifted across the migration write");

            // The ratios were folded into the snapshot; writing them too would apply them twice
            // on the next load.
            Assert.IsTrue(read[0].Overrides.Sanitized().IsDefault,
                "a record that owns a configuration must carry no size ratios");
        }

        [Test]
        public void SerializeRecords_PassesUnspawnableRecordsThroughUntouched()
        {
            // No config, because the loader could not resolve the preset — and the ratios are
            // all that placement has. Dropping it, or dropping them, is data loss on a catalog
            // that is only temporarily missing an entry.
            var ov = new ParticleInstanceOverrides(1.4792f, 0.6035f, 0.5623f);
            var read = ParticleInstanceSerializer.Deserialize(
                ParticleInstanceSerializer.SerializeRecords(
                    new List<ParticleInstanceRecord> { Record("preset_not_in_catalog", "Lobby", 7, 9, ov) }),
                null);

            Assert.AreEqual(1, read.Count, "an unspawnable record was dropped by the migration write");
            Assert.AreEqual("preset_not_in_catalog", read[0].PresetId);
            Assert.IsNull(read[0].Config);
            Assert.AreEqual(ov.spawnScaleX, read[0].Overrides.spawnScaleX, 1e-3f);
            Assert.AreEqual(ov.spawnScaleY, read[0].Overrides.spawnScaleY, 1e-3f);
            Assert.AreEqual(ov.reachScale, read[0].Overrides.reachScale, 1e-3f);
        }

        [Test]
        public void Loader_FreezesALegacyFileOntoDisk_OnTheFirstLoad()
        {
            // The whole point of the migration write: a v3 file is frozen ONCE, so retuning the
            // asset afterwards cannot reach a placement that already existed. Without the write
            // the freeze lasts only until the next restart, which re-snapshots from the edited
            // preset — the coupling copy-on-place removes, coming back through the file.
            var preset = MakePreset("mig_loader", emitRate: 3f);

            var catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();
            _created.Add(catalog);
            catalog.SetPresets(new List<ParticlePresetDefinition> { preset });

            var store = new InMemoryParticleInstanceStore(
                "{\"version\":3,\"instances\":[" +
                "{\"id\":\"aaa\",\"preset_id\":\"mig_loader\",\"zone\":\"\",\"rel_x\":32,\"rel_y\":64," +
                "\"scale_multiplier\":1.0000,\"spawn_scale_x\":1.5000,\"spawn_scale_y\":0.5000,\"reach\":2.0000}," +
                "{\"id\":\"bbb\",\"preset_id\":\"not_in_catalog\",\"zone\":\"\",\"rel_x\":1,\"rel_y\":2," +
                "\"scale_multiplier\":1.0000}]}");

            var go = new GameObject("ParticleInstancesLoader");
            _created.Add(go);
            var loader = go.AddComponent<ParticleInstancesLoader>();
            loader.Initialize(catalog);
            loader.SetInstanceStore(store);

            loader.Reload();

            var written = ParticleInstanceSerializer.Deserialize(store.Load(), null);
            Assert.AreEqual(2, written.Count, "the migration write lost a record");

            var frozen = written.Find(r => r.PresetId == "mig_loader");
            Assert.IsNotNull(frozen?.Config,
                "the placement was frozen in memory but not on disk — the next restart would " +
                "re-snapshot it from whatever the preset says then");
            Assert.AreEqual(3f, frozen.Config.vfx.emitRate, 1e-4f);
            Assert.IsTrue(frozen.Overrides.Sanitized().IsDefault,
                "the size ratios belong in the snapshot now, not beside it");

            var passthrough = written.Find(r => r.PresetId == "not_in_catalog");
            Assert.IsNotNull(passthrough, "a record the loader could not spawn was dropped");
            Assert.IsNull(passthrough.Config);

            // Idempotent: a file that already carries configs is not rewritten.
            string afterFirst = store.Load();
            loader.Reload();
            Assert.AreEqual(afterFirst, store.Load(),
                "the migration rewrote a file that was already migrated");
        }

        [Test]
        public void SerializeRecords_WritesTheCurrentSchemaVersion()
        {
            string json = ParticleInstanceSerializer.SerializeRecords(
                new List<ParticleInstanceRecord> { Record("mig_version", "Lobby", 1, 2) });

            StringAssert.StartsWith("{\"version\":4", json,
                "a migration that writes an older version would be redone on every load");
        }
    }
}
