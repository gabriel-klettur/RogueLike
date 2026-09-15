using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Tests.EditMode.Gameplay.Combat.StatusEffects
{
    /// <summary>
    /// A status effect has a shape on the body now, not only a tint: one emitter per kind,
    /// on while the effect is, sorted just over the body, additive for light and alpha for
    /// matter. Edit Mode drives the seams directly; the manager's events are what Play Mode
    /// pulls them through.
    /// </summary>
    [TestFixture]
    public class StatusEffectVisualsTests
    {
        private readonly List<GameObject> _spawned = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private StatusEffectVisuals Body()
        {
            var go = new GameObject("body");
            _spawned.Add(go);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(new Texture2D(16, 32), new Rect(0, 0, 16, 32), new Vector2(0.5f, 0f), 16f);
            sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            sr.sortingOrder = 4000;
            go.AddComponent<StatusEffectManager>();
            return go.AddComponent<StatusEffectVisuals>();
        }

        [Test]
        public void ABurn_HasEmbers_JustOverTheBody()
        {
            var v = Body();
            v.Show(StatusEffectKind.Burn);
            var ps = v.EmitterFor(StatusEffectKind.Burn);
            Assert.IsNotNull(ps);
            Assert.IsTrue(v.IsShowing(StatusEffectKind.Burn));
            var r = ps.GetComponent<ParticleSystemRenderer>();
            Assert.That(r.sortingLayerName, Is.EqualTo(SortingConfig.LAYER_ENTITIES));
            Assert.That(r.sortingOrder, Is.EqualTo(4001), "One order over the body: embers in front of the creature, under whoever stands before it.");
            Assert.That(ps.main.startColor.color.r, Is.GreaterThan(ps.main.startColor.color.b), "Embers are warm.");
        }

        [Test]
        public void Hiding_StopsTheEmission_AndKeepsTheEmitter()
        {
            var v = Body();
            v.Show(StatusEffectKind.Poison);
            v.Hide(StatusEffectKind.Poison);
            Assert.IsFalse(v.IsShowing(StatusEffectKind.Poison));
            Assert.IsNotNull(v.EmitterFor(StatusEffectKind.Poison), "Pooled: the next poison reuses it.");
            v.Show(StatusEffectKind.Poison);
            Assert.IsTrue(v.IsShowing(StatusEffectKind.Poison));
        }

        [Test]
        public void Light_IsAdditive_Matter_IsNot()
        {
            Assert.IsTrue(StatusEffectVisuals.IsAdditive(StatusEffectKind.Burn));
            Assert.IsTrue(StatusEffectVisuals.IsAdditive(StatusEffectKind.Freeze));
            Assert.IsFalse(StatusEffectVisuals.IsAdditive(StatusEffectKind.Poison), "A drip of poison is matter, and adds no light.");
            Assert.IsFalse(StatusEffectVisuals.IsAdditive(StatusEffectKind.Slow));
        }

        [Test]
        public void EveryKind_HasAColour_AndTheyDiffer()
        {
            var seen = new HashSet<Color>();
            foreach (StatusEffectKind kind in System.Enum.GetValues(typeof(StatusEffectKind)))
            {
                var c = StatusEffectVisuals.ColourFor(kind);
                Assert.That(c.a, Is.GreaterThan(0f), $"{kind} draws nothing.");
                seen.Add(c);
            }
            Assert.That(seen.Count, Is.GreaterThanOrEqualTo(System.Enum.GetValues(typeof(StatusEffectKind)).Length - 1),
                "Two statuses in one colour are one status to the player.");
        }
    }
}
