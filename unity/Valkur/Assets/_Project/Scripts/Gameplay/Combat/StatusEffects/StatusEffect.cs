using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Base class for all status effects applied to entities (Burn, Poison, Stun, etc.).
    /// Mirrors Python's BurnComponent / PoisonComponent / StunComponent data model
    /// combined with the DoTSystem / StunSystem tick logic.
    /// </summary>
    public abstract class StatusEffect
    {
        public float Duration { get; }
        public float StartTime { get; }
        public float EndTime => StartTime + Duration;
        public bool IsExpired => Time.time >= EndTime;

        /// <summary>The GameObject that applied this effect (can be null).</summary>
        public GameObject Applier { get; }

        /// <summary>
        /// Which <see cref="StatusEffectKind"/> this concrete effect is. Lets data-only
        /// code (EntityStats.statusImmunities, SpellDefinition.statusApplications) name a
        /// status effect without referencing the Gameplay class directly —
        /// StatusEffectManager.Apply checks this against the target's immunity list before
        /// OnApply ever runs.
        /// </summary>
        public abstract StatusEffectKind Kind { get; }

        protected StatusEffect(float duration, GameObject applier = null)
        {
            Duration    = duration;
            StartTime   = Time.time;
            Applier     = applier;
        }

        /// <summary>Called every frame while the effect is active.</summary>
        public abstract void Tick(StatusEffectManager target);

        /// <summary>Called once when the effect is first applied.</summary>
        public virtual void OnApply(StatusEffectManager target) { }

        /// <summary>Called once when the effect expires or is forcefully removed.</summary>
        public virtual void OnRemove(StatusEffectManager target) { }
    }
}
