using System.Collections.Generic;
using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Who has hurt this entity, how much, and how recently — the thing that decides who it
    /// fights when more than one candidate is on the field.
    ///
    /// <para><b>WITHOUT IT, TARGETING IS "WHOEVER IS NEAREST", RE-EVALUATED EVERY FRAME.</b>
    /// That is what <c>FactionTargeting</c> did, and three consequences followed from it, none
    /// of them visible in the code. Damage bought no attention at all, so a summon that spends
    /// a fight tanking and a player who spends it nuking were equally interesting to a monster.
    /// There was no hysteresis, so two candidates at similar range made a monster's target flip
    /// frame to frame — it would face one, start a swing, and re-aim mid-windup. And nothing
    /// could be pulled, peeled or taunted, which removes the whole vocabulary of party
    /// fighting from a game that has summons and thralls in it.</para>
    ///
    /// <para><b>THREAT DECAYS ON A HALF-LIFE, NOT A TIMEOUT.</b> A timeout has an edge: the
    /// instant it expires the monster forgets everything and re-picks by distance, which reads
    /// as it suddenly losing interest for no reason. A half-life makes disengaging GRADUAL —
    /// stop hitting something and its hold on the monster fades until somebody else's damage
    /// naturally outweighs it, which is the behaviour a player can actually learn.</para>
    ///
    /// <para><b>THE SWITCH NEEDS A MARGIN, and that margin is the point.</b> A challenger must
    /// beat the current leader by <see cref="SwitchMargin"/> before the monster turns. Without
    /// it two attackers doing similar damage make the target oscillate, which is worse than
    /// either choice: the monster spends the fight turning and lands nothing. The margin is
    /// what turns a table of numbers into a DECISION.</para>
    ///
    /// <para>Range-limited on purpose: threat that reaches across the map would let a player
    /// hit a monster once from a rooftop and own its attention forever, past the point where
    /// the monster can even see them. The leash already stops the chase; this stops the
    /// intent.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThreatMemory : MonoBehaviour
    {
        /// <summary>
        /// Seconds for a contribution to lose half its weight. Long enough that a fight is one
        /// continuous decision; short enough that whoever is hitting the monster NOW wins
        /// against whoever hit it at the start.
        /// </summary>
        public const float HalfLifeSeconds = 8f;

        /// <summary>
        /// How far ahead a challenger must be before the monster turns to it. 1.35 is roughly
        /// "a third more damage" — enough that a peel is a deliberate act rather than noise.
        /// </summary>
        public const float SwitchMargin = 1.35f;

        /// <summary>
        /// Below this a contribution is not worth keeping. Prevents a table full of one-point
        /// splash entries from outvoting the real fight.
        /// </summary>
        private const float ForgetBelow = 0.5f;

        /// <summary>Cap on tracked attackers. Anything past this evicts the weakest.</summary>
        private const int MaxTracked = 8;

        private struct Entry
        {
            public GameObject Who;
            public float Threat;     // value at Stamp, before decay
            public float Stamp;      // Time.time when Threat was last written

            /// <summary>
            /// Cached at the moment the attacker was first recorded.
            ///
            /// <para>Resolved once rather than per read: <see cref="Top"/> is asked by
            /// <c>FactionTargeting</c>, which every chasing, attacking, casting and dodging
            /// monster reaches at least once a frame, and a <c>GetComponent</c> per entry per
            /// read is up to eight of them per monster per frame for a value that cannot
            /// change. Unity's fake-null still answers correctly when the attacker is
            /// destroyed, which is the one thing the cache has to keep getting right.</para>
            /// </summary>
            public Health Health;
        }

        private readonly List<Entry> _entries = new List<Entry>(MaxTracked);
        private GameObject _leader;

        /// <summary>
        /// How far a threat target may be before it stops counting. Set from the entity's own
        /// aggro range at spawn; zero means unlimited, which is what a hand-built test double
        /// gets and what keeps its behaviour predictable.
        /// </summary>
        [SerializeField]
        [Tooltip("World units. A threat target further than this is ignored, so a single hit " +
                 "from out of reach cannot own this entity's attention. 0 = unlimited.")]
        private float maxRange;

        /// <summary>Written by EntitySetup from the monster's own aggro range.</summary>
        public void SetMaxRange(float range) => maxRange = Mathf.Max(0f, range);

        /// <summary>The current leader without re-evaluating. For diagnostics and the overlay.</summary>
        public GameObject CurrentLeader => _leader;

        /// <summary>Number of attackers being tracked, decayed entries included.</summary>
        public int TrackedCount => _entries.Count;

        /// <summary>
        /// Note that <paramref name="attacker"/> did <paramref name="amount"/> damage.
        ///
        /// <para>Self-damage and null attackers are dropped: a burn tick with no source, or an
        /// entity standing in its own hazard, must not make it hunt itself.</para>
        /// </summary>
        public void Record(GameObject attacker, float amount)
        {
            if (attacker == null || attacker == gameObject || amount <= 0f) return;

            float now = Time.time;

            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Who != attacker) continue;

                var updated = _entries[i];
                updated.Threat = DecayedValue(updated, now) + amount;
                updated.Stamp = now;
                _entries[i] = updated;
                return;
            }

            if (_entries.Count >= MaxTracked) EvictWeakest(now);
            _entries.Add(new Entry
            {
                Who = attacker,
                Threat = amount,
                Stamp = now,
                Health = attacker.GetComponent<Health>(),
            });
        }

        /// <summary>
        /// Who this entity should be fighting, or null when nothing has earned it.
        ///
        /// <para>Null is the honest answer for a monster nobody has hit, and it is what makes
        /// this layer additive: <c>FactionTargeting</c> falls back to its distance rule, so
        /// every monster in a fight that has not started yet behaves exactly as it always
        /// did.</para>
        /// </summary>
        public GameObject Top()
        {
            float now = Time.time;
            Prune(now);
            if (_entries.Count == 0) { _leader = null; return null; }

            GameObject best = null;
            float bestValue = 0f;
            float leaderValue = 0f;

            for (int i = 0; i < _entries.Count; i++)
            {
                float value = DecayedValue(_entries[i], now);
                if (_entries[i].Who == _leader) leaderValue = value;
                if (value <= bestValue) continue;
                bestValue = value;
                best = _entries[i].Who;
            }

            if (best == null) { _leader = null; return null; }

            // Sticky. The leader keeps the entity's attention until somebody clearly outbids
            // it — see SwitchMargin.
            if (_leader != null && best != _leader && bestValue < leaderValue * SwitchMargin)
                return _leader;

            _leader = best;
            return best;
        }

        /// <summary>Drop one attacker outright — used when a target dies or is dismissed.</summary>
        public void Forget(GameObject who)
        {
            if (who == null) return;
            for (int i = _entries.Count - 1; i >= 0; i--)
                if (_entries[i].Who == who) _entries.RemoveAt(i);
            if (_leader == who) _leader = null;
        }

        /// <summary>Wipe the table. For a pooled entity going back into the pool.</summary>
        public void Clear()
        {
            _entries.Clear();
            _leader = null;
        }

        private float DecayedValue(Entry e, float now)
        {
            float age = now - e.Stamp;
            if (age <= 0f) return e.Threat;
            return e.Threat * Mathf.Pow(0.5f, age / HalfLifeSeconds);
        }

        /// <summary>
        /// Removes entries that are dead, destroyed, faded or out of range. Called on every
        /// read rather than on a timer: the list is at most eight long, and a timer would be a
        /// second clock to keep in step with the decay.
        /// </summary>
        private void Prune(float now)
        {
            Vector2 here = transform.position;
            float maxSq = maxRange > 0f ? maxRange * maxRange : float.MaxValue;

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];

                if (e.Who == null || !e.Who.activeInHierarchy) { RemoveAt(i); continue; }
                if (DecayedValue(e, now) < ForgetBelow) { RemoveAt(i); continue; }
                if (e.Health != null && e.Health.IsDead) { RemoveAt(i); continue; }

                if (((Vector2)e.Who.transform.position - here).sqrMagnitude > maxSq) { RemoveAt(i); continue; }
            }
        }

        private void RemoveAt(int index)
        {
            if (_entries[index].Who == _leader) _leader = null;
            _entries.RemoveAt(index);
        }

        private void EvictWeakest(float now)
        {
            int weakest = -1;
            float weakestValue = float.MaxValue;
            for (int i = 0; i < _entries.Count; i++)
            {
                float value = DecayedValue(_entries[i], now);
                if (value >= weakestValue) continue;
                weakestValue = value;
                weakest = i;
            }
            if (weakest >= 0) RemoveAt(weakest);
        }
    }
}
