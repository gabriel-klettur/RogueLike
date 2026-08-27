using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.EditMode.Game.AI
{
    /// <summary>
    /// Pins the attack moveset: which move an entity picks, and what that choice changes.
    ///
    /// <c>AttackVariant</c> used to carry three fields — key, directional, sheets — and its
    /// own tooltip called the key "used in logs and by any future range/cooldown selection
    /// rule". <c>PickVariant</c> was <c>Random.Range(0, count)</c> with the comment "Random
    /// for now". The result: <c>knight_red</c> shipped five visually distinct attacks that
    /// were mechanically the same hit, and a designer could not make the shield bash come
    /// out close or the jump kick close a gap.
    /// </summary>
    [TestFixture]
    public class AttackVariantSelectionTests
    {
        private const BindingFlags NPS = BindingFlags.NonPublic | BindingFlags.Static;

        private GameObject _owner;
        private GameObject _player;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _owner = new GameObject("variant-probe");
            _player = new GameObject("player-probe");
            EntityRegistry.RegisterPlayer(_player);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = true;
            if (_player != null) EntityRegistry.UnregisterPlayer(_player);
            if (_owner != null) Object.DestroyImmediate(_owner);
            if (_player != null) Object.DestroyImmediate(_player);
        }

        private static AttackVariant Variant(string key, int weight = 1,
                                             float minDistance = 0f, float maxDistance = 0f)
        {
            return new AttackVariant
            {
                key = key,
                weight = weight,
                minDistance = minDistance,
                maxDistance = maxDistance,
            };
        }

        // ── The distance gate ───────────────────────────────────────────────────

        [Test]
        public void AllowedAt_WithNoBounds_IsAlwaysLegal()
        {
            var v = Variant("slash");

            Assert.IsTrue(v.AllowedAt(0f));
            Assert.IsTrue(v.AllowedAt(999f),
                "An unset bound is not a bound — that is what keeps every shipped variant " +
                "available everywhere, exactly as before this data existed.");
        }

        [Test]
        public void AllowedAt_RespectsAnUpperBound()
        {
            var punch = Variant("punch", maxDistance: 1.4f);

            Assert.IsTrue(punch.AllowedAt(1.0f));
            Assert.IsFalse(punch.AllowedAt(2.0f), "a punch is a point-blank answer");
        }

        [Test]
        public void AllowedAt_RespectsALowerBound()
        {
            var jumpkick = Variant("jumpkick", minDistance: 1.8f);

            Assert.IsFalse(jumpkick.AllowedAt(1.0f), "nothing to leap across");
            Assert.IsTrue(jumpkick.AllowedAt(3.0f), "a gap closer needs a gap");
        }

        // ── Selection ───────────────────────────────────────────────────────────

        private int Pick(StateMachine fsm, FSMComponents c)
        {
            var m = typeof(AttackState).GetMethod("PickVariant", NPS);
            Assert.IsNotNull(m, "PickVariant must exist");
            return (int)m.Invoke(null, new object[] { fsm, c });
        }

        /// <summary>
        /// The animator reports how many variants exist; the context carries what they mean.
        /// A DirectionalAnimator with real sprite sets is far more machinery than this needs,
        /// so the count is written straight into the backing field.
        /// </summary>
        private FSMComponents ComponentsWithVariantCount(int count)
        {
            var animator = _owner.AddComponent<DirectionalAnimator>();

            // Through the public API rather than by reflecting the backing field. That field
            // used to be `_attackVariants`; it is `_variantsByState` now that variants are
            // keyed by AnimState, and reaching past the API meant this fixture broke on the
            // rename while the behaviour it guards never changed.
            animator.SetAttackVariants(new DirectionalAnimator.DirectionalSpriteSet[count]);
            Assert.AreEqual(count, animator.AttackVariantCount,
                "fixture must actually report the variant count it claims");
            return new FSMComponents(_owner);
        }

        [Test]
        public void NoVariants_ReturnsMinusOne_SoTheBaseAttackSetIsUsed()
        {
            var c = ComponentsWithVariantCount(0);
            var fsm = new StateMachine(_owner, new IdleState());

            Assert.AreEqual(-1, Pick(fsm, c));
        }

        [Test]
        public void ZeroWeightVariant_IsNeverChosen()
        {
            var c = ComponentsWithVariantCount(2);
            var fsm = new StateMachine(_owner, new IdleState());
            fsm.SetContext(AttackState.AttackVariantContextKey, new[]
            {
                Variant("never", weight: 0),
                Variant("always", weight: 5),
            });

            for (int i = 0; i < 40; i++)
                Assert.AreEqual(1, Pick(fsm, c), "a weight of 0 means never");
        }

        [Test]
        public void OutOfRangeVariant_IsGatedOut()
        {
            _owner.transform.position = Vector3.zero;
            _player.transform.position = new Vector3(3f, 0f, 0f);   // 3 units apart

            var c = ComponentsWithVariantCount(2);
            var fsm = new StateMachine(_owner, new IdleState());
            fsm.SetContext(AttackState.AttackVariantContextKey, new[]
            {
                Variant("punch", weight: 5, maxDistance: 1.4f),  // illegal at 3 units
                Variant("slash", weight: 1),
            });

            for (int i = 0; i < 40; i++)
                Assert.AreEqual(1, Pick(fsm, c), "the punch must be unreachable at this range");
        }

        [Test]
        public void EveryVariantGatedOut_FallsBackToAUniformPick_RatherThanRefusingToAttack()
        {
            _owner.transform.position = Vector3.zero;
            _player.transform.position = new Vector3(9f, 0f, 0f);

            var c = ComponentsWithVariantCount(2);
            var fsm = new StateMachine(_owner, new IdleState());
            fsm.SetContext(AttackState.AttackVariantContextKey, new[]
            {
                Variant("punch", maxDistance: 1f),
                Variant("bash",  maxDistance: 1f),
            });

            for (int i = 0; i < 20; i++)
            {
                int picked = Pick(fsm, c);
                Assert.GreaterOrEqual(picked, 0,
                    "A monster standing there doing nothing reads as broken; an imperfect " +
                    "move reads as a monster.");
                Assert.Less(picked, 2);
            }
        }

        [Test]
        public void NoAuthoredData_StillPicksSomeAnimation()
        {
            var c = ComponentsWithVariantCount(3);
            var fsm = new StateMachine(_owner, new IdleState());
            // Context deliberately empty: animations exist, authored moveset does not.

            for (int i = 0; i < 20; i++)
            {
                int picked = Pick(fsm, c);
                Assert.GreaterOrEqual(picked, 0);
                Assert.Less(picked, 3);
            }
        }

        // ── What the choice changes ─────────────────────────────────────────────

        [Test]
        public void VariantMultipliers_DefaultToNeutral()
        {
            var v = new AttackVariant();

            Assert.AreEqual(1f, v.damageMultiplier, 0.0001f);
            Assert.AreEqual(1f, v.rangeMultiplier, 0.0001f);
            Assert.AreEqual(1f, v.cooldownMultiplier, 0.0001f);
            Assert.AreEqual(1, v.weight,
                "Defaults must leave every existing asset behaving exactly as it did.");
        }

        [Test]
        public void ShippedKnightMoveset_IsActuallyDifferentiated()
        {
            // The point of the whole feature: knight_red's five moves must not all be the
            // same hit any more. Reads the shipped asset, so this fails if someone flattens
            // it back to uniform.
            // Loaded from disk, the way every other shipped-data test does it
            // (MonsterCatalogXpRewardTests). Resources.FindObjectsOfTypeAll only sees what
            // the session happens to have loaded, so it turned this guard into a skip.
            var knight = UnityEditor.AssetDatabase.LoadAssetAtPath<MonsterDefinition>(
                "Assets/_Project/Data/Catalogs/Monsters/knight_red.asset");
            Assert.IsNotNull(knight, "knight_red.asset must exist — it is the reference moveset.");

            var variants = knight.assetConfig?.attackVariants;
            Assert.IsNotNull(variants, "knight_red must still carry its attack variants.");
            Assert.GreaterOrEqual(variants.Count, 2,
                "knight_red ships five attack variants; fewer than two means the moveset was lost.");

            bool anyDifferent = false;
            foreach (var v in variants)
                if (Mathf.Abs(v.damageMultiplier - 1f) > 0.001f ||
                    Mathf.Abs(v.rangeMultiplier - 1f) > 0.001f ||
                    v.minDistance > 0f || v.maxDistance > 0f)
                    anyDifferent = true;

            Assert.IsTrue(anyDifferent,
                "knight_red ships five visually distinct attacks; at least one must differ " +
                "mechanically or the moveset is decoration again.");
        }
    }
}
