using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay;

namespace Valkur.Tests.PlayMode.Gameplay
{
    /// <summary>
    /// The test the suite was missing: a monster hurting a player and a player hurting a
    /// monster, through the real components, with the real layer masks.
    ///
    /// The audit found this hole and named its consequence precisely — swapping the two
    /// target layer masks that <c>EntitySetup</c> assigns (<c>1 &lt;&lt; PlayerLayer</c>
    /// for a monster's MeleeCombat, <c>1 &lt;&lt; NPCLayer</c> for the player's) would
    /// break the game in the most basic way possible and leave the entire suite green.
    /// <c>CombatTests</c> only reads back configuration; <c>AttackStateSwingTests</c>
    /// asserts that <c>_lastAttackTime</c> moved, never that a target's Health fell.
    ///
    /// PlayMode rather than EditMode because this needs real physics: the damage query is
    /// a <c>Physics2D.OverlapCircleAll</c> and EditMode never steps the 2D world.
    /// </summary>
    public class PvMDamageExchangePlayTests
    {
        private const int LayerPlayer = 8;
        private const int LayerNPC    = 9;

        private GameObject _attacker;
        private GameObject _victim;

        [TearDown]
        public void TearDown()
        {
            if (_attacker != null) Object.Destroy(_attacker);
            if (_victim != null) Object.Destroy(_victim);
        }

        /// <summary>
        /// A body with the pieces the damage path actually touches: a Health to lose, a
        /// collider for the overlap query to find, and a layer for the mask to match.
        /// </summary>
        private static GameObject MakeBody(string name, int layer, Vector3 pos, int hp)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.position = pos;

            var body = go.AddComponent<CircleCollider2D>();
            body.radius = 0.4f;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;

            go.AddComponent<Health>().Initialize(hp);
            return go;
        }

        private static MeleeCombat MakeAttacker(GameObject go, int damage, float range, int targetLayer)
        {
            var combat = go.AddComponent<MeleeCombat>();
            combat.Initialize(damage, cd: 0.01f, rng: range);
            combat.SetTargetLayers(1 << targetLayer);
            return combat;
        }

        // ── Monster hits player ─────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator MonsterMelee_DamagesThePlayer()
        {
            _attacker = MakeBody("monster", LayerNPC, Vector3.zero, 100);
            _victim   = MakeBody("player",  LayerPlayer, new Vector3(1f, 0f, 0f), 100);

            var combat = MakeAttacker(_attacker, damage: 7, range: 2f, targetLayer: LayerPlayer);
            var victimHealth = _victim.GetComponent<Health>();

            yield return new WaitForFixedUpdate();
            combat.TryAttack(Vector2.right);
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(93, victimHealth.CurrentHp,
                "A monster's swing must reduce the player's Health through the real path.");
        }

        // ── Player hits monster ─────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator PlayerMelee_DamagesTheMonster()
        {
            _attacker = MakeBody("player",  LayerPlayer, Vector3.zero, 100);
            _victim   = MakeBody("monster", LayerNPC, new Vector3(1f, 0f, 0f), 100);

            var combat = MakeAttacker(_attacker, damage: 11, range: 2f, targetLayer: LayerNPC);
            var victimHealth = _victim.GetComponent<Health>();

            yield return new WaitForFixedUpdate();
            combat.TryAttack(Vector2.right);
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(89, victimHealth.CurrentHp,
                "The exchange must work in both directions, not just monster-to-player.");
        }

        // ── The masks are not interchangeable ───────────────────────────────────

        [UnityTest]
        public IEnumerator TargetMask_IsRespected_AnAttackerCannotHitItsOwnLayer()
        {
            _attacker = MakeBody("monster_a", LayerNPC, Vector3.zero, 100);
            _victim   = MakeBody("monster_b", LayerNPC, new Vector3(1f, 0f, 0f), 100);

            // A monster targets the Player layer. Its neighbour on the NPC layer must be
            // untouched — this is the assertion that makes swapping the two masks fail.
            var combat = MakeAttacker(_attacker, damage: 9, range: 2f, targetLayer: LayerPlayer);
            var victimHealth = _victim.GetComponent<Health>();

            yield return new WaitForFixedUpdate();
            combat.TryAttack(Vector2.right);
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(100, victimHealth.CurrentHp,
                "Friendly fire between NPCs would be the visible symptom of a swapped mask.");
        }

        // ── Geometry ────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator DamageReach_DoesNotExceedTheDrawnArc()
        {
            // Range 2, victim at 2.6: inside the OLD query (which was centred half a range
            // forward and so reached range * 1.5 = 3.0) and outside the corrected one.
            _attacker = MakeBody("monster", LayerNPC, Vector3.zero, 100);
            _victim   = MakeBody("player",  LayerPlayer, new Vector3(2.6f, 0f, 0f), 100);

            var combat = MakeAttacker(_attacker, damage: 5, range: 2f, targetLayer: LayerPlayer);
            var victimHealth = _victim.GetComponent<Health>();

            yield return new WaitForFixedUpdate();
            combat.TryAttack(Vector2.right);
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(100, victimHealth.CurrentHp,
                "You must not be hit a tile and a half outside the crescent that was drawn.");
        }

        [UnityTest]
        public IEnumerator BehindTheAttacker_IsOutsideTheArc()
        {
            _attacker = MakeBody("monster", LayerNPC, Vector3.zero, 100);
            _victim   = MakeBody("player",  LayerPlayer, new Vector3(-1f, 0f, 0f), 100);

            var combat = MakeAttacker(_attacker, damage: 5, range: 2f, targetLayer: LayerPlayer);
            var victimHealth = _victim.GetComponent<Health>();

            yield return new WaitForFixedUpdate();
            combat.TryAttack(Vector2.right);   // swinging east, victim is west
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(100, victimHealth.CurrentHp,
                "The arc is what tells the player which side of the monster is dangerous.");
        }

        [UnityTest]
        public IEnumerator OneEntity_TakesOneHitPerSwing_EvenWithSeveralColliders()
        {
            _attacker = MakeBody("monster", LayerNPC, Vector3.zero, 100);
            _victim   = MakeBody("player",  LayerPlayer, new Vector3(1f, 0f, 0f), 100);

            // A second collider on a child, the shape MeleeCombat resolves through
            // GetComponentInParent. Without the per-swing de-dupe this entity would take
            // the hit twice.
            var hurtbox = new GameObject("hurtbox") { layer = LayerPlayer };
            hurtbox.transform.SetParent(_victim.transform, false);
            hurtbox.AddComponent<CircleCollider2D>().radius = 0.5f;

            var combat = MakeAttacker(_attacker, damage: 6, range: 2f, targetLayer: LayerPlayer);
            var victimHealth = _victim.GetComponent<Health>();

            yield return new WaitForFixedUpdate();
            combat.TryAttack(Vector2.right);
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(94, victimHealth.CurrentHp,
                "Resolving victims through the parent must not let one entity be hit twice.");
        }
    }
}
