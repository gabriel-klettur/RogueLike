using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Gameplay.VFX
{
    /// <summary>
    /// Tests for v1 → v2 schema migration in <see cref="ParticleInstanceSerializer"/>.
    ///
    /// Requirements:
    ///   - V1 input (bare array) loads correctly.
    ///   - After loading v1, the next serialize call emits v2 format.
    ///   - V1 synthetic GUIDs don't collide with each other.
    ///   - The existing production file (3 v1 entries) parses correctly.
    /// </summary>
    [TestFixture]
    public class ParticlePersistenceSchemaMigrationTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
                if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
        }

        // ── V1 parse ──────────────────────────────────────────────────────────────

        [Test]
        public void V1_BareArray_ParsesCorrectly()
        {
            // Simulates the exact format of the current production file.
            string v1Json =
                "[\n" +
                "  {\"id\": 1, \"preset_id\": \"firework_launch\", \"zone\": \"Forest\", \"rel_x\": 898, \"rel_y\": 351},\n" +
                "  {\"id\": 2, \"preset_id\": \"water_fountain_small\", \"zone\": \"Forest\", \"rel_x\": 1139, \"rel_y\": 315},\n" +
                "  {\"id\": 3, \"preset_id\": \"arcane_flame_emitter\", \"zone\": \"Forest\", \"rel_x\": 1027, \"rel_y\": 413}\n" +
                "]";

            var records = ParticleInstanceSerializer.Deserialize(v1Json, null, 50, 1f);

            Assert.AreEqual(3, records.Count, "Must parse all 3 v1 entries.");
            Assert.AreEqual("firework_launch",       records[0].PresetId);
            Assert.AreEqual("water_fountain_small",  records[1].PresetId);
            Assert.AreEqual("arcane_flame_emitter",  records[2].PresetId);
        }

        [Test]
        public void V1_SyntheticGuids_AreUnique()
        {
            string v1Json =
                "[{\"id\":1,\"preset_id\":\"fire\",\"zone\":\"\",\"rel_x\":0,\"rel_y\":0}," +
                " {\"id\":2,\"preset_id\":\"smoke\",\"zone\":\"\",\"rel_x\":0,\"rel_y\":0}," +
                " {\"id\":3,\"preset_id\":\"water\",\"zone\":\"\",\"rel_x\":0,\"rel_y\":0}]";

            var records = ParticleInstanceSerializer.Deserialize(v1Json, null);

            var guids = new HashSet<string>();
            foreach (var r in records)
                guids.Add(r.Guid);

            Assert.AreEqual(3, guids.Count,
                "V1 synthetic GUIDs must be unique for each record.");
        }

        [Test]
        public void V1_ScaleMultiplier_DefaultsToOne_WhenAbsent()
        {
            string v1Json = "[{\"id\":1,\"preset_id\":\"fire\",\"zone\":\"\",\"rel_x\":0,\"rel_y\":0}]";
            var records = ParticleInstanceSerializer.Deserialize(v1Json, null);

            Assert.AreEqual(1f, records[0].ScaleMultiplier, 0.0001f,
                "Missing scale_multiplier in v1 must default to 1.0.");
        }

        // ── V1 → V2 migration on next save ────────────────────────────────────────

        [Test]
        public void V1Input_OnSerialize_ProducesV2Output()
        {
            string v1Json = "[{\"id\":1,\"preset_id\":\"arcane\",\"zone\":\"\",\"rel_x\":0,\"rel_y\":0}]";
            var records = ParticleInstanceSerializer.Deserialize(v1Json, null);

            // Reconstruct PersistedParticleInstance from the migrated record.
            var go = new GameObject("PE_arcane");
            go.transform.position = records[0].WorldPos;
            _created.Add(go);
            var inst = go.AddComponent<PersistedParticleInstance>();
            inst.Restore(records[0].PresetId, records[0].Guid, records[0].ScaleMultiplier);

            string v2Json = ParticleInstanceSerializer.Serialize(
                new List<PersistedParticleInstance> { inst }, null);

            Assert.IsTrue(v2Json.Contains("\"version\":2"),
                "Serialize after v1 migration must produce v2 JSON.");
            Assert.IsFalse(v2Json.TrimStart().StartsWith("["),
                "V2 JSON must NOT be a bare array.");
        }

        // ── V2 round-trip ─────────────────────────────────────────────────────────

        [Test]
        public void V2_RoundTrip_PreservesAllFields()
        {
            string guid = System.Guid.NewGuid().ToString("N");
            string v2Json = $"{{\"version\":2,\"instances\":[" +
                $"{{\"id\":\"{guid}\",\"preset_id\":\"portal_red\",\"zone\":\"Forest\",\"rel_x\":128,\"rel_y\":64,\"scale_multiplier\":2.5}}]}}";

            var records = ParticleInstanceSerializer.Deserialize(v2Json, null, 50, 1f);

            Assert.AreEqual(1, records.Count);
            Assert.AreEqual(guid, records[0].Guid);
            Assert.AreEqual("portal_red", records[0].PresetId);
            Assert.AreEqual(128, records[0].RelX);
            Assert.AreEqual(64, records[0].RelY);
            Assert.AreEqual(2.5f, records[0].ScaleMultiplier, 0.001f);
        }
    }
}
