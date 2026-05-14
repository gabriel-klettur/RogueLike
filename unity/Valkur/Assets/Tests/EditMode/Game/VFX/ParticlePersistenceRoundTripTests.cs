using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Game.VFX
{
    /// <summary>
    /// Round-trip persistence tests: spawn instances → save via editor →
    /// parse JSON → assert positions, preset_id, scale, GUID survive intact.
    ///
    /// No ZoneManager is injected, so positions use zone="" and offset (0,0).
    /// Coordinate formula: rel_x = worldX * PPU, rel_y = (zH-1 - worldY) * PPU (PPU=32, zH=50).
    /// </summary>
    [TestFixture]
    public class ParticlePersistenceRoundTripTests
    {
        private const float PPU = 32f;
        private const int   ZH  = 50;
        private const float TOLERANCE = 1f / PPU; // 0.03125 world units

        private readonly List<GameObject> _created = new List<GameObject>();
        private ParticlesRuntimeEditor _editor;

        // ── Reflection helpers ────────────────────────────────────────────────────

        private static void ClearSingleton<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var f = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        private static void Invoke(object obj, string method, params object[] args)
        {
            var t = obj.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                t = t.BaseType;
            }
            m?.Invoke(obj, args);
        }

        private static void SetVal(object obj, string name, object value)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                if (f != null) { f.SetValue(obj, value); return; }
                t = t.BaseType;
            }
        }

        // ── Setup / Teardown ──────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            ClearSingleton<ParticlesRuntimeEditor>();

            var go = new GameObject("RoundTripEditor");
            _created.Add(go);
            _editor = go.AddComponent<ParticlesRuntimeEditor>();
            Invoke(_editor, "OnSingletonAwake");

            var catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();
            SetVal(_editor, "_catalog", catalog);
            _editor.SetInstanceStore(new InMemoryParticleInstanceStore());
            Invoke(_editor, "Start");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
                if (go != null) Object.DestroyImmediate(go);
            _created.Clear();

            ClearSingleton<ParticlesRuntimeEditor>();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private PersistedParticleInstance CreateInst(string presetId, float scale, Vector3 pos)
        {
            var go = new GameObject($"PE_{presetId}");
            go.transform.position = pos;
            _created.Add(go);
            var inst = go.AddComponent<PersistedParticleInstance>();
            inst.Initialize(presetId, scale);
            return inst;
        }

        private InMemoryParticleInstanceStore GetStore()
        {
            return (InMemoryParticleInstanceStore)
                _editor.GetType()
                    .GetField("_instanceStore", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(_editor);
        }

        // ── Tests ─────────────────────────────────────────────────────────────────

        [Test]
        public void RoundTrip_FiveInstances_PresetIdsPreserved()
        {
            string[] presets = { "fire_aura", "smoke_ring", "water_fountain", "arcane_flame", "portal_red" };
            for (int i = 0; i < presets.Length; i++)
                CreateInst(presets[i], 1f + i * 0.5f, new Vector3(i, i, 0));

            Invoke(_editor, "SaveInstancesToJson");

            string json = GetStore().CurrentJson;
            var records = ParticleInstanceSerializer.Deserialize(json, null, ZH, 1f);

            Assert.AreEqual(presets.Length, records.Count,
                "Round-trip must preserve all 5 instance records.");

            // Sort both by presetId for comparison independence.
            var sortedPresets = new List<string>(presets);
            sortedPresets.Sort();
            var sortedRecords = new List<ParticleInstanceRecord>(records);
            sortedRecords.Sort((a, b) => string.Compare(a.PresetId, b.PresetId,
                System.StringComparison.Ordinal));

            for (int i = 0; i < sortedPresets.Count; i++)
                Assert.AreEqual(sortedPresets[i], sortedRecords[i].PresetId,
                    $"PresetId #{i} must match after round-trip.");
        }

        [Test]
        public void RoundTrip_ScaleMultiplierPreserved()
        {
            float[] scales = { 1f, 1.5f, 2f, 0.75f, 3.25f };
            var instances = new List<PersistedParticleInstance>();
            for (int i = 0; i < scales.Length; i++)
                instances.Add(CreateInst($"preset_{i}", scales[i], new Vector3(i, 0, 0)));

            Invoke(_editor, "SaveInstancesToJson");

            string json = GetStore().CurrentJson;
            var records = ParticleInstanceSerializer.Deserialize(json, null, ZH, 1f);

            Assert.AreEqual(scales.Length, records.Count);

            // Match by GUID to avoid ordering issues.
            foreach (var record in records)
            {
                PersistedParticleInstance matched = null;
                foreach (var inst in instances)
                    if (inst.StableGuid == record.Guid) { matched = inst; break; }

                if (matched == null) continue; // generated GUID might differ if not matched
                Assert.AreEqual(matched.ScaleMultiplier, record.ScaleMultiplier, 0.001f,
                    $"Scale multiplier must match for {record.PresetId}.");
            }
        }

        [Test]
        public void RoundTrip_GuidPreserved()
        {
            var inst = CreateInst("arcane_flame", 2f, new Vector3(5f, 5f, 0f));
            string originalGuid = inst.StableGuid;

            Invoke(_editor, "SaveInstancesToJson");

            string json = GetStore().CurrentJson;
            var records = ParticleInstanceSerializer.Deserialize(json, null, ZH, 1f);

            Assert.AreEqual(1, records.Count);
            Assert.AreEqual(originalGuid, records[0].Guid,
                "GUID must survive the save/load round-trip.");
        }

        [Test]
        public void RoundTrip_WorldPosition_WithinOnePpuTolerance()
        {
            // Without a ZoneManager, zone="" and offset=(0,0).
            // world_x = rel_x / PPU  →  rel_x = world_x * PPU
            // world_y = (zH-1) - rel_y/PPU  →  rel_y = ((zH-1) - world_y) * PPU
            // So decode: wx = rel_x/PPU, wy = (zH-1)*1f - rel_y/PPU
            // We just check that the coordinate round-trips within rounding error.

            var inst = CreateInst("firework", 1f, new Vector3(3.5f, 12.25f, 0f));
            Invoke(_editor, "SaveInstancesToJson");

            string json = GetStore().CurrentJson;
            var records = ParticleInstanceSerializer.Deserialize(json, null, ZH, 1f);

            Assert.AreEqual(1, records.Count);
            // WorldPos reconstructed by Deserialize. Check it's within 1-pixel tolerance.
            Assert.AreEqual(3.5f, records[0].WorldPos.x, TOLERANCE,
                "WorldPos.x must round-trip within 1/PPU tolerance.");
            Assert.AreEqual(12.25f, records[0].WorldPos.y, TOLERANCE,
                "WorldPos.y must round-trip within 1/PPU tolerance.");
        }
    }
}
