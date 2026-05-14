using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Valkur.Tests.EditMode.Game.VFX
{
    /// <summary>
    /// Unit tests for <see cref="Valkur.Gameplay.VFX.PersistedParticleInstance"/>.
    /// Covers GUID uniqueness, Initialize, and Restore contracts.
    /// </summary>
    [TestFixture]
    public class PersistedParticleInstanceTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
                if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
        }

        private Valkur.Gameplay.VFX.PersistedParticleInstance Create(string name = "test")
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go.AddComponent<Valkur.Gameplay.VFX.PersistedParticleInstance>();
        }

        // ── Initialize ────────────────────────────────────────────────────────────

        [Test]
        public void Initialize_AssignsNonEmptyGuid()
        {
            var inst = Create();
            inst.Initialize("fire_aura", 1.5f);

            Assert.IsFalse(string.IsNullOrEmpty(inst.StableGuid),
                "Initialize must assign a non-empty GUID.");
        }

        [Test]
        public void Initialize_AssignsPresetId()
        {
            var inst = Create();
            inst.Initialize("water_fountain", 2f);

            Assert.AreEqual("water_fountain", inst.PresetId,
                "Initialize must set the preset id.");
        }

        [Test]
        public void Initialize_AssignsScaleMultiplier()
        {
            var inst = Create();
            inst.Initialize("portal_blue", 3.5f);

            Assert.AreEqual(3.5f, inst.ScaleMultiplier, 0.0001f,
                "Initialize must set the scale multiplier.");
        }

        [Test]
        public void Initialize_TwoInstances_HaveDifferentGuids()
        {
            var a = Create("a");
            var b = Create("b");
            a.Initialize("smoke", 1f);
            b.Initialize("smoke", 1f);

            Assert.AreNotEqual(a.StableGuid, b.StableGuid,
                "Two different Initialize calls must produce distinct GUIDs.");
        }

        [Test]
        public void Initialize_DefaultScale_IsOne()
        {
            var inst = Create();
            inst.Initialize("arcane_flame");

            Assert.AreEqual(1f, inst.ScaleMultiplier, 0.0001f,
                "Initialize without scale arg must default to 1.");
        }

        // ── Restore ───────────────────────────────────────────────────────────────

        [Test]
        public void Restore_PreservesProvidedGuid()
        {
            var inst = Create();
            string guid = "abcdef1234567890abcdef1234567890";
            inst.Restore("fire_aura", guid, 2f);

            Assert.AreEqual(guid, inst.StableGuid,
                "Restore must preserve the provided GUID string.");
        }

        [Test]
        public void Restore_EmptyGuid_GeneratesNewGuid()
        {
            var inst = Create();
            inst.Restore("smoke", "", 1f);

            Assert.IsFalse(string.IsNullOrEmpty(inst.StableGuid),
                "Restore with empty guid must generate a new GUID.");
        }

        [Test]
        public void Restore_NullGuid_GeneratesNewGuid()
        {
            var inst = Create();
            inst.Restore("smoke", null, 1f);

            Assert.IsFalse(string.IsNullOrEmpty(inst.StableGuid),
                "Restore with null guid must generate a new GUID.");
        }

        // ── SetScaleMultiplier ────────────────────────────────────────────────────

        [Test]
        public void SetScaleMultiplier_UpdatesValue_WithoutChangingGuid()
        {
            var inst = Create();
            inst.Initialize("explosion", 1f);
            string originalGuid = inst.StableGuid;

            inst.SetScaleMultiplier(4f);

            Assert.AreEqual(4f, inst.ScaleMultiplier, 0.0001f,
                "SetScaleMultiplier must update the scale.");
            Assert.AreEqual(originalGuid, inst.StableGuid,
                "SetScaleMultiplier must not change the GUID.");
        }
    }
}
