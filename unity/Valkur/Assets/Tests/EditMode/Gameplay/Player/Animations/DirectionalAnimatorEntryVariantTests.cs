using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Gameplay.Player.Animations
{
    /// <summary>
    /// Guards how a state that is neither Attack nor Cast chooses among its alternatives.
    ///
    /// Those two are chosen by an ACTION — a swing rotates, a spell reserves by key — and both
    /// go through the three-argument <c>SetState</c>. Everything else has no action to hang a
    /// choice off: nothing "casts" a walk. So a locomotion or reaction variant is picked on
    /// ENTRY to the state, by the two-argument overload, and held for as long as the state is.
    ///
    /// Three properties, and each fails differently:
    ///
    /// * <b>Rotation, not randomness.</b> A random pick repeats the same cycle back to back
    ///   about one entry in N, which reads as the animation having failed to change — the same
    ///   call <c>PlayerController.NextVariant</c> makes for swings.
    /// * <b>No re-roll mid-state.</b> <c>PlayerController.Movement</c> re-asserts the walk
    ///   state every frame and every direction change comes through the same overload, so a
    ///   re-roll would restart the cycle on each step and the character would twitch.
    /// * <b>-1 for a state with no variants.</b> The old body returned <c>_activeVariant</c>
    ///   unconditionally, which was harmless only while Attack and Cast were the sole states
    ///   carrying variants. The moment walk carries four, an index left over from a spellcast
    ///   selects walk variant 3.
    ///
    /// Awake never runs in EditMode, so the renderer is wired by reflection — the same shape as
    /// <see cref="DirectionalAnimatorCastVariantTests"/>.
    /// </summary>
    public class DirectionalAnimatorEntryVariantTests
    {
        private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
        private const int FramesPerDirection = 4;

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        // ---- Helpers --------------------------------------------------------

        private DirectionalAnimator CreateAnimator()
        {
            var go = new GameObject("TestEntryVariantAnimator");
            _created.Add(go);
            var renderer = go.AddComponent<SpriteRenderer>();
            var anim = go.AddComponent<DirectionalAnimator>();
            typeof(DirectionalAnimator).GetField("targetRenderer", Instance).SetValue(anim, renderer);
            return anim;
        }

        private List<Sprite> CreateFrames(string prefix, int count)
        {
            var texture = new Texture2D(count, 1);
            _created.Add(texture);

            var frames = new List<Sprite>(count);
            for (int i = 0; i < count; i++)
            {
                var sprite = Sprite.Create(texture, new Rect(i, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
                sprite.name = $"{prefix}_{i}";
                frames.Add(sprite);
                _created.Add(sprite);
            }
            return frames;
        }

        private DirectionalAnimator.DirectionalSpriteSet SetOf(string prefix)
            => DirectionalAnimator.CreateSetFromLinearFrames(CreateFrames(prefix, 8 * FramesPerDirection));

        private DirectionalAnimator WithWalkVariants(int count)
        {
            var anim = CreateAnimator();
            anim.SetSpriteSets(SetOf("idle"), SetOf("walk"), SetOf("chase"), SetOf("cast"),
                               SetOf("attack"), SetOf("damage"), SetOf("death"));

            var variants = new List<DirectionalAnimator.DirectionalSpriteSet>(count);
            for (int i = 0; i < count; i++) variants.Add(SetOf($"walk_{i}"));
            anim.SetVariants(DirectionalAnimator.AnimState.Walk, variants);
            return anim;
        }

        // ---- Rotation -------------------------------------------------------

        [Test]
        public void FirstEntry_PlaysVariantZero()
        {
            DirectionalAnimator anim = WithWalkVariants(3);

            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);

            // Index 0 is where an author puts the character's default cycle, and it is the one
            // the `states` map already names — so the first step must not be an alternate.
            Assert.AreEqual(0, anim.ActiveVariant);
        }

        [Test]
        public void EachEntry_TakesTheNextVariant_AndWrapsAround()
        {
            DirectionalAnimator anim = WithWalkVariants(3);
            var seen = new List<int>();

            for (int i = 0; i < 7; i++)
            {
                anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
                seen.Add(anim.ActiveVariant);
                // Leave and come back: an entry is what selects, so the state has to change.
                anim.SetState(DirectionalAnimator.AnimState.Idle, DirectionalAnimator.Direction.East);
            }

            Assert.AreEqual(new[] { 0, 1, 2, 0, 1, 2, 0 }, seen.ToArray());
        }

        [Test]
        public void ReAssertingTheSameState_DoesNotRoll()
        {
            DirectionalAnimator anim = WithWalkVariants(3);
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            int chosen = anim.ActiveVariant;

            // PlayerController.Movement does exactly this, every frame, for as long as the
            // player holds a direction.
            for (int i = 0; i < 20; i++)
                anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);

            Assert.AreEqual(chosen, anim.ActiveVariant);
        }

        [Test]
        public void TurningWhileWalking_DoesNotRoll()
        {
            DirectionalAnimator anim = WithWalkVariants(3);
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            int chosen = anim.ActiveVariant;

            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.NorthEast);
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.North);

            // A direction change is not an entry. Re-rolling here would swap the cycle every
            // time the cursor crossed a facing sector.
            Assert.AreEqual(chosen, anim.ActiveVariant);
        }

        // ---- States with no variants ----------------------------------------

        [Test]
        public void AStateWithNoVariants_ResolvesToTheBaseSet_NotThePreviousIndex()
        {
            DirectionalAnimator anim = WithWalkVariants(3);

            // Walk to variant 2, then step into a state that has none.
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            anim.SetState(DirectionalAnimator.AnimState.Idle, DirectionalAnimator.Direction.East);
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            anim.SetState(DirectionalAnimator.AnimState.Idle, DirectionalAnimator.Direction.East);
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            Assert.AreEqual(2, anim.ActiveVariant, "precondition: walk reached its third cycle");

            anim.SetState(DirectionalAnimator.AnimState.Chase, DirectionalAnimator.Direction.East);

            Assert.AreEqual(-1, anim.ActiveVariant,
                "a state with no variants must fall to its base set, not inherit an index");
        }

        [Test]
        public void ReinstallingVariants_RestartsTheRotationAtZero()
        {
            DirectionalAnimator anim = WithWalkVariants(3);
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            anim.SetState(DirectionalAnimator.AnimState.Idle, DirectionalAnimator.Direction.East);
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            Assert.AreEqual(1, anim.ActiveVariant, "precondition: rotation has advanced");

            // A loadout swap re-enters ApplyVisuals and rebuilds every state's variant array.
            // A cursor carried across a SHORTER rebuild would start out of range.
            var shorter = new List<DirectionalAnimator.DirectionalSpriteSet> { SetOf("only") };
            anim.SetVariants(DirectionalAnimator.AnimState.Walk, shorter);

            anim.SetState(DirectionalAnimator.AnimState.Idle, DirectionalAnimator.Direction.East);
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            Assert.AreEqual(0, anim.ActiveVariant);
        }

        [Test]
        public void TheVeryFirstSetState_SelectsEvenWhenTheStateIsAlreadyIdle()
        {
            var anim = CreateAnimator();
            anim.SetSpriteSets(SetOf("idle"), SetOf("walk"), SetOf("chase"), SetOf("cast"),
                               SetOf("attack"), SetOf("damage"), SetOf("death"));
            var variants = new List<DirectionalAnimator.DirectionalSpriteSet>
                { SetOf("idle_a"), SetOf("idle_b") };
            anim.SetVariants(DirectionalAnimator.AnimState.Idle, variants);

            // `_currentState` defaults to Idle, so a plain "same state, keep what is playing"
            // guard would answer -1 here forever and an idle rotation would never install.
            anim.SetState(DirectionalAnimator.AnimState.Idle, DirectionalAnimator.Direction.East);

            Assert.AreEqual(0, anim.ActiveVariant);
        }

        [Test]
        public void AnExplicitMinusOne_IsRespected_NotOverwrittenByTheRotation()
        {
            DirectionalAnimator anim = WithWalkVariants(3);

            // This is NPCCastState's shape, moved onto a state with variants so the assertion
            // is about the mechanism rather than about the cast state. It resolves the
            // animation a spell reserves and passes -1 when the spell reserves none — meaning
            // "use the base set" — and FSMMonsterBrain.OnFSMStateChanged then calls the
            // two-argument overload right after Enter. -1 is a CHOICE, not an absence: a
            // selector that read it as "nothing chosen yet" would overwrite it here, and the
            // caller's next explicit re-assert would put it back, restarting the frame cursor
            // twice for one entry.
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East, -1);
            Assert.AreEqual(-1, anim.ActiveVariant);

            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);

            Assert.AreEqual(-1, anim.ActiveVariant,
                "the two-argument overload must not overwrite an explicitly chosen base set");
        }

        [Test]
        public void AnExplicitVariant_SurvivesTheTwoArgumentReAssert()
        {
            DirectionalAnimator anim = WithWalkVariants(3);

            // The other half of the same contract, and the one the monster brain depends on:
            // Enter picks the reserved animation with the three-argument overload, then
            // OnFSMStateChanged re-asserts the state with the two-argument one.
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East, 2);
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);

            Assert.AreEqual(2, anim.ActiveVariant);
        }

        [Test]
        public void ReinstallingVariantsWhileStandingInTheState_KeepsPlayingUntilTheNextEntry()
        {
            DirectionalAnimator anim = WithWalkVariants(2);
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            Assert.AreEqual(0, anim.ActiveVariant);

            // A loadout swap rebuilds every state's array mid-stride, and SetVariants clears
            // the selection. Nothing re-selects until the character next ENTERS the state,
            // which is harmless here by construction: the builder makes variant 0 the state's
            // own sheet, so the base set the animator falls back to is the same art.
            var swapped = new List<DirectionalAnimator.DirectionalSpriteSet>
                { SetOf("armed_a"), SetOf("armed_b") };
            anim.SetVariants(DirectionalAnimator.AnimState.Walk, swapped);
            Assert.AreEqual(-1, anim.ActiveVariant, "precondition: the rebuild cleared the selection");

            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            Assert.AreEqual(-1, anim.ActiveVariant);

            // Leaving and coming back restarts the rebuilt rotation from 0.
            anim.SetState(DirectionalAnimator.AnimState.Idle, DirectionalAnimator.Direction.East);
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            Assert.AreEqual(0, anim.ActiveVariant);
        }

        // ---- The frames actually change -------------------------------------

        [Test]
        public void ConsecutiveEntries_RenderDifferentSprites()
        {
            DirectionalAnimator anim = WithWalkVariants(2);
            var renderer = anim.GetComponent<SpriteRenderer>();

            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            string first = renderer.sprite != null ? renderer.sprite.name : null;

            anim.SetState(DirectionalAnimator.AnimState.Idle, DirectionalAnimator.Direction.East);
            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.East);
            string second = renderer.sprite != null ? renderer.sprite.name : null;

            // The index moving is not the point; the picture changing is. Asserting on
            // ActiveVariant alone would pass with both cycles resolving to the same set.
            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
            Assert.AreNotEqual(first, second);
        }
    }
}
