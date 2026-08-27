using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// Pins <see cref="DirectionalAnimator.SetAnimationSpeedMultiplier"/> — the per-entity
    /// playback speed added so a monster's swing can be retimed WITHOUT retiming its
    /// damage window. Before this, the only lever on swing duration was frame COUNT
    /// (<c>GetStateLength = frames.Length * frameInterval</c>), and
    /// <c>AttackState._attackDuration</c> reads that same length — so shortening a swing
    /// by deleting frames also shortened (and re-timed) the hit. See CLAUDE.md
    /// "Retiming an attack animation retimes its DAMAGE".
    /// </summary>
    public class DirectionalAnimatorAnimationSpeedTests
    {
        private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        // ---- Helpers --------------------------------------------------------

        private DirectionalAnimator CreateAnimator()
        {
            var go = new GameObject("TestAnimator");
            _created.Add(go);
            return go.AddComponent<DirectionalAnimator>();
        }

        private List<Sprite> CreateFrames(int count)
        {
            var texture = new Texture2D(count, 1);
            _created.Add(texture);

            var frames = new List<Sprite>(count);
            for (int i = 0; i < count; i++)
            {
                var sprite = Sprite.Create(texture, new Rect(i, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
                frames.Add(sprite);
                _created.Add(sprite);
            }
            return frames;
        }

        private DirectionalAnimator.DirectionalSpriteSet SetOf(int framesPerDirection)
            => DirectionalAnimator.CreateSetFromLinearFrames(CreateFrames(8 * framesPerDirection));

        private static float FrameInterval(DirectionalAnimator anim)
            => (float)typeof(DirectionalAnimator).GetField("frameInterval", Instance).GetValue(anim);

        // ---- Default (no call) is the identity multiplier --------------------

        [Test]
        public void Default_MultiplierIsOne()
        {
            var anim = CreateAnimator();
            Assert.AreEqual(1f, anim.AnimationSpeedMultiplier,
                "An entity whose visuals were never bound through EntityAnimationBinder " +
                "(e.g. a raw AddComponent in a test) must animate at the authored speed.");
        }

        [Test]
        public void Default_GetStateLength_MatchesRawFrameIntervalMath()
        {
            var anim = CreateAnimator();
            anim.SetSpriteSets(SetOf(3), SetOf(3), SetOf(3), SetOf(3), SetOf(3), SetOf(3), SetOf(3));
            anim.SetState(DirectionalAnimator.AnimState.Attack, DirectionalAnimator.Direction.South);

            float interval = FrameInterval(anim);
            Assert.AreEqual(3 * interval,
                anim.GetStateLength(DirectionalAnimator.AnimState.Attack), 0.0001f,
                "Every shipped monster must keep its exact current 0.15s/frame timing " +
                "until an author explicitly sets animationSpeedMultiplier.");
        }

        // ---- Explicit multiplier changes state length, not frame count -------

        [Test]
        public void DoubleSpeed_HalvesStateLength()
        {
            var anim = CreateAnimator();
            anim.SetSpriteSets(SetOf(4), SetOf(4), SetOf(4), SetOf(4), SetOf(4), SetOf(4), SetOf(4));
            anim.SetState(DirectionalAnimator.AnimState.Attack, DirectionalAnimator.Direction.South);
            float baseline = anim.GetStateLength(DirectionalAnimator.AnimState.Attack);

            anim.SetAnimationSpeedMultiplier(2f);

            Assert.AreEqual(2f, anim.AnimationSpeedMultiplier);
            Assert.AreEqual(baseline / 2f,
                anim.GetStateLength(DirectionalAnimator.AnimState.Attack), 0.0001f,
                "2x speed must halve the swing's real-world duration WITHOUT touching " +
                "frame count — the whole point of the multiplier over deleting frames.");
        }

        [Test]
        public void HalfSpeed_DoublesStateLength()
        {
            var anim = CreateAnimator();
            anim.SetSpriteSets(SetOf(4), SetOf(4), SetOf(4), SetOf(4), SetOf(4), SetOf(4), SetOf(4));
            anim.SetState(DirectionalAnimator.AnimState.Attack, DirectionalAnimator.Direction.South);
            float baseline = anim.GetStateLength(DirectionalAnimator.AnimState.Attack);

            anim.SetAnimationSpeedMultiplier(0.5f);

            Assert.AreEqual(baseline * 2f,
                anim.GetStateLength(DirectionalAnimator.AnimState.Attack), 0.0001f);
        }

        // ---- The <=0 "field never authored" sentinel --------------------------

        [Test]
        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(-100f)]
        public void NonPositiveMultiplier_CollapsesToIdentity(float raw)
        {
            // A struct field with no matching key in an asset serialized before
            // animationSpeedMultiplier existed deserializes to the CLR default, 0 — not
            // the C# field's absent initializer. Every shipped monster must therefore
            // read back as 1 (unchanged), not as "animate infinitely fast" or throw.
            var anim = CreateAnimator();

            anim.SetAnimationSpeedMultiplier(raw);

            Assert.AreEqual(1f, anim.AnimationSpeedMultiplier,
                $"multiplier {raw} must collapse to the identity, not propagate as-is.");
        }

        [Test]
        public void VerySmallPositiveMultiplier_IsClampedAboveZero()
        {
            var anim = CreateAnimator();
            anim.SetSpriteSets(SetOf(2), SetOf(2), SetOf(2), SetOf(2), SetOf(2), SetOf(2), SetOf(2));
            anim.SetState(DirectionalAnimator.AnimState.Attack, DirectionalAnimator.Direction.South);

            anim.SetAnimationSpeedMultiplier(0.0000001f);

            float length = anim.GetStateLength(DirectionalAnimator.AnimState.Attack);
            Assert.IsFalse(float.IsInfinity(length), "an absurdly small multiplier must not produce Infinity.");
            Assert.IsFalse(float.IsNaN(length), "an absurdly small multiplier must not produce NaN.");
            Assert.Greater(length, 0f);
        }
    }
}
