using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spawners;

namespace Valkur.Tests.EditMode.Gameplay.Spawners
{
    /// <summary>
    /// The property the whole rework exists for: <b>two placements of one preset are
    /// independent, and neither follows the preset after it was placed.</b>
    ///
    /// <para>Before this, a placed spawner's entire on-disk record was
    /// <c>template_id</c>/<c>zone</c>/<c>tile</c>/<c>id</c> and the runtime read every
    /// decision off the shared asset — so tuning one camp retuned every other camp of its
    /// kind, and because a ScriptableObject edited in Play Mode survives until the next domain
    /// reload, the change followed the author back into the Editor and rewrote shipped
    /// balance.</para>
    /// </summary>
    [TestFixture]
    public class SpawnerCopyOnPlaceTests
    {
        private SpawnerTemplateData _preset;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _preset = ScriptableObject.CreateInstance<SpawnerTemplateData>();
            _preset.templateId      = "camp";
            _preset.cooldownSeconds = 3f;
            _preset.spawnRadius     = 12;
            _preset.waves = new List<WaveDefinition>
            {
                new WaveDefinition { spawns = { new WaveSpawnEntry { entityId = "barbol", count = 3 } } }
            };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            if (_preset != null) Object.DestroyImmediate(_preset);
        }

        private SpawnerInstance Place(string id)
        {
            var go = new GameObject($"Spawner_{id}");
            _spawned.Add(go);
            var si = go.AddComponent<SpawnerInstance>();
            si.Initialize(_preset, id, "Forest", spawner: null);
            return si;
        }

        [Test]
        public void TwoPlacements_DoNotShareAConfig()
        {
            var a = Place("a");
            var b = Place("b");

            a.Config.cooldownSeconds = 99f;

            Assert.That(b.Config.cooldownSeconds, Is.EqualTo(3f).Within(1e-4f),
                "Editing one placement reached another — the two are sharing an object.");
        }

        [Test]
        public void APlacement_DoesNotFollowThePresetAfterPlacement()
        {
            var si = Place("a");

            _preset.cooldownSeconds = 99f;

            Assert.That(si.Config.cooldownSeconds, Is.EqualTo(3f).Within(1e-4f),
                "The placement is still reading the shared asset.");
        }

        [Test]
        public void EditingAPlacement_NeverWritesBackToThePreset()
        {
            var si = Place("a");

            si.Config.cooldownSeconds = 42f;
            si.Config.spawnRadius     = 1;

            Assert.That(_preset.cooldownSeconds, Is.EqualTo(3f).Within(1e-4f));
            Assert.That(_preset.spawnRadius,     Is.EqualTo(12));
        }

        /// <summary>
        /// A SHALLOW copy would leave every placement pointing at the preset's own
        /// <see cref="WaveDefinition"/> objects, so editing one camp's roster would edit them
        /// all — copy-on-place defeated one level down, and invisible until two placements of
        /// one preset exist.
        /// </summary>
        [Test]
        public void TheRoster_IsDeepCopied()
        {
            var a = Place("a");
            var b = Place("b");

            a.Config.waves[0].spawns[0].count    = 99;
            a.Config.waves[0].spawns[0].entityId = "dark_vampire";

            Assert.That(b.Config.waves[0].spawns[0].count,    Is.EqualTo(3));
            Assert.That(b.Config.waves[0].spawns[0].entityId, Is.EqualTo("barbol"));
            Assert.That(_preset.waves[0].spawns[0].count,     Is.EqualTo(3));
            Assert.That(_preset.waves[0].spawns[0].entityId,  Is.EqualTo("barbol"));
        }

        [Test]
        public void APlacementWhosePresetIsGone_StillHasItsOwnConfig()
        {
            // What lets a preset be deleted from the catalogue without unplacing anything.
            var config = SpawnerInstanceConfig.SnapshotOf(_preset);

            var go = new GameObject("Spawner_orphan");
            _spawned.Add(go);
            var si = go.AddComponent<SpawnerInstance>();
            si.Initialize(null, config, "orphan", "Forest", spawner: null);

            Assert.That(si.Preset, Is.Null);
            Assert.That(si.Config.cooldownSeconds, Is.EqualTo(3f).Within(1e-4f));
            Assert.That(si.Config.waves[0].spawns[0].entityId, Is.EqualTo("barbol"));
        }

        [Test]
        public void ANullConfig_FallsBackToASnapshotOfThePreset_NotToABlankOne()
        {
            var go = new GameObject("Spawner_nullcfg");
            _spawned.Add(go);
            var si = go.AddComponent<SpawnerInstance>();
            si.Initialize(_preset, null, "nullcfg", "Forest", spawner: null);

            Assert.That(si.Config.cooldownSeconds, Is.EqualTo(3f).Within(1e-4f),
                "A row whose freeze could not run must behave exactly as it did, not as a " +
                "brand-new default spawner.");
        }

        // ── ApplyConfig re-seeds the state machine ──────────────────────────────

        [Test]
        public void SwitchingToAutoStart_ArmsTheSpawnerImmediately()
        {
            // Without the re-seed, flipping Trigger in the properties panel would take effect
            // only after a reload — which reads as the control not working.
            var si = Place("a");
            Assert.That(si.State, Is.EqualTo(SpawnerState.Idle));

            si.Config.triggerType = TriggerType.Auto;
            si.Config.autoStart   = true;
            si.ApplyConfig(si.Config);

            Assert.That(si.State, Is.EqualTo(SpawnerState.Active));
        }

        [Test]
        public void SwitchingBackToProximity_DisarmsIt()
        {
            _preset.triggerType = TriggerType.Auto;
            _preset.autoStart   = true;
            var si = Place("a");
            Assert.That(si.State, Is.EqualTo(SpawnerState.Active));

            si.Config.triggerType = TriggerType.Proximity;
            si.ApplyConfig(si.Config);

            Assert.That(si.State, Is.EqualTo(SpawnerState.Idle));
        }

        [Test]
        public void ApplyConfig_RewindsTheWaveCursor()
        {
            var si = Place("a");
            si.ApplyConfig(SpawnerInstanceConfig.SnapshotOf(_preset));

            Assert.That(si.CurrentWaveIndex, Is.EqualTo(0));
        }

        [Test]
        public void Clone_IsIndependentIncludingItsRoster()
        {
            // Undo and the two reapply actions depend on this: a shared "before" snapshot
            // would give every target the first one's config.
            var original = SpawnerInstanceConfig.SnapshotOf(_preset);
            var copy     = original.Clone();

            copy.cooldownSeconds = 99f;
            copy.waves[0].spawns[0].count = 99;

            Assert.That(original.cooldownSeconds, Is.EqualTo(3f).Within(1e-4f));
            Assert.That(original.waves[0].spawns[0].count, Is.EqualTo(3));
        }

        [Test]
        public void SnapshotOfNull_IsBlankRatherThanNull()
        {
            var config = SpawnerInstanceConfig.SnapshotOf(null);

            Assert.That(config, Is.Not.Null);
            Assert.That(config.waves, Is.Not.Null);
        }
    }
}
