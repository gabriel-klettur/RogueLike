using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// What a floating damage number says beyond the amount: the element in its hue, the
    /// critical in its size and its gold, a shadow under it so it reads on any ground, and a
    /// sorting order that puts it OVER the bars of the creature it rose from.
    /// </summary>
    [TestFixture]
    public class DamageNumberLookTests
    {
        private readonly List<GameObject> _spawned = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        [Test]
        public void ACritical_IsGold_AndBigger_WhateverTheElement()
        {
            Assert.That(DamageNumberPalette.ColourFor(SpellElement.Fire, true), Is.EqualTo(DamageNumberPalette.Critical));
            Assert.That(DamageNumberPalette.ColourFor(null, true), Is.EqualTo(DamageNumberPalette.Critical));
            Assert.That(DamageNumberPalette.SizeFor(true), Is.GreaterThan(DamageNumberPalette.SizeFor(false) * 1.4f),
                "A crit that is the same size as a hit is a stat the player cannot see working.");
        }

        [Test]
        public void TheElement_PicksTheHue_AndUntypedStaysRed()
        {
            var fire = DamageNumberPalette.ColourFor(SpellElement.Fire, false);
            var ice  = DamageNumberPalette.ColourFor(SpellElement.Ice, false);
            Assert.That(fire.r, Is.GreaterThan(fire.b), "Fire is warm.");
            Assert.That(ice.b, Is.GreaterThan(ice.r), "Ice is cold.");
            Assert.That(DamageNumberPalette.ColourFor(null, false), Is.EqualTo(DamageNumberPalette.Plain));
        }

        [Test]
        public void TheNumber_SortsOverTheBarsOfTheCreatureItRoseFrom()
        {
            var go = new GameObject("dmg");
            _spawned.Add(go);
            go.transform.position = new Vector3(3f, 20f, 0f);
            var num = go.AddComponent<FloatingDamageNumber>();
            num.Initialize(42, Color.red, critical: true);

            Assert.That(num.Label.sortingLayerID, Is.EqualTo(SortingLayer.NameToID(SortingConfig.LAYER_UI_WORLD)),
                "UI_World: the layer the ambient light leaves alone, where the bars live.");
            int barBase = SortingConfig.ComputeSortingOrder(SortingConfig.Z_UI, 20f);
            Assert.That(num.Label.sortingOrder, Is.GreaterThan(barBase + 30),
                "Above the whole span of the bar rig, or it rises behind the health bar.");
            Assert.That(num.Label.fontSize, Is.EqualTo(DamageNumberPalette.CriticalSize));
            Assert.IsTrue(num.IsCritical);
            Assert.IsNotNull(go.transform.Find("Shadow"), "A shadow under the digits: a red number over a red wall.");
        }

        [Test]
        public void Health_AnnotatesTheBlowInFlight()
        {
            var go = new GameObject("victim");
            _spawned.Add(go);
            var health = go.AddComponent<Health>();
            health.Initialize(100);

            bool sawCrit = false; SpellElement? sawElement = null;
            health.OnDamaged += _ => { sawCrit = health.LastHitWasCritical; sawElement = health.LastHitElement; };
            health.TakeDamage(10, null, SpellElement.Ice, critical: true);
            Assert.IsTrue(sawCrit);
            Assert.That(sawElement, Is.EqualTo(SpellElement.Ice));

            health.TakeDamage(10, null, null);
            Assert.IsFalse(health.LastHitWasCritical, "The next blow overwrites it.");
        }
    }
}
