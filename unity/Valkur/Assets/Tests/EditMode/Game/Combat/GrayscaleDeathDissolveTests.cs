using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// The corpse crumbles at the END of its window, not on the frame it fell, and is whole
    /// again when a pooled body comes back. The shader half is pinned by the property
    /// existing on both HDR sprite shaders.
    /// </summary>
    [TestFixture]
    public class GrayscaleDeathDissolveTests
    {
        private readonly List<GameObject> _spawned = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private GrayscaleDeath Corpse()
        {
            var go = new GameObject("corpse");
            _spawned.Add(go);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(new Texture2D(16, 32), new Rect(0, 0, 16, 32), new Vector2(0.5f, 0f), 16f);
            return go.AddComponent<GrayscaleDeath>();
        }

        [Test]
        public void TheBody_IsWholeForMostOfTheWindow_AndGoneAtTheEnd()
        {
            var d = Corpse();
            d.TriggerDeath();                       // fade duration: the 0.5 s default
            d.Advance(0.5f * 0.5f);                 // half way: darkening only
            Assert.That(d.DissolveAmount, Is.EqualTo(0f), "A kill with a corpse that starts crumbling at once is a kill with no corpse.");
            d.Advance(0.5f * 0.3f);                 // 80 %: inside the tail
            Assert.That(d.DissolveAmount, Is.GreaterThan(0f).And.LessThan(1f));
            d.Advance(0.5f);                        // past the end
            Assert.That(d.DissolveAmount, Is.EqualTo(1f).Within(1e-4f));

            var mpb = new MaterialPropertyBlock();
            d.GetComponent<SpriteRenderer>().GetPropertyBlock(mpb);
            Assert.That(mpb.GetFloat("_Dissolve"), Is.EqualTo(1f).Within(1e-4f), "Written to the renderer's own block.");
        }

        [Test]
        public void APooledBody_ComesBackWhole()
        {
            var d = Corpse();
            d.TriggerDeath();
            d.Advance(2f);
            Assert.That(d.DissolveAmount, Is.EqualTo(1f).Within(1e-4f));
            d.ResetTint();
            Assert.That(d.DissolveAmount, Is.EqualTo(0f));
            var mpb = new MaterialPropertyBlock();
            d.GetComponent<SpriteRenderer>().GetPropertyBlock(mpb);
            Assert.That(mpb.GetFloat("_Dissolve"), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void BothHdrSpriteShaders_CarryTheDissolve()
        {
            foreach (var name in new[] { "Valkur/SpriteHDRTint", "Valkur/SpriteHDRTintLit" })
            {
                var shader = Shader.Find(name);
                Assert.IsNotNull(shader, name);
                Assert.That(shader.FindPropertyIndex("_Dissolve"), Is.GreaterThanOrEqualTo(0), $"{name} has no _Dissolve.");
                Assert.IsTrue(shader.isSupported, $"{name} does not compile.");
            }
        }
    }
}
