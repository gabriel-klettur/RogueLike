using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Turns a <see cref="StatusApplication"/> array — the data authored on
    /// <c>SpellDefinition.statusApplications</c> — into real <see cref="StatusEffect"/>
    /// instances applied to a struck target. This is the ONLY place that instantiates a
    /// concrete effect from data; every damage seam that wants to honour a spell's status
    /// applications calls <see cref="ApplyAll"/> rather than constructing effects itself.
    /// </summary>
    public static class StatusApplicationFactory
    {
        /// <summary>
        /// Rolls and applies every entry in <paramref name="applications"/> against
        /// <paramref name="target"/>. Each entry rolls its own <c>chance</c> independently,
        /// so a spell with two applications (e.g. slow + poison) doesn't lose the second
        /// because the first missed. Safe to call with a null/empty array (no-op) — every
        /// spell authored before this field existed keeps working unchanged.
        /// </summary>
        public static void ApplyAll(StatusApplication[] applications, GameObject target, GameObject applier)
        {
            if (applications == null || applications.Length == 0 || target == null) return;

            StatusEffectManager mgr = null;
            for (int i = 0; i < applications.Length; i++)
            {
                StatusApplication app = applications[i];
                if (app.duration <= 0f || app.chance <= 0f) continue;
                if (Random.value > app.chance) continue;

                // Lazily resolved: most calls hit an array with zero live entries (the
                // overwhelming majority of shipped spells), so no point paying a
                // GetComponent before we know there is something to apply.
                if (mgr == null)
                {
                    mgr = target.GetComponent<StatusEffectManager>();
                    if (mgr == null) return;
                }

                StatusEffect effect = Build(app, applier);
                if (effect != null) mgr.Apply(effect);
            }
        }

        private static StatusEffect Build(StatusApplication app, GameObject applier)
        {
            switch (app.type)
            {
                case StatusEffectKind.Burn:
                    return new BurnEffect(app.duration, Mathf.RoundToInt(Mathf.Max(1f, app.magnitude)), applier: applier);
                case StatusEffectKind.Poison:
                    return new PoisonEffect(app.duration, Mathf.RoundToInt(Mathf.Max(1f, app.magnitude)), applier: applier);
                case StatusEffectKind.Stun:
                    return new StunEffect(app.duration, applier);
                case StatusEffectKind.Freeze:
                    return new FreezeEffect(app.duration, applier);
                case StatusEffectKind.Slow:
                    return new SlowEffect(app.duration, app.magnitude > 0f ? app.magnitude : 0.5f, applier);
                case StatusEffectKind.Root:
                    // No magnitude: a hold is binary. Anything an author types there is
                    // ignored rather than silently meaning something.
                    return new RootEffect(app.duration, applier);
                default:
                    return null;
            }
        }
    }
}
