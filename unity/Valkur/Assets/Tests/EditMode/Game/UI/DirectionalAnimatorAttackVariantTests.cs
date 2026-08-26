using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// Guards the attack-variant path: an entity may declare several attack animations
    /// under the single <see cref="DirectionalAnimator.AnimState.Attack"/> state, selected
    /// by index.
    ///
    /// The state vocabulary is a fixed seven-value enum enumerated positionally in four
    /// places, so extra animations are DATA (a list on <see cref="EntityAssetConfig"/>)
    /// rather than new enum members. These tests pin the two things that go wrong silently
    /// when that indirection is added: a variant index that does not actually reach the
    /// renderer, and a variant change that the SetState early-return swallows so the second
    /// swing keeps playing the first one's frames.
    ///
    /// Awake does not run in EditMode, so the renderer the animator draws through is wired
    /// by reflection and the Update tick is stood in for by calling AdvanceFrame directly —
    /// exercising the real render path rather than a parallel one.
    /// </summary>
    public class DirectionalAnimatorAttackVariantTests
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

        /// <summary>An animator whose renderer is wired, since Awake never runs here.</summary>
        private DirectionalAnimator CreateAnimator(out SpriteRenderer renderer)
        {
            var go = new GameObject("TestAnimator");
            _created.Add(go);
            renderer = go.AddComponent<SpriteRenderer>();
            var anim = go.AddComponent<DirectionalAnimator>();
            typeof(DirectionalAnimator)
                .GetField("targetRenderer", Instance)
                .SetValue(anim, renderer);
            return anim;
        }

        /// <summary>Stands in for the Update tick, which EditMode never delivers.</summary>
        private static void Tick(DirectionalAnimator anim)
            => typeof(DirectionalAnimator).GetMethod("AdvanceFrame", Instance).Invoke(anim, null);

        private static int FrameIndex(DirectionalAnimator anim)
            => (int)typeof(DirectionalAnimator).GetField("_frameIndex", Instance).GetValue(anim);

        /// <summary>
        /// <paramref name="count"/> named sprites, so a failure says which family and frame
        /// was rendered instead of comparing opaque references.
        /// </summary>
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

        /// <summary>
        /// Frames per direction in the test sets. Must be &gt; 1: AdvanceFrame short-circuits
        /// a single-frame bucket (<c>if (frames.Length == 1) { ApplyFrame(frames[0]); return; }</c>)
        /// and never touches the cursor, so a one-frame set cannot exercise a restart.
        /// </summary>
        private const int FramesPerDirection = 4;

        /// <summary>An 8-direction set whose sprites are named after <paramref name="prefix"/>.</summary>
        private DirectionalAnimator.DirectionalSpriteSet SetOf(string prefix)
            => DirectionalAnimator.CreateSetFromLinearFrames(
                CreateFrames(prefix, 8 * FramesPerDirection));

        private DirectionalAnimator WithVariants(out SpriteRenderer renderer, params string[] prefixes)
        {
            var anim = CreateAnimator(out renderer);
            anim.SetSpriteSets(SetOf("idle"), SetOf("walk"), SetOf("chase"), SetOf("cast"),
                               SetOf("attack"), SetOf("damage"), SetOf("death"));

            var variants = new List<DirectionalAnimator.DirectionalSpriteSet>();
            foreach (string p in prefixes) variants.Add(SetOf(p));
            anim.SetAttackVariants(variants);
            return anim;
        }

        // ---- Routing --------------------------------------------------------

        [Test]
        public void SelectedVariant_RendersItsOwnFrames()
        {
            var anim = WithVariants(out SpriteRenderer renderer, "punch", "kick", "jumpkick");
            Assert.AreEqual(3, anim.AttackVariantCount);

            for (int v = 0; v < 3; v++)
            {
                anim.SetState(DirectionalAnimator.AnimState.Attack,
                              DirectionalAnimator.Direction.West, v);
                Tick(anim);
                StringAssert.StartsWith(new[] { "punch", "kick", "jumpkick" }[v], renderer.sprite.name,
                    $"variant {v} rendered the wrong family");
            }
        }

        [Test]
        public void NoVariantSelected_FallsBackToTheSingleAttackSet()
        {
            var anim = WithVariants(out SpriteRenderer renderer, "punch", "kick");

            anim.SetState(DirectionalAnimator.AnimState.Attack,
                          DirectionalAnimator.Direction.West, -1);
            Tick(anim);

            StringAssert.StartsWith("attack", renderer.sprite.name,
                "-1 must resolve to the base attack set, which is what every entity " +
                "without variants relies on.");
        }

        [Test]
        public void OutOfRangeVariant_FallsBackInsteadOfThrowing()
        {
            var anim = WithVariants(out SpriteRenderer renderer, "punch");

            // The variant array is rebuilt on every ApplyVisuals; an index cached across a
            // shorter rebuild must not throw out of the render path.
            Assert.DoesNotThrow(() =>
            {
                anim.SetState(DirectionalAnimator.AnimState.Attack,
                              DirectionalAnimator.Direction.West, 7);
                Tick(anim);
            });
            StringAssert.StartsWith("attack", renderer.sprite.name);
        }

        [Test]
        public void AnEntityWithNoVariants_IsUntouched()
        {
            var anim = CreateAnimator(out SpriteRenderer renderer);
            anim.SetSpriteSets(SetOf("idle"), SetOf("walk"), SetOf("chase"), SetOf("cast"),
                               SetOf("attack"), SetOf("damage"), SetOf("death"));

            Assert.AreEqual(0, anim.AttackVariantCount);
            anim.SetState(DirectionalAnimator.AnimState.Attack, DirectionalAnimator.Direction.West);
            Tick(anim);
            StringAssert.StartsWith("attack", renderer.sprite.name);
        }

        // ---- The early-return trap ------------------------------------------

        [Test]
        public void ChangingOnlyTheVariant_RestartsTheAnimation()
        {
            var anim = WithVariants(out SpriteRenderer renderer, "punch", "kick");

            anim.SetState(DirectionalAnimator.AnimState.Attack,
                          DirectionalAnimator.Direction.West, 0);
            Tick(anim);
            Tick(anim);
            Assert.Greater(FrameIndex(anim), 0, "precondition: the cursor has advanced");

            // Same state, same direction, different variant. SetState early-returns when
            // neither state nor direction changed, so without treating the variant as a
            // change the second swing would keep playing the first one's frames.
            anim.SetState(DirectionalAnimator.AnimState.Attack,
                          DirectionalAnimator.Direction.West, 1);

            Assert.AreEqual(1, anim.ActiveAttackVariant);
            StringAssert.StartsWith("kick", renderer.sprite.name,
                "the new variant must render immediately, not after the next frame interval");

            // The FIRST frame of the new variant, not merely a frame of it: without the
            // cursor reset the second kick starts three frames into its own arc, so the
            // wind-up is missing and the hit lands against a mid-arc pose.
            Assert.AreSame(anim.AttackVariantSet(1).west[0], renderer.sprite,
                "the new variant must start from its first frame");
        }

        [Test]
        public void RestartCurrentState_ReplaysTheSameVariantFromTheStart()
        {
            var anim = WithVariants(out SpriteRenderer renderer, "punch");

            anim.SetState(DirectionalAnimator.AnimState.Attack,
                          DirectionalAnimator.Direction.West, 0);
            Tick(anim);
            Tick(anim);
            int advanced = FrameIndex(anim);
            Assert.Greater(advanced, 0, "precondition: the cursor has advanced");

            // A knight the player never backs away from re-swings without leaving the
            // state, so the same variant has to be replayable from frame 0.
            anim.RestartCurrentState();

            Assert.Less(FrameIndex(anim), advanced);
            Assert.AreEqual(0, anim.ActiveAttackVariant);
        }

        [Test]
        public void ADirectionOnlyChangeDuringAnAttack_KeepsTheVariant()
        {
            var anim = WithVariants(out SpriteRenderer renderer, "punch", "kick");

            anim.SetState(DirectionalAnimator.AnimState.Attack,
                          DirectionalAnimator.Direction.West, 1);
            Tick(anim);
            StringAssert.StartsWith("kick", renderer.sprite.name, "precondition");

            // Turning mid-swing is what a strafing player causes, constantly. The
            // direction-only branch refreshes the sprite WITHOUT advancing the cursor, and
            // resolving the base attack set there flashes one frame of the default swing
            // into the middle of the kick — hidden again by the very next tick.
            anim.SetState(DirectionalAnimator.AnimState.Attack,
                          DirectionalAnimator.Direction.NorthWest, 1);

            StringAssert.StartsWith("kick", renderer.sprite.name,
                "a direction-only change dropped back to the base attack set");
        }

        // ---- Lifecycle --------------------------------------------------------

        [Test]
        public void SetAttackVariants_NullOrEmpty_ClearsWhatWasThere()
        {
            var anim = WithVariants(out SpriteRenderer renderer, "punch", "kick");
            anim.SetState(DirectionalAnimator.AnimState.Attack,
                          DirectionalAnimator.Direction.West, 1);
            Assert.AreEqual(2, anim.AttackVariantCount, "precondition");

            // Rebinding a monster to a different definition must not leave the old set live:
            // AttackVariantCount is what AttackState rolls its index against, so a stale
            // non-zero means indices into an array that no longer describes this entity.
            anim.SetAttackVariants(null);
            Assert.AreEqual(0, anim.AttackVariantCount);
            Assert.AreEqual(-1, anim.ActiveAttackVariant);

            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.West);
            anim.SetState(DirectionalAnimator.AnimState.Attack,
                          DirectionalAnimator.Direction.West, -1);
            Tick(anim);
            StringAssert.StartsWith("attack", renderer.sprite.name);

            anim.SetAttackVariants(new List<DirectionalAnimator.DirectionalSpriteSet>());
            Assert.AreEqual(0, anim.AttackVariantCount);
        }

        [Test]
        public void ALatchedVariant_NeverLeaksIntoANonAttackState()
        {
            var anim = WithVariants(out SpriteRenderer renderer, "punch", "kick");

            anim.SetState(DirectionalAnimator.AnimState.Attack,
                          DirectionalAnimator.Direction.West, 1);
            Tick(anim);
            Assert.AreEqual(1, anim.ActiveAttackVariant, "precondition: a variant is latched");

            // The index stays latched after the swing — the 2-arg overload forwards it so a
            // relay from FSMMonsterBrain cannot clobber it. That is only safe because the
            // variant lookup is gated on AnimState.Attack; drop that guard and a knight
            // walks away using its kick frames as locomotion.
            foreach (var state in new[]
            {
                DirectionalAnimator.AnimState.Walk,
                DirectionalAnimator.AnimState.Idle,
                DirectionalAnimator.AnimState.Chase,
                DirectionalAnimator.AnimState.Cast,
                DirectionalAnimator.AnimState.Damage,
            })
            {
                anim.SetState(state, DirectionalAnimator.Direction.West);
                Tick(anim);
                StringAssert.StartsWith(state.ToString().ToLowerInvariant(), renderer.sprite.name,
                    $"{state} rendered a variant's frames");
            }
        }

        [Test]
        public void GetStateLength_IgnoresTheVariantForNonAttackStates()
        {
            // The variant sets are four frames per direction, the base states one.
            var anim = CreateAnimator(out _);
            anim.SetSpriteSets(SetOf("idle"), SetOf("walk"), SetOf("chase"), SetOf("cast"),
                               SetOf("attack"), SetOf("damage"), SetOf("death"));
            anim.SetAttackVariants(new List<DirectionalAnimator.DirectionalSpriteSet>
            {
                DirectionalAnimator.CreateSetFromLinearFrames(CreateFrames("long", 8 * 9)),
            });

            anim.SetState(DirectionalAnimator.AnimState.Walk, DirectionalAnimator.Direction.West);
            float interval = (float)typeof(DirectionalAnimator)
                .GetField("frameInterval", Instance).GetValue(anim);

            // A stale index must not size a Death despawn or a Cast channel by the length of
            // whatever attack happened to play last.
            Assert.AreEqual(FramesPerDirection * interval,
                anim.GetStateLength(DirectionalAnimator.AnimState.Walk, 0), 0.0001f);
            Assert.AreEqual(9 * interval,
                anim.GetStateLength(DirectionalAnimator.AnimState.Attack, 0), 0.0001f,
                "precondition: the variant really is longer");
        }

        // ---- Length ----------------------------------------------------------

        [Test]
        public void GetStateLength_IsFrameCountTimesInterval()
        {
            var anim = WithVariants(out _, "punch");
            float interval = (float)typeof(DirectionalAnimator)
                .GetField("frameInterval", Instance).GetValue(anim);

            anim.SetState(DirectionalAnimator.AnimState.Attack, DirectionalAnimator.Direction.West, 0);

            Assert.AreEqual(FramesPerDirection * interval,
                anim.GetStateLength(DirectionalAnimator.AnimState.Attack, 0), 0.0001f);
        }

        [Test]
        public void GetStateLength_IsZeroWhenNothingIsWired()
        {
            var anim = CreateAnimator(out _);

            // Zero, not one frame: AttackState takes the larger of this and its historical
            // windup + 0.3 s, so an unwired state must never lengthen a swing.
            Assert.AreEqual(0f, anim.GetStateLength(DirectionalAnimator.AnimState.Attack), 0.0001f);
        }

        // ---- Binder ----------------------------------------------------------

        [Test]
        public void Binder_DropsEmptyVariantsInsteadOfKeepingBlankSlots()
        {
            var go = new GameObject("BinderTarget");
            _created.Add(go);
            go.AddComponent<SpriteRenderer>();

            var config = new EntityAssetConfig
            {
                idleSheets = CreateFrames("idle", 8),
                attackSheets = CreateFrames("attack", 8),
                attackVariants = new List<AttackVariant>
                {
                    new AttackVariant { key = "punch", sheets = CreateFrames("punch", 8) },
                    new AttackVariant { key = "empty", sheets = new List<Sprite>() },
                    null,
                    new AttackVariant { key = "kick", sheets = CreateFrames("kick", 8) },
                    new AttackVariant { key = "allNull", sheets = new List<Sprite> { null, null } },
                },
            };

            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            _created.Add(def);
            def.assetConfig = config;

            Assert.IsTrue(EntityAnimationBinder.ApplyMonsterVisuals(go, def));

            // The selector picks an index at random; a surviving empty slot would render
            // the idle pose mid-swing roughly one attack in four.
            Assert.AreEqual(2, go.GetComponent<DirectionalAnimator>().AttackVariantCount);
        }
    }
}
