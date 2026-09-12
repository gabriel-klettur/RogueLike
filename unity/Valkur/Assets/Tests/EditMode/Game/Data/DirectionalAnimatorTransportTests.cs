using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// <see cref="DirectionalAnimator"/>'s transport: pause, step, scrub, and the index of the
    /// frame actually on screen.
    ///
    /// <para>These run in Edit Mode, where Unity calls no Awake and no Update — so the animator
    /// caches no renderer and the clock never ticks. Neither matters here: the cursor is moved
    /// by <see cref="DirectionalAnimator.RestartCurrentState"/> and the transport methods, all
    /// of which are synchronous, and <c>ApplyFrame</c> records the index BEFORE it touches the
    /// renderer it does not have.</para>
    /// </summary>
    [TestFixture]
    public class DirectionalAnimatorTransportTests
    {
        private readonly List<Object> _created = new List<Object>();
        private GameObject _go;
        private DirectionalAnimator _animator;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _go = new GameObject("TransportRig");
            _created.Add(_go);
            _animator = _go.AddComponent<DirectionalAnimator>();

            // 24 frames = 3 per direction across the eight contiguous buckets.
            var set = DirectionalAnimator.CreateSetFromLinearFrames(Frames(24));
            _animator.SetSpriteSets(set, set, set, set, set, set, set, false);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        private List<Sprite> Frames(int count)
        {
            var frames = new List<Sprite>(count);
            for (int i = 0; i < count; i++)
            {
                var tex = new Texture2D(2, 2);
                _created.Add(tex);
                var sprite = Sprite.Create(tex, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0f), 16f);
                sprite.name = $"f{i}";
                _created.Add(sprite);
                frames.Add(sprite);
            }
            return frames;
        }

        // ── The index ────────────────────────────────────────────────────────────

        [Test]
        public void DisplayedFrameIndex_IsTheFrameDrawn_NotTheCursor()
        {
            _animator.SetState(DirectionalAnimator.AnimState.Attack, DirectionalAnimator.Direction.South);
            _animator.RestartCurrentState();

            // RestartCurrentState draws frame 0 and leaves the cursor pointing at 1. A reader
            // that showed the cursor would name a frame nobody has seen yet.
            Assert.That(_animator.DisplayedFrameIndex, Is.EqualTo(0));
        }

        [Test]
        public void FramesFor_ReportsTheFramesOfOneDirection()
        {
            var frames = _animator.FramesFor(DirectionalAnimator.AnimState.Attack,
                                             DirectionalAnimator.Direction.South);
            Assert.That(frames, Is.Not.Null.And.Length.EqualTo(3),
                "24 frames cut into eight contiguous buckets is three per direction");

            _animator.SetState(DirectionalAnimator.AnimState.Attack, DirectionalAnimator.Direction.North);
            Assert.That(_animator.CurrentFrameCount, Is.EqualTo(3),
                "the count must follow the state and direction on screen, not the base set");
        }

        // ── Scrub and step ───────────────────────────────────────────────────────

        [Test]
        public void ShowFrame_DrawsThatFrame_AndClampsOutOfRange()
        {
            _animator.SetState(DirectionalAnimator.AnimState.Attack, DirectionalAnimator.Direction.South);

            _animator.ShowFrame(2);
            Assert.That(_animator.DisplayedFrameIndex, Is.EqualTo(2));

            _animator.ShowFrame(99);
            Assert.That(_animator.DisplayedFrameIndex, Is.EqualTo(2),
                "a strip cannot ask for a frame that is not there; the nearest end is the answer");

            _animator.ShowFrame(-4);
            Assert.That(_animator.DisplayedFrameIndex, Is.EqualTo(0));
        }

        [Test]
        public void StepFrame_Wraps_BecauseTheAnimationDoes()
        {
            _animator.SetState(DirectionalAnimator.AnimState.Attack, DirectionalAnimator.Direction.South);
            _animator.ShowFrame(2);

            _animator.StepFrame(1);
            Assert.That(_animator.DisplayedFrameIndex, Is.EqualTo(0),
                "\"next\" on the last frame of a looping cycle is the first frame");

            _animator.StepFrame(-1);
            Assert.That(_animator.DisplayedFrameIndex, Is.EqualTo(2));
        }

        [Test]
        public void Paused_DefaultsOff_SoNothingInGameplayChanges()
        {
            Assert.That(_animator.Paused, Is.False);
        }

        [Test]
        public void ShowFrame_OnAnEmptyState_DoesNothingRatherThanThrow()
        {
            var bare = new GameObject("Bare");
            _created.Add(bare);
            var animator = bare.AddComponent<DirectionalAnimator>();

            Assert.DoesNotThrow(() => animator.ShowFrame(3));
            Assert.DoesNotThrow(() => animator.StepFrame(1));
            Assert.That(animator.CurrentFrameCount, Is.EqualTo(0));
        }

        // ── Variant labels ───────────────────────────────────────────────────────

        [Test]
        public void VariantLabel_CarriesTheAuthoredKey_AndAnsweredNullWhenTheAssetNamedNone()
        {
            var set = DirectionalAnimator.CreateSetFromLinearFrames(Frames(8));
            _animator.SetVariants(DirectionalAnimator.AnimState.Attack,
                                  new[] { set, set },
                                  null, null,
                                  new[] { "punch", null });

            Assert.That(_animator.VariantLabel(DirectionalAnimator.AnimState.Attack, 0), Is.EqualTo("punch"));
            Assert.That(_animator.VariantLabel(DirectionalAnimator.AnimState.Attack, 1), Is.Null,
                "\"the asset did not name this\" and \"the asset called it 1\" are different facts");
            Assert.That(_animator.VariantLabel(DirectionalAnimator.AnimState.Attack, 7), Is.Null);
        }

        [Test]
        public void ReservedSpellKeys_ReportsWhatTheSelectorObeys()
        {
            var set = DirectionalAnimator.CreateSetFromLinearFrames(Frames(8));
            _animator.SetVariants(DirectionalAnimator.AnimState.Cast,
                                  new[] { set, set },
                                  new IReadOnlyList<string>[] { new[] { "fireball" }, null },
                                  null,
                                  new[] { "spell_3", "spell_2" });

            var reserved = _animator.ReservedSpellKeys(DirectionalAnimator.AnimState.Cast, 0);
            Assert.That(reserved, Is.Not.Null.And.Contains("fireball"));
            Assert.That(_animator.ReservedSpellKeys(DirectionalAnimator.AnimState.Cast, 1), Is.Null,
                "a variant with no reservation stays in the generic rotation");
            Assert.That(_animator.VariantForSpell(DirectionalAnimator.AnimState.Cast, "fireball"),
                Is.EqualTo(0), "the label must not change which variant a spell resolves to");
        }

        // ── Pacing ───────────────────────────────────────────────────────────────

        [Test]
        public void FrameIntervalFor_MultipliesAllThreeDials()
        {
            var set = DirectionalAnimator.CreateSetFromLinearFrames(Frames(8));
            _animator.SetVariants(DirectionalAnimator.AnimState.Attack, new[] { set },
                                  null,
                                  new[] { new DirectionalAnimator.VariantPacing { SpeedMultiplier = 2f } });
            _animator.SetAnimationSpeedMultiplier(2f);
            _animator.SetStateSpeed(DirectionalAnimator.AnimState.Attack, 2f);

            float baseInterval = 0.15f;
            float measured = _animator.FrameIntervalFor(DirectionalAnimator.AnimState.Attack, 0);

            Assert.That(measured, Is.EqualTo(baseInterval / 8f).Within(0.0001f),
                "entity x state x variant — three dials that multiply, which is exactly what a " +
                "readout showing only the product cannot say");
        }
    }
}
