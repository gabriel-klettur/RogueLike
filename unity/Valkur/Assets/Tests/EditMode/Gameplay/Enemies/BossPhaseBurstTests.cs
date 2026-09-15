using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Enemies;

namespace Valkur.Tests.EditMode.Gameplay.Enemies
{
    /// <summary>
    /// A phase change bursts a ring from the boss's feet. Edit Mode runs no coroutines, so what
    /// is pinned is the ring's birth: additive, on the VFX layer, unparented.
    /// </summary>
    [TestFixture]
    public class BossPhaseBurstTests
    {
        private readonly List<GameObject> _spawned = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            var ring = GameObject.Find("PhaseBurstRing");
            if (ring != null) Object.DestroyImmediate(ring);
        }

        [Test]
        public void ThePhaseBurst_SpawnsAnAdditiveRing_OnTheVFXLayer()
        {
            var go = new GameObject("boss");
            _spawned.Add(go);
            go.transform.localScale = new Vector3(3f, 3f, 1f);   // a scaled root must not scale the ring
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(new Texture2D(16, 32), new Rect(0, 0, 16, 32), new Vector2(0.5f, 0f), 16f);
            go.AddComponent<Health>().Initialize(1000);
            var boss = go.AddComponent<BossPhaseController>();

            boss.PlayPhaseBurst();

            var ring = GameObject.Find("PhaseBurstRing");
            Assert.IsNotNull(ring, "The one moment the fight changes shape produced no pixel on the creature.");
            var rsr = ring.GetComponent<SpriteRenderer>();
            Assert.That(rsr.sortingLayerName, Is.EqualTo(SortingConfig.LAYER_VFX));
            Assert.That(rsr.sharedMaterial.shader.name, Does.Contain("Additive"), "Light, not matter.");
            Assert.IsNull(ring.transform.parent, "Unparented: a scaled boss root would scale the ring's radius.");
        }
    }
}
