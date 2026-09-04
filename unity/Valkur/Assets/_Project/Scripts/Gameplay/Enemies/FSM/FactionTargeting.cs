using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// The single answer to "who is this entity's enemy".
    ///
    /// <para>Every FSM state used to ask <c>EntityRegistry.Player</c> directly — twenty
    /// call sites across Idle, Patrol, Chase, AlertChase, Attack, Flee and NPCCast — which
    /// hard-codes the assumption that the only thing worth attacking is the player. That
    /// assumption is why summoning has never worked in this project: a summon spawned
    /// through the monster pipeline would immediately hunt the person who cast it.</para>
    ///
    /// <para>Routing those call sites through here is deliberately the SMALL half of the
    /// alternative. The other option was a dedicated <c>Monster_Ally</c> FSM set whose states
    /// target the NPC layer, which forks the state classes — and a forked state class is how
    /// a project acquires two implementations of chasing that drift apart on the first fix.
    /// One helper and a mechanical substitution also buys monster-versus-monster fights,
    /// boss adds and charmed enemies for free, none of which the fork would.</para>
    ///
    /// <para>The hostile path — every monster in the game, almost always — costs one integer
    /// comparison more than the old code: <see cref="AlliedUnit.AnyLive"/> is a Count check
    /// on a list that is normally empty.</para>
    /// </summary>
    public static class FactionTargeting
    {
        /// <summary>
        /// The entity <paramref name="seeker"/> should be hunting, or null when there is
        /// nothing to hunt.
        ///
        /// <para>For an ordinary monster that is the player, unless an ally is CLOSER — a
        /// summon that could not be attacked would be an invulnerable turret, and the spell
        /// is supposed to be a companion, not a wall. For an ally it is the nearest hostile
        /// monster.</para>
        /// </summary>
        public static GameObject EnemyOf(GameObject seeker)
        {
            if (seeker == null) return EntityRegistry.Player;

            return AlliedUnit.IsAllied(seeker)
                ? NearestHostileTo(seeker)
                : NearestPlayerSideTo(seeker);
        }

        /// <summary>Transform form of <see cref="EnemyOf"/>, for the call sites that only
        /// need a position.</summary>
        public static Transform EnemyTransformOf(GameObject seeker)
        {
            var target = EnemyOf(seeker);
            return target != null ? target.transform : null;
        }

        /// <summary>
        /// Distance from <paramref name="seeker"/> to its enemy, or
        /// <see cref="float.MaxValue"/> when it has none. Named for what it measures rather
        /// than for the player, because on an ally it is not the player it measures.
        /// </summary>
        public static float DistanceToEnemy(GameObject seeker)
        {
            if (seeker == null) return float.MaxValue;
            var target = EnemyOf(seeker);
            if (target == null) return float.MaxValue;
            return Vector2.Distance(seeker.transform.position, target.transform.position);
        }

        /// <summary>
        /// What a hostile monster hunts: the player, or a nearer ally.
        /// </summary>
        private static GameObject NearestPlayerSideTo(GameObject seeker)
        {
            GameObject player = EntityRegistry.Player;

            // ONE read, not two. Asking AnyLive and then Live prunes the registry twice on
            // every query that actually has an ally out -- and Live already returns a shared
            // empty array when there is none, so the early exit costs nothing either way.
            var allies = AlliedUnit.Live;
            if (allies.Count == 0) return player;

            Vector2 from = seeker.transform.position;
            GameObject best = player;
            float bestSq = player != null
                ? ((Vector2)player.transform.position - from).sqrMagnitude
                : float.PositiveInfinity;
            for (int i = 0; i < allies.Count; i++)
            {
                var ally = allies[i];
                if (ally == null || !ally.isActiveAndEnabled) continue;
                if (ally.gameObject == seeker) continue;
                if (!IsViableTarget(ally.gameObject)) continue;

                float sq = ((Vector2)ally.transform.position - from).sqrMagnitude;
                if (sq >= bestSq) continue;
                bestSq = sq;
                best = ally.gameObject;
            }

            return best;
        }

        /// <summary>
        /// What an ally hunts: the nearest living monster that is not itself an ally.
        /// </summary>
        private static GameObject NearestHostileTo(GameObject seeker)
        {
            Vector2 from = seeker.transform.position;
            GameObject best = null;
            float bestSq = float.PositiveInfinity;

            var monsters = EntityRegistry.Monsters;
            for (int i = 0; i < monsters.Count; i++)
            {
                var monster = monsters[i];
                if (monster == null || monster == seeker) continue;
                if (AlliedUnit.IsAllied(monster)) continue;      // never turn on our own side
                if (!IsViableTarget(monster)) continue;

                float sq = ((Vector2)monster.transform.position - from).sqrMagnitude;
                if (sq >= bestSq) continue;
                bestSq = sq;
                best = monster;
            }

            return best;
        }

        /// <summary>
        /// A corpse is not a target. Without this an ally would walk to whatever it killed
        /// last and stand there swinging until the body despawned, which reads as the summon
        /// being broken rather than as it having won.
        /// </summary>
        private static bool IsViableTarget(GameObject candidate)
        {
            if (candidate == null || !candidate.activeInHierarchy) return false;
            var health = candidate.GetComponent<Health>();
            return health == null || !health.IsDead;
        }
    }
}
