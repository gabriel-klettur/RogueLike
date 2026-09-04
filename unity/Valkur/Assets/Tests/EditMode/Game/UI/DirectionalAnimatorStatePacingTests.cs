using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// Pins the PER-STATE playback multiplier — the third and last of the three dials that
    /// decide how long one frame is on screen.
    ///
    /// <para>It exists because the other two cannot express "this creature breathes slowly
    /// and walks normally". The entity-wide multiplier moves every state at once, so slowing
    /// an idle to a drowsy breath makes the character wade; and the variant multiplier is
    /// answered by <c>PacingOf</c>, which returns the neutral default for variant -1 — which
    /// is what idle, walk and chase always are, since only Attack and Cast carry variants.
    /// Gatita is the entity it was added for.</para>
    ///
    /// <para>The three COMPOSE rather than override, and that is the property most worth
    /// pinning: a slow idle on a fast creature has to be both.</para>
    /// </summary>
    public class DirectionalAnimatorStatePacingTests
    {
        private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                    UnityEngine.Object.DestroyImmediate(_created[i]);
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
            var texture = new Texture2D(Mathf.Max(1, count), 1);
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

        private DirectionalAnimator Bound(int framesPerDirection)
        {
            var anim = CreateAnimator();
            var set = SetOf(framesPerDirection);
            anim.SetSpriteSets(set, set, set, set, set, set, set);
            return anim;
        }

        private static float FrameInterval(DirectionalAnimator anim)
            => (float)typeof(DirectionalAnimator).GetField("frameInterval", Instance).GetValue(anim);

        // ---- Default is the identity multiplier ------------------------------

        [Test]
        public void EveryState_DefaultsToOne()
        {
            var anim = CreateAnimator();

            foreach (DirectionalAnimator.AnimState state in
                     Enum.GetValues(typeof(DirectionalAnimator.AnimState)))
            {
                Assert.AreEqual(1f, anim.StateSpeedOf(state), 0.0001f,
                    $"{state} on an animator nobody paced must play at the entity's own rate. " +
                    "Nearly every shipped entity is in this state and must be unaffected.");
            }
        }

        [Test]
        public void UnpacedEntity_StateLengthIsUnchanged()
        {
            var anim = Bound(4);
            anim.SetState(DirectionalAnimator.AnimState.Idle, DirectionalAnimator.Direction.South);

            Assert.AreEqual(4 * FrameInterval(anim),
                anim.GetStateLength(DirectionalAnimator.AnimState.Idle), 0.0001f,
                "Adding per-state pacing must not retime a single already-shipped entity.");
        }

        // ---- The dial itself -------------------------------------------------

        [Test]
        public void HalfSpeed_DoublesThatStatesLength()
        {
            var anim = Bound(6);
            anim.SetState(DirectionalAnimator.AnimState.Idle, DirectionalAnimator.Direction.South);
            float baseline = anim.GetStateLength(DirectionalAnimator.AnimState.Idle);

            anim.SetStateSpeed(DirectionalAnimator.AnimState.Idle, 0.5f);

            Assert.AreEqual(baseline * 2f,
                anim.GetStateLength(DirectionalAnimator.AnimState.Idle), 0.0001f,
                "A 0.5x idle must take twice as long, without changing its frame count.");
        }

        [Test]
        public void PacingOneState_LeavesTheOthersAlone()
        {
            var anim = Bound(5);
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.South);
            float walkBefore = anim.GetStateLength(DirectionalAnimator.AnimState.Walk);

            anim.SetStateSpeed(DirectionalAnimator.AnimState.Idle, 0.25f);

            Assert.AreEqual(walkBefore,
                anim.GetStateLength(DirectionalAnimator.AnimState.Walk), 0.0001f,
                "This is the whole reason the dial is per state: a slow idle must not " +
                "make the character wade. The entity-wide multiplier already moves both.");
            Assert.AreEqual(0.25f, anim.StateSpeedOf(DirectionalAnimator.AnimState.Idle), 0.0001f);
            Assert.AreEqual(1f, anim.StateSpeedOf(DirectionalAnimator.AnimState.Walk), 0.0001f);
        }

        [Test]
        public void NonPositiveSpeed_IsClampedRatherThanFreezingTheState()
        {
            var anim = Bound(3);
            anim.SetStateSpeed(DirectionalAnimator.AnimState.Idle, 0f);

            Assert.Greater(anim.StateSpeedOf(DirectionalAnimator.AnimState.Idle), 0f,
                "A zero multiplier would divide the frame interval to infinity and stop the " +
                "animation dead — an authoring slip must degrade to 'very slow', not to a freeze.");
        }

        // ---- Composition with the other two dials ----------------------------

        [Test]
        public void EntityAndStateMultipliers_Compose()
        {
            var anim = Bound(4);
            anim.SetState(DirectionalAnimator.AnimState.Idle, DirectionalAnimator.Direction.South);
            float raw = 4 * FrameInterval(anim);

            anim.SetAnimationSpeedMultiplier(2f);   // a fast creature
            anim.SetStateSpeed(DirectionalAnimator.AnimState.Idle, 0.5f); // with a slow breath

            Assert.AreEqual(raw,
                anim.GetStateLength(DirectionalAnimator.AnimState.Idle), 0.0001f,
                "2x entity against a 0.5x state must land back on the raw rate. If one " +
                "overrode the other, a slow idle on a fast creature could not be authored.");
        }

        [Test]
        public void StateAndVariantMultipliers_Compose()
        {
            var anim = Bound(4);
            var variant = SetOf(4);
            anim.SetVariants(
                DirectionalAnimator.AnimState.Cast,
                new[] { variant },
                null,
                new[] { new DirectionalAnimator.VariantPacing { SpeedMultiplier = 4f } });

            anim.SetState(DirectionalAnimator.AnimState.Cast, DirectionalAnimator.Direction.South, 0);
            float variantOnly = anim.GetStateLength(DirectionalAnimator.AnimState.Cast, 0);

            anim.SetStateSpeed(DirectionalAnimator.AnimState.Cast, 0.5f);

            Assert.AreEqual(variantOnly * 2f,
                anim.GetStateLength(DirectionalAnimator.AnimState.Cast, 0), 0.0001f,
                "A variant that must finish inside its beat (the dash's 4x charge) and a " +
                "state-wide pace answer different questions, so they multiply.");
        }

        [Test]
        public void StatePacing_DoesNotLeakIntoPacingOf()
        {
            var anim = Bound(3);
            anim.SetStateSpeed(DirectionalAnimator.AnimState.Attack, 0.5f);

            Assert.AreEqual(1f,
                anim.PacingOf(DirectionalAnimator.AnimState.Attack, -1).SpeedMultiplier, 0.0001f,
                "PacingOf is public and answers 'how is this VARIANT paced'. Callers sizing " +
                "one action's window read it; it must not silently start reporting a " +
                "state-wide dial without their call changing.");
        }

        // ---- The data seam ---------------------------------------------------

        [Test]
        public void StateNames_MatchTheEnumPositionally()
        {
            var values = Enum.GetValues(typeof(DirectionalAnimator.AnimState));

            Assert.AreEqual(values.Length, EntityAnimationBinder.StateNames.Length,
                "EntityAnimationBinder.StateNames is the fifth place the states are " +
                "enumerated positionally. A new AnimState must be added here too.");

            for (int i = 0; i < values.Length; i++)
            {
                var state = (DirectionalAnimator.AnimState)values.GetValue(i);
                Assert.AreEqual(state.ToString().ToLowerInvariant(),
                    EntityAnimationBinder.StateNames[i],
                    $"Index {i} of StateNames must name {state}. The index IS the enum value, " +
                    "so a reorder would silently pace the wrong state.");
            }
        }

        [Test]
        public void UnauthoredConfig_ReportsOneForEveryState()
        {
            var config = new EntityAssetConfig();

            foreach (string state in EntityAnimationBinder.StateNames)
            {
                Assert.AreEqual(1f, config.StateSpeedMultiplier(state), 0.0001f,
                    $"'{state}' is unauthored on nearly every entity and must read as neutral.");
            }
        }

        [Test]
        public void ConfigLookup_IsCaseInsensitive()
        {
            var config = new EntityAssetConfig
            {
                statePacing = new List<StatePacing>
                {
                    new StatePacing { state = "Idle", animationSpeedMultiplier = 0.4f },
                },
            };

            Assert.AreEqual(0.4f, config.StateSpeedMultiplier("idle"), 0.0001f,
                "The state name is typed by hand in the Inspector and generated in lower " +
                "case by the frame pipeline; a casing slip must not fail silently by " +
                "falling back to the neutral 1, which looks exactly like nothing being wired.");
        }
    }
}
