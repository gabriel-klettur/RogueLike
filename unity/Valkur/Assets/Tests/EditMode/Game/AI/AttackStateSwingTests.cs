using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.EditMode.Game.AI
{
    /// <summary>
    /// Guards how long one swing of <see cref="AttackState"/> lasts, and which attack
    /// animation it plays.
    ///
    /// The swing length used to be the constant <c>windup + 0.3 s</c>. Against a global
    /// 0.15 s frame interval that is five frames, so an entity with a genuinely animated
    /// eight-frame attack was cut mid-arc and snapped back to Chase — visible only as "the
    /// swing looks wrong", which is why it survived a full release. The fix takes the
    /// LARGER of that historical floor and the animation's own length, and the floor is
    /// what these tests care about most: shortening it would silently re-pace the eighteen
    /// monsters whose attack is a single held pose.
    ///
    /// EditMode never delivers Awake or Update, so the animator's renderer is wired by
    /// reflection and the sprite sets are installed directly.
    /// </summary>
    public class AttackStateSwingTests
    {
        private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>Mirrors DirectionalAnimator's serialized default; asserted below.</summary>
        private const float FrameInterval = 0.15f;

        /// <summary>AttackState's historical swing length beyond the windup.</summary>
        private const float SwingTail = 0.3f;

        private readonly List<Object> _created = new List<Object>();
        private GameObject _player;

        [SetUp]
        public void SetUp()
        {
            _player = new GameObject("Player");
            _created.Add(_player);
            // AttackState abandons the swing with no player registered, so every test needs
            // one — placed to the west so the resolved facing direction is deterministic.
            _player.transform.position = new Vector3(-2f, 0f, 0f);
            EntityRegistry.RegisterPlayer(_player);
        }

        [TearDown]
        public void TearDown()
        {
            EntityRegistry.UnregisterPlayer(_player);
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        // ---- Helpers --------------------------------------------------------

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

        /// <summary>
        /// An attacker at the origin. <paramref name="baseFramesPerDirection"/> sizes the
        /// single attack set; <paramref name="variantFramesPerDirection"/> adds one variant
        /// per entry, so a test can give the variant a different length from the base and
        /// prove the selected one is what sizes the swing.
        /// </summary>
        /// <summary>
        /// A set whose WEST bucket is <paramref name="westFrames"/> long while every other
        /// direction holds one frame. Exists to catch a swing measured before the entity
        /// turns: on a uniform sheet that mistake is invisible.
        /// </summary>
        private DirectionalAnimator.DirectionalSpriteSet UnevenSet(int westFrames)
        {
            Sprite[] wide = CreateFrames(westFrames).ToArray();
            Sprite[] one = CreateFrames(1).ToArray();
            return new DirectionalAnimator.DirectionalSpriteSet
            {
                south = one, southEast = one, east = one, northEast = one,
                north = one, northWest = one, west = wide, southWest = one,
            };
        }

        private StateMachine CreateAttacker(int baseFramesPerDirection,
                                            params int[] variantFramesPerDirection)
            => CreateAttacker(SetOf(baseFramesPerDirection), null, variantFramesPerDirection);

        private StateMachine CreateAttacker(DirectionalAnimator.DirectionalSpriteSet attackSet,
                                            System.Action<MeleeCombat> configureMelee,
                                            params int[] variantFramesPerDirection)
        {
            var go = new GameObject("Attacker");
            _created.Add(go);
            var renderer = go.AddComponent<SpriteRenderer>();
            var anim = go.AddComponent<DirectionalAnimator>();
            typeof(DirectionalAnimator).GetField("targetRenderer", Instance).SetValue(anim, renderer);

            if (configureMelee != null)
            {
                // Added BEFORE FSMComponents, which caches the reference at construction.
                var melee = go.AddComponent<MeleeCombat>();
                configureMelee(melee);
            }

            var oneFrame = SetOf(1);
            anim.SetSpriteSets(oneFrame, oneFrame, oneFrame, oneFrame,
                               attackSet, oneFrame, oneFrame);

            if (variantFramesPerDirection != null && variantFramesPerDirection.Length > 0)
            {
                var variants = new List<DirectionalAnimator.DirectionalSpriteSet>();
                foreach (int n in variantFramesPerDirection) variants.Add(SetOf(n));
                anim.SetAttackVariants(variants);
            }

            var fsm = new StateMachine(go, new AttackState());
            fsm.SetContext(FSMComponents.KEY, new FSMComponents(go));
            return fsm;
        }

        private static float SwingDuration(StateMachine fsm)
            => (float)typeof(AttackState).GetField("_attackDuration", Instance)
                .GetValue(fsm.CurrentState);

        private static int SelectedVariant(StateMachine fsm)
            => (int)typeof(AttackState).GetField("_variant", Instance).GetValue(fsm.CurrentState);

        private static bool DamageGateSpent(StateMachine fsm)
            => (bool)typeof(AttackState).GetField("_attacked", Instance).GetValue(fsm.CurrentState);

        private static float Timer(StateMachine fsm)
            => (float)typeof(AttackState).GetField("_timer", Instance).GetValue(fsm.CurrentState);

        private static float LastAttackTime(MeleeCombat melee)
            => (float)typeof(MeleeCombat).GetField("_lastAttackTime", Instance).GetValue(melee);

        private static void Enter(StateMachine fsm, float windupSeconds)
        {
            fsm.SetContext("attack_windup_s", windupSeconds);
            // Begin(), not CurrentState.Enter(): entering by hand leaves the machine's
            // pending flag set, and the first Update would then enter the swing a
            // second time and restart its measurement mid-test.
            fsm.Begin();
        }

        // ---- The floor -------------------------------------------------------

        [Test]
        public void FrameIntervalIsStillTheValueTheseTestsAssume()
        {
            var go = new GameObject("IntervalProbe");
            _created.Add(go);
            var anim = go.AddComponent<DirectionalAnimator>();

            // Every duration expectation below is derived from this. If the serialized
            // default moves, these tests must be re-derived rather than quietly re-baselined.
            Assert.AreEqual(FrameInterval,
                (float)typeof(DirectionalAnimator).GetField("frameInterval", Instance).GetValue(anim),
                0.0001f);
        }

        [Test]
        public void SinglePoseAttack_KeepsTheHistoricalSwingLength()
        {
            // What eighteen of the nineteen monsters look like: one held attack pose.
            var fsm = CreateAttacker(baseFramesPerDirection: 1);

            Enter(fsm, 0.5f);

            // 1 frame = 0.15 s, far under the 0.8 s floor, so the floor must win.
            Assert.AreEqual(0.5f + SwingTail, SwingDuration(fsm), 0.0001f,
                "An entity with no real attack animation must be paced exactly as before.");
        }

        [Test]
        public void AttackWithNoAnimatorAtAll_StillUsesTheFloor()
        {
            var go = new GameObject("AnimatorlessAttacker");
            _created.Add(go);
            var fsm = new StateMachine(go, new AttackState());
            fsm.SetContext(FSMComponents.KEY, new FSMComponents(go));

            Assert.DoesNotThrow(() => Enter(fsm, 0.2f));
            Assert.AreEqual(0.2f + SwingTail, SwingDuration(fsm), 0.0001f);
        }

        [Test]
        public void AnimatedAttack_LastsLongEnoughToPlayEveryFrame()
        {
            // The knight: eight frames per direction.
            var fsm = CreateAttacker(baseFramesPerDirection: 8);

            Enter(fsm, 0.45f);

            // 8 x 0.15 = 1.2 s, above the 0.75 s floor. This is the regression that shipped:
            // the swing ended at 0.75 s with three frames of the arc never drawn.
            Assert.AreEqual(8 * FrameInterval, SwingDuration(fsm), 0.0001f);
            Assert.Greater(SwingDuration(fsm), 0.45f + SwingTail);
        }

        [Test]
        public void TheSwingIsNeverShorterThanTheFloor()
        {
            // A long windup with a short animation: the floor must still win, or a slow
            // telegraphed attack would land before its own wind-up finished reading.
            var fsm = CreateAttacker(baseFramesPerDirection: 2);

            Enter(fsm, 1.5f);

            Assert.AreEqual(1.5f + SwingTail, SwingDuration(fsm), 0.0001f);
        }

        // ---- Variant selection ------------------------------------------------

        [Test]
        public void NoVariantsDeclared_SelectsTheBaseAttackSet()
        {
            var fsm = CreateAttacker(baseFramesPerDirection: 4);

            Enter(fsm, 0.2f);

            Assert.AreEqual(-1, SelectedVariant(fsm),
                "-1 is what routes to the single attack set; any other value would index " +
                "a variant array that does not exist.");
        }

        [Test]
        public void VariantsDeclared_SelectsOneOfThemAndSizesTheSwingToIt()
        {
            // Every variant is six frames while the base set is one, so the duration alone
            // proves the SELECTED variant sized the swing rather than the base set.
            var fsm = CreateAttacker(baseFramesPerDirection: 1, 6, 6, 6);

            Enter(fsm, 0.2f);

            Assert.That(SelectedVariant(fsm), Is.InRange(0, 2));
            Assert.AreEqual(6 * FrameInterval, SwingDuration(fsm), 0.0001f);
        }

        [Test]
        public void EverySwingReRollsItsVariant()
        {
            var fsm = CreateAttacker(baseFramesPerDirection: 1, 2, 2, 2, 2, 2);
            var beginSwing = typeof(AttackState).GetMethod("BeginSwing", Instance);
            var components = fsm.GetContext<FSMComponents>(FSMComponents.KEY);

            var seen = new HashSet<int>();
            for (int i = 0; i < 60; i++)
            {
                beginSwing.Invoke(fsm.CurrentState, new object[] { fsm, components });
                seen.Add(SelectedVariant(fsm));
            }

            // Sixty swings over five variants: seeing only one means selection is frozen,
            // which is how a knight ends up throwing the same kick forever.
            Assert.Greater(seen.Count, 1, "the variant never changed across 60 swings");
            foreach (int v in seen)
                Assert.That(v, Is.InRange(0, 4), $"variant {v} is outside the declared range");
        }

        [Test]
        public void ReSwingRestartsTheAnimationInsteadOfRidingTheOldLoop()
        {
            var fsm = CreateAttacker(baseFramesPerDirection: 6);
            Enter(fsm, 0.1f);

            var anim = fsm.Owner.GetComponent<DirectionalAnimator>();
            var renderer = fsm.Owner.GetComponent<SpriteRenderer>();
            var advance = typeof(DirectionalAnimator).GetMethod("AdvanceFrame", Instance);

            // The player sits due west, so the west bucket is the one being drawn.
            Sprite firstFrame = anim.AttackSprites.west[0];

            advance.Invoke(anim, null);
            advance.Invoke(anim, null);
            Assert.AreNotSame(firstFrame, renderer.sprite,
                "precondition: the animation has moved off its first frame");

            // The player never leaves melee range, so Execute takes the re-swing branch
            // rather than exiting the state. Before this, the second swing picked up
            // wherever the first one's sprite loop happened to be.
            fsm.SetContext("melee_range", 10f);
            fsm.CurrentState.Execute(fsm, SwingDuration(fsm) + 0.01f);

            Assert.AreEqual(typeof(AttackState), fsm.CurrentState.GetType(),
                "precondition: still attacking, not chasing");
            // Asserted on the drawn sprite, not on _frameIndex: AdvanceFrame draws a frame
            // and then leaves the cursor pointing at the NEXT one, so a correctly restarted
            // animation reads as index 1 with frame 0 on screen.
            Assert.AreSame(firstFrame, renderer.sprite,
                "a re-swing must replay its animation from the first frame");
        }

        // ---- The damage gate -------------------------------------------------

        [Test]
        public void ReSwing_ReArmsTheDamageGate()
        {
            var fsm = CreateAttacker(baseFramesPerDirection: 1);
            Enter(fsm, 0.1f);
            fsm.SetContext("melee_range", 10f);          // the player never leaves range

            Assert.IsFalse(DamageGateSpent(fsm), "precondition: the swing has not landed yet");
            fsm.CurrentState.Execute(fsm, 0.15f);        // past the windup
            Assert.IsTrue(DamageGateSpent(fsm), "precondition: the first swing landed");

            // Re-swing. Without re-arming the gate the monster animates forever and damages
            // exactly once — and every other test here stays green, because none of them
            // looks at this field.
            fsm.CurrentState.Execute(fsm, SwingDuration(fsm));

            Assert.IsFalse(DamageGateSpent(fsm), "the second swing can never land");
            Assert.AreEqual(0f, Timer(fsm), 0.0001f,
                "a re-swing that re-arms the gate but not the clock lands its hit immediately");
        }

        [Test]
        public void SecondSwing_ActuallyAttemptsDamage()
        {
            MeleeCombat melee = null;
            var fsm = CreateAttacker(SetOf(1), m =>
            {
                // Cooldown 0 because Time.time is frozen in EditMode: at the shipped 1 s the
                // melee component would refuse the second attempt on its own and this test
                // would pass while proving nothing about AttackState.
                m.Initialize(7, 0f, 5f);
                melee = m;
            });
            Enter(fsm, 0.1f);
            fsm.SetContext("melee_range", 10f);

            fsm.CurrentState.Execute(fsm, 0.15f);
            Assert.Greater(LastAttackTime(melee), -900f,
                "precondition: the first swing reached TryAttack");

            // Re-swing, then step past the windup again with the record wiped, so only a
            // genuine second attempt can move it.
            fsm.CurrentState.Execute(fsm, SwingDuration(fsm));
            typeof(MeleeCombat).GetField("_lastAttackTime", Instance).SetValue(melee, -999f);
            fsm.CurrentState.Execute(fsm, 0.15f);

            Assert.Greater(LastAttackTime(melee), -900f,
                "The second swing never called TryAttack — the monster animates forever " +
                "and deals damage exactly once.");
        }

        // ---- Facing before measuring -----------------------------------------

        [Test]
        public void TheSwingIsMeasuredAfterTurningToFaceThePlayer()
        {
            // West holds eight frames, every other direction one. The animator starts facing
            // South, so measuring before the turn reads one frame and falls back to the floor.
            var fsm = CreateAttacker(UnevenSet(westFrames: 8), null);

            Enter(fsm, 0.1f);   // floor = 0.4 s; the west bucket needs 1.2 s

            Assert.AreEqual(8 * FrameInterval, SwingDuration(fsm), 0.0001f,
                "The swing was sized against the direction the entity faced BEFORE it " +
                "turned, so it gets cut mid-arc — and only on the first swing, which is " +
                "what makes it so hard to see.");
        }

        [Test]
        public void PlayerOutOfRange_LeavesAttackForChase()
        {
            var fsm = CreateAttacker(baseFramesPerDirection: 1);
            Enter(fsm, 0.1f);

            fsm.SetContext("melee_range", 0.1f);   // the player sits 2 units west
            fsm.CurrentState.Execute(fsm, SwingDuration(fsm) + 0.01f);

            Assert.AreEqual(typeof(ChaseState), fsm.CurrentState.GetType());
        }
    }
}
