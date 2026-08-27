using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Per-monster feel knobs for the FSM.
    ///
    /// These lived as <c>private const float</c> inside the state classes — eleven of them,
    /// two of them duplicated verbatim between <c>ChaseState</c> and <c>AlertChaseState</c>
    /// where they were free to drift apart the moment one was edited. The consequence was
    /// that aggro hysteresis, repath cadence, leash length, flee timing and re-swing reach
    /// were identical for every monster in the game and changing any of them meant a
    /// recompile, so encounter feel could only be expressed through HP and damage.
    ///
    /// <b>Zero means "use the engine default".</b> That is why every field can stay at its
    /// serialized default and every shipped monster behaves exactly as it did — the same
    /// convention <c>AttackVariant.minDistance</c>/<c>maxDistance</c> already uses for
    /// "no bound". The defaults themselves live in one place, <c>FSMTuning</c>, so the
    /// number in the tooltip and the number the runtime uses cannot disagree.
    /// </summary>
    [Serializable]
    public struct AIBehaviourTuning
    {
        [Header("Perception")]
        [Tooltip("How far past aggro_range the target must get before the chase breaks off, " +
                 "as a multiple. Prevents a monster oscillating on the edge of its ring. " +
                 "0 = default 1.15.")]
        [Min(0f)] public float aggroExitHysteresis;

        [Tooltip("World units from its spawn point at which a chasing monster gives up and " +
                 "walks home. 0 = default is three times this monster's aggro range.")]
        [Min(0f)] public float leashRange;

        [Header("Pathing")]
        [Tooltip("Seconds between A* repaths while chasing. Lower is more responsive and " +
                 "more expensive — this is the knob that decides how many monsters a fight " +
                 "can hold. 0 = default 0.5.")]
        [Min(0f)] public float repathInterval;

        [Tooltip("How close counts as having reached a waypoint, in world units. " +
                 "0 = default 0.25.")]
        [Min(0f)] public float waypointReachDistance;

        [Header("Reactions")]
        [Tooltip("Seconds an alerted monster investigates after being hit from out of range " +
                 "before returning to patrol. 0 = default 5.")]
        [Min(0f)] public float alertDuration;

        [Tooltip("Seconds a fleeing monster runs before it turns back. 0 = default 3.")]
        [Min(0f)] public float fleeDuration;

        [Tooltip("How much faster than its walk speed a panicking monster moves. " +
                 "0 = default 1.5.")]
        [Min(0f)] public float fleeSpeedMultiplier;

        [Header("Melee")]
        [Tooltip("How far past melee range the target may drift before the monster stops " +
                 "re-swinging and chases again, as a multiple. 0 = default 1.5.")]
        [Min(0f)] public float reswingRangeFactor;
    }
}
