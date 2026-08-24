using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Game.VFX
{
    /// <summary>
    /// Unit tests for <see cref="ParticleInstanceSerializer"/>.
    ///
    /// Exercises:
    ///   - V2 round-trip: serialize → deserialize preserves all fields.
    ///   - V1 migration: bare array JSON is parsed and upgraded on deserialize.
    ///   - Schema migration test: v1 input → v2 output on next serialize call.
    ///   - Edge cases: empty zone, scale=1, missing scale_multiplier field.
    /// </summary>
    [TestFixture]
    public class ParticleInstanceSerializerTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
                if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
        }

        private PersistedParticleInstance CreateInstance(string presetId, float scale, Vector3 pos)
        {
            var go = new GameObject($"PE_{presetId}");
            go.transform.position = pos;
            _created.Add(go);
            var inst = go.AddComponent<PersistedParticleInstance>();
            inst.Initialize(presetId, scale);
            return inst;
        }

        // ── V2 round-trip ─────────────────────────────────────────────────────────

        [Test]
        public void Serialize_ProducesCurrentVersionJson()
        {
            var instances = new List<PersistedParticleInstance>
            {
                CreateInstance("fire_aura", 1f, Vector3.zero)
            };

            string json = ParticleInstanceSerializer.Serialize(instances, null);

            // v3 added the optional per-instance size overrides; v4 added each record's own
            // configuration, the copy of the preset a placement is born with.
            Assert.IsTrue(json.Contains("\"version\":4"),
                "Serialize must stamp the current schema version.");
            Assert.IsTrue(json.Contains("\"instances\":"),
                "Serialize must include instances array.");
        }

        [Test]
        public void Serialize_EmptyList_ProducesEmptyInstancesArray()
        {
            string json = ParticleInstanceSerializer.Serialize(
                new List<PersistedParticleInstance>(), null);

            Assert.IsTrue(json.Contains("\"instances\":[]"),
                $"Empty list must produce {{\"instances\":[]}}. Got: {json}");
        }

        [Test]
        public void Serialize_IncludesAllFields()
        {
            var inst = CreateInstance("smoke_ring", 2.5f, new Vector3(3f, 4f, 0f));
            string expectedGuid = inst.StableGuid;

            string json = ParticleInstanceSerializer.Serialize(
                new List<PersistedParticleInstance> { inst }, null);

            Assert.IsTrue(json.Contains(expectedGuid),
                "Serialized JSON must contain the stable GUID.");
            Assert.IsTrue(json.Contains("\"preset_id\":\"smoke_ring\""),
                "Serialized JSON must contain preset_id.");
            Assert.IsTrue(json.Contains("\"scale_multiplier\":"),
                "Serialized JSON must contain scale_multiplier.");
        }

        [Test]
        public void Deserialize_V2_PreservesPresetId()
        {
            string guid = System.Guid.NewGuid().ToString("N");
            string json = $"{{\"version\":2,\"instances\":[{{\"id\":\"{guid}\",\"preset_id\":\"water_fountain\",\"zone\":\"\",\"rel_x\":0,\"rel_y\":0,\"scale_multiplier\":1.0}}]}}";

            var records = ParticleInstanceSerializer.Deserialize(json, null);

            Assert.AreEqual(1, records.Count);
            Assert.AreEqual("water_fountain", records[0].PresetId);
        }

        [Test]
        public void Deserialize_V2_PreservesGuid()
        {
            string guid = "aabbccdd11223344aabbccdd11223344";
            string json = $"{{\"version\":2,\"instances\":[{{\"id\":\"{guid}\",\"preset_id\":\"aura\",\"zone\":\"\",\"rel_x\":0,\"rel_y\":0,\"scale_multiplier\":1.0}}]}}";

            var records = ParticleInstanceSerializer.Deserialize(json, null);

            Assert.AreEqual(guid, records[0].Guid,
                "Deserialize must preserve the stable GUID from v2 JSON.");
        }

        [Test]
        public void Deserialize_V2_PreservesScaleMultiplier()
        {
            string guid = System.Guid.NewGuid().ToString("N");
            string json = $"{{\"version\":2,\"instances\":[{{\"id\":\"{guid}\",\"preset_id\":\"portal\",\"zone\":\"\",\"rel_x\":0,\"rel_y\":0,\"scale_multiplier\":3.5}}]}}";

            var records = ParticleInstanceSerializer.Deserialize(json, null);

            Assert.AreEqual(3.5f, records[0].ScaleMultiplier, 0.001f,
                "Deserialize must preserve scale_multiplier.");
        }

        // ── V1 migration ──────────────────────────────────────────────────────────

        [Test]
        public void Deserialize_V1_BareArray_ParsesAllEntries()
        {
            // Legacy v1 format: bare JSON array.
            string json = "[{\"id\":1,\"preset_id\":\"firework\",\"zone\":\"Forest\",\"rel_x\":100,\"rel_y\":200}," +
                          "{\"id\":2,\"preset_id\":\"smoke\",\"zone\":\"Forest\",\"rel_x\":300,\"rel_y\":400}]";

            var records = ParticleInstanceSerializer.Deserialize(json, null, 50, 1f);

            Assert.AreEqual(2, records.Count,
                "V1 bare array must parse all entries.");
        }

        [Test]
        public void Deserialize_V1_GeneratesSyntheticGuids()
        {
            string json = "[{\"id\":1,\"preset_id\":\"fire\",\"zone\":\"\",\"rel_x\":0,\"rel_y\":0}]";

            var records = ParticleInstanceSerializer.Deserialize(json, null);

            Assert.IsFalse(string.IsNullOrEmpty(records[0].Guid),
                "V1 migration must synthesize a non-empty GUID.");
        }

        [Test]
        public void Deserialize_V1_ThenSerialize_ProducesCurrentVersion()
        {
            // Full migration cycle: parse v1, convert records to PersistedParticleInstance,
            // serialize back — result must be v2.
            string v1Json = "[{\"id\":1,\"preset_id\":\"arcane_flame\",\"zone\":\"\",\"rel_x\":0,\"rel_y\":0}]";
            var records = ParticleInstanceSerializer.Deserialize(v1Json, null);

            // Simulate spawning components from records.
            var go = new GameObject("PE_arcane_flame");
            go.transform.position = records[0].WorldPos;
            _created.Add(go);
            var inst = go.AddComponent<PersistedParticleInstance>();
            inst.Restore(records[0].PresetId, records[0].Guid, records[0].ScaleMultiplier);

            string v2Json = ParticleInstanceSerializer.Serialize(
                new List<PersistedParticleInstance> { inst }, null);

            Assert.IsTrue(v2Json.Contains("\"version\":4"),
                "A migrated v1 record is written in the current schema.");
            Assert.IsTrue(v2Json.Contains(records[0].Guid),
                "Migrated GUID must survive round-trip through v2 serialize.");
        }

        // ── Edge cases ────────────────────────────────────────────────────────────

        [Test]
        public void Deserialize_EmptyJson_ReturnsEmptyList()
        {
            var records = ParticleInstanceSerializer.Deserialize("", null);
            Assert.AreEqual(0, records.Count, "Empty JSON must return empty list.");
        }

        [Test]
        public void Deserialize_NullJson_ReturnsEmptyList()
        {
            var records = ParticleInstanceSerializer.Deserialize(null, null);
            Assert.AreEqual(0, records.Count, "Null JSON must return empty list.");
        }

        [Test]
        public void Deserialize_MissingScaleMultiplier_DefaultsToOne()
        {
            // The field is absent in this entry.
            string guid = System.Guid.NewGuid().ToString("N");
            string json = $"{{\"version\":2,\"instances\":[{{\"id\":\"{guid}\",\"preset_id\":\"fire\",\"zone\":\"\",\"rel_x\":0,\"rel_y\":0}}]}}";

            var records = ParticleInstanceSerializer.Deserialize(json, null);

            Assert.AreEqual(1f, records[0].ScaleMultiplier, 0.0001f,
                "Missing scale_multiplier must default to 1.0.");
        }

        [Test]
        public void Deserialize_EmptyZone_DoesNotThrow()
        {
            string guid = System.Guid.NewGuid().ToString("N");
            string json = $"{{\"version\":2,\"instances\":[{{\"id\":\"{guid}\",\"preset_id\":\"fire\",\"zone\":\"\",\"rel_x\":50,\"rel_y\":100,\"scale_multiplier\":1.0}}]}}";

            List<ParticleInstanceRecord> records = null;
            Assert.DoesNotThrow(() =>
            {
                records = ParticleInstanceSerializer.Deserialize(json, null);
            }, "Deserialize must not throw for empty zone.");
            Assert.AreEqual(1, records.Count);
        }
    }
}
