using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Where around its target each attacker stands, so a pack SURROUNDS instead of queueing.
    ///
    /// <para><b>Every chaser used to steer at the target's centre.</b> With one monster that is
    /// correct and invisible; with six it is a conga line — they arrive along the same bearing,
    /// stack on the same tile, and <c>NPCSeparationSystem</c> spends the fight pushing them off
    /// each other, which reads as a scrum rather than as an encirclement. Only the front one can
    /// reach the player, so five of the six are decoration, and the player can hold a doorway
    /// against a horde by standing still. Surrounding is the cheapest thing that makes a group
    /// feel like a group, and it is a targeting change rather than a new behaviour.</para>
    ///
    /// <para><b>A REGISTRY, not a hash of the instance id.</b> Hashing needs no state and no
    /// reset hook, and it was tried first: it gives each monster a stable angle but no guarantee
    /// two of them are not within a few degrees of each other, so a pack of three routinely
    /// lands two on the same side and leaves the target a free flank. Claiming a slot from a
    /// list is the only version that can promise an even spread, and the promise is the whole
    /// feature.</para>
    ///
    /// <para><b>Slots are stable while membership holds.</b> A monster keeps its index for as
    /// long as it is engaged, so the ring does not re-deal every frame — re-dealing makes the
    /// whole pack cross over each other whenever one dies, which looks like a bug and undoes the
    /// separation system's work.</para>
    ///
    /// <para><b>It biases, it does not command.</b> The slot is an OFFSET applied to the point a
    /// chaser steers at, so line of sight, the leash, the standoff band and pathfinding all still
    /// decide what actually happens. A monster whose slot is behind a wall simply fails to reach
    /// it and closes normally — the ring must never be able to make a monster refuse to fight.</para>
    /// </summary>
    public static class EngagementRing
    {
        /// <summary>
        /// Past this many claimants the ring stops subdividing: sixteen bodies on one target is
        /// already a wall, and finer slots would put them closer together than the separation
        /// system can hold them apart.
        /// </summary>
        private const int MaxSlots = 16;

        /// <summary>
        /// Seconds a claim survives without being renewed. A monster that dies, de-aggros or
        /// switches target stops asking, and its slot has to be recycled — but not on the frame
        /// it misses one tick, or a monster losing line of sight for an instant would give up its
        /// place and take a different one when it came back.
        /// </summary>
        private const float ClaimTimeoutSeconds = 2f;

        private struct Claim
        {
            public int OwnerId;
            public float Renewed;
        }

        // Not readonly: DomainReloadStaticResetTests reads this hook's raw IL and accepts only
        // `stsfld` or `field.Clear()`. A Dictionary does have Clear(), but the per-target lists
        // inside it hold entries from a previous Play session, so assigning a fresh dictionary is
        // both simpler and what the scanner recognises.
        private static Dictionary<int, List<Claim>> _byTarget = new Dictionary<int, List<Claim>>(8);

        /// <summary>
        /// Domain Reload is OFF, so this dictionary would otherwise carry a previous session's
        /// instance ids straight into the next one — and an id is reused by Unity, so the stale
        /// entries would not merely be dead, they would silently belong to somebody else.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _byTarget = new Dictionary<int, List<Claim>>(8);
        }

        /// <summary>
        /// The point <paramref name="seeker"/> should steer at while engaging
        /// <paramref name="target"/>: the target's position pushed out along this seeker's own
        /// slot bearing.
        /// </summary>
        /// <param name="standoff">
        /// How far out the ring sits. For a melee monster this is its reach, so it arrives
        /// swinging rather than walking through the target; for a caster it is the standoff band.
        /// </param>
        public static Vector2 ApproachPoint(GameObject seeker, GameObject target, float standoff)
        {
            if (seeker == null || target == null) return Vector2.zero;

            Vector2 targetPos = target.transform.position;
            if (standoff <= 0f) return targetPos;

            int slot = ClaimSlot(seeker, target, out int occupancy);

            // One attacker has no ring to stand on: sending it to an arbitrary bearing would make
            // a solo monster walk around its target before engaging, which is the behaviour this
            // exists to prevent, not to create.
            if (occupancy <= 1) return targetPos;

            Vector2 fromTarget = (Vector2)seeker.transform.position - targetPos;
            float baseAngle = fromTarget.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(fromTarget.y, fromTarget.x)
                : 0f;

            // Anchored on the bearing the FIRST claimant approached from, so the ring forms
            // around the fight as it actually started rather than around world +X. Slot 0 keeps
            // that bearing and everyone else fans off it.
            float step = 2f * Mathf.PI / Mathf.Min(occupancy, MaxSlots);
            float angle = baseAngle + step * slot;

            return targetPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * standoff;
        }

        /// <summary>Give up this seeker's place. Called when it stops engaging.</summary>
        public static void Release(GameObject seeker, GameObject target)
        {
            if (seeker == null || target == null) return;
            if (!_byTarget.TryGetValue(target.GetInstanceID(), out var claims)) return;

            int id = seeker.GetInstanceID();
            for (int i = claims.Count - 1; i >= 0; i--)
                if (claims[i].OwnerId == id) claims.RemoveAt(i);

            if (claims.Count == 0) _byTarget.Remove(target.GetInstanceID());
        }

        /// <summary>How many attackers currently hold a slot on this target. For the overlay.</summary>
        public static int OccupancyOf(GameObject target)
        {
            if (target == null) return 0;
            if (!_byTarget.TryGetValue(target.GetInstanceID(), out var claims)) return 0;
            Prune(claims);
            return claims.Count;
        }

        /// <summary>
        /// This seeker's index in the target's ring, renewing its claim. Claims are pruned first,
        /// so a slot freed by a dead attacker is reused rather than leaving a gap in the circle.
        /// </summary>
        private static int ClaimSlot(GameObject seeker, GameObject target, out int occupancy)
        {
            int targetId = target.GetInstanceID();
            if (!_byTarget.TryGetValue(targetId, out var claims))
            {
                claims = new List<Claim>(4);
                _byTarget[targetId] = claims;
            }

            Prune(claims);

            int id = seeker.GetInstanceID();
            for (int i = 0; i < claims.Count; i++)
            {
                if (claims[i].OwnerId != id) continue;
                claims[i] = new Claim { OwnerId = id, Renewed = Time.time };
                occupancy = claims.Count;
                return i;
            }

            claims.Add(new Claim { OwnerId = id, Renewed = Time.time });
            occupancy = claims.Count;
            return claims.Count - 1;
        }

        private static void Prune(List<Claim> claims)
        {
            float now = Time.time;
            for (int i = claims.Count - 1; i >= 0; i--)
                if (now - claims[i].Renewed > ClaimTimeoutSeconds) claims.RemoveAt(i);
        }
    }
}
