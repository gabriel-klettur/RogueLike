using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.EditMode.Game.AI
{
    /// <summary>
    /// Pins <see cref="FSMComponents.SetVelocity"/>, the single seam every FSM state
    /// writes movement through.
    ///
    /// Two systems used to fight the states for ownership of <c>velocity</c> and lose,
    /// because the states wrote it unconditionally on every tick: a knockback impulse
    /// survived at most one frame, and a stun was honoured by the player controller and
    /// by NPCAutoCast but by nothing in the FSM. Both failures were invisible to the
    /// suite — nothing asserted on NPC velocity at all — so the rule gets its own
    /// fixture rather than riding along inside a state test.
    ///
    /// EditMode note: physics does not step here, so <c>AddForce</c> never becomes
    /// velocity. The knockback cases therefore arm the real window through
    /// <see cref="CombatFeedback.ApplyKnockback"/> and then write the velocity the
    /// impulse would have produced. What is under test is whether the seam YIELDS,
    /// which is exactly the half that was broken.
    /// </summary>
    [TestFixture]
    public class FSMMovementGatingTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            // StunEffect.OnApply and ApplyKnockback both start coroutines, which
            // EditMode refuses; the refusal is noise, not a failure of what is tested.
            LogAssert.ignoreFailingMessages = true;

            _go = new GameObject("gating-probe");
            _go.AddComponent<Rigidbody2D>().gravityScale = 0f;
            _go.AddComponent<Health>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            LogAssert.ignoreFailingMessages = false;
        }

        private FSMComponents Components() => new FSMComponents(_go);
        private Rigidbody2D Rb => _go.GetComponent<Rigidbody2D>();

        /// <summary>
        /// Adds CombatFeedback and runs its Awake by hand.
        ///
        /// EditMode never calls Awake on a plain MonoBehaviour, so the component's
        /// cached <c>_rb</c> would stay null and <c>ApplyKnockback</c> would take its
        /// "no rigidbody" early-out — the test would then pass for the wrong reason,
        /// asserting nothing. In Play Mode AddComponent runs Awake synchronously, which
        /// is what EntitySetup.ConfigureMonster relies on.
        /// </summary>
        private CombatFeedback AddAwokenFeedback()
        {
            var feedback = _go.AddComponent<CombatFeedback>();
            typeof(CombatFeedback)
                .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(feedback, null);
            return feedback;
        }

        // ── The ordinary case ───────────────────────────────────────────────────

        [Test]
        public void SetVelocity_WithNoStunOrKnockback_WritesThrough()
        {
            Components().SetVelocity(new Vector2(3f, -4f));

            Assert.AreEqual(new Vector2(3f, -4f), Rb.velocity);
        }

        [Test]
        public void StopMovement_ZeroesVelocity()
        {
            Rb.velocity = new Vector2(5f, 5f);

            Components().StopMovement();

            Assert.AreEqual(Vector2.zero, Rb.velocity);
        }

        [Test]
        public void SetVelocity_WithNoRigidbody_DoesNotThrow()
        {
            var bare = new GameObject("no-rb");
            try
            {
                Assert.DoesNotThrow(() => new FSMComponents(bare).SetVelocity(Vector2.one));
            }
            finally
            {
                Object.DestroyImmediate(bare);
            }
        }

        // ── Stun ────────────────────────────────────────────────────────────────

        [Test]
        public void SetVelocity_WhileStunned_ForcesZero_NotTheRequestedVector()
        {
            _go.AddComponent<StatusEffectManager>().Apply(new StunEffect(5f));
            var c = Components();
            Assert.IsTrue(c.IsStunned, "probe must actually be stunned");

            c.SetVelocity(new Vector2(9f, 9f));

            Assert.AreEqual(Vector2.zero, Rb.velocity,
                "A stunned entity must be stopped, not merely left alone — otherwise it " +
                "coasts on whatever the previous tick wrote.");
        }

        [Test]
        public void IsStunned_WithNoStatusEffectManager_IsFalse()
        {
            Assert.IsFalse(Components().IsStunned);
        }

        // ── Knockback ───────────────────────────────────────────────────────────

        [Test]
        public void SetVelocity_DuringKnockback_LeavesTheImpulseAlone()
        {
            var feedback = AddAwokenFeedback();
            feedback.ApplyKnockback((Vector2)_go.transform.position + Vector2.right);
            Assert.IsTrue(feedback.KnockbackActive, "probe must be inside the knockback window");

            var impulse = new Vector2(-4f, 0f);
            Rb.velocity = impulse;

            Components().SetVelocity(new Vector2(7f, 0f));

            Assert.AreEqual(impulse, Rb.velocity,
                "The FSM must yield to knockback. Overwriting it here is what made every " +
                "hit in the game read as weightless.");
        }

        [Test]
        public void StopMovement_DuringKnockback_DoesNotCancelIt()
        {
            var feedback = AddAwokenFeedback();
            feedback.ApplyKnockback((Vector2)_go.transform.position + Vector2.right);

            var impulse = new Vector2(-4f, 0f);
            Rb.velocity = impulse;

            // DamageState.Enter stops the body; before this seam existed that call
            // landed one frame after the impulse and cancelled it outright.
            Components().StopMovement();

            Assert.AreEqual(impulse, Rb.velocity);
        }

        [Test]
        public void KnockbackActive_WithNoCombatFeedback_IsFalse()
        {
            Assert.IsFalse(Components().KnockbackActive);
        }

        // ── Late component resolution ───────────────────────────────────────────

        [Test]
        public void StatusIsResolvedLazily_SoLateAddedComponentsAreSeen()
        {
            // EntitySetup.ConfigureMonster adds StatusEffectManager and CombatFeedback
            // AFTER brain.Initialize(def) builds FSMComponents. Resolving them in the
            // constructor would cache null for every monster in the game.
            var c = Components();
            _go.AddComponent<StatusEffectManager>();

            Assert.IsNotNull(c.Status,
                "FSMComponents must resolve Status on first access, not at construction.");
        }
    }
}
