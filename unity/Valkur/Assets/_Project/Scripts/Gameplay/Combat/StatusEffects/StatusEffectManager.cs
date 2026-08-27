using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Central hub for status effects on a single entity.
    /// Attach to any damageable GameObject (player, monsters, buildings).
    ///
    /// Mirrors Python DoTSystem + StunSystem — but per-entity rather than
    /// a global ECS system, fitting the Unity MonoBehaviour pattern.
    ///
    /// Usage:
    ///   var mgr = target.GetComponent&lt;StatusEffectManager&gt;();
    ///   mgr.Apply(new BurnEffect(duration: 4f, damagePerTick: 3));
    ///   mgr.Apply(new StunEffect(duration: 1.5f));
    ///   bool stunned = mgr.HasEffect&lt;StunEffect&gt;();
    /// </summary>
    public class StatusEffectManager : MonoBehaviour
    {
        // Active effects keyed by their runtime Type — one instance per type max.
        // Re-applying the same type replaces the previous instance (refresh semantics).
        private readonly Dictionary<Type, StatusEffect> _active = new();
        // Reused removal buffer — hoisted out of Update so the GC doesn't pay for
        // a new List<Type> every frame an entity has active effects (DoTs, stuns,
        // freezes during combat). With ~7 status managers, the original code
        // allocated up to 7 lists/frame, triggering Gen0 GC in tight combat loops.
        private readonly List<Type> _removalBuffer = new();

        // Status effect kinds this entity refuses outright. Wired from
        // MonsterDefinition.stats.statusImmunities by EntitySetup via SetImmunities; empty
        // (the default) means immune to nothing, exactly as before this field existed.
        [SerializeField]
        [Tooltip("Status effect kinds Apply() refuses before OnApply ever runs. Usually set " +
                 "at runtime via SetImmunities rather than authored here directly.")]
        private StatusEffectKind[] immuneKinds = Array.Empty<StatusEffectKind>();

        public event Action<StatusEffect> OnEffectApplied;
        public event Action<StatusEffect> OnEffectRemoved;
        // Fired when Apply() refuses an effect because the entity is immune to its Kind.
        // Distinct from OnEffectRemoved (which implies the effect was active at least one
        // frame) so a "shrugged it off" VFX can key off this instead.
        public event Action<StatusEffectKind> OnEffectImmune;

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>Wired from MonsterDefinition.stats.statusImmunities by EntitySetup.</summary>
        public void SetImmunities(StatusEffectKind[] kinds) => immuneKinds = kinds ?? Array.Empty<StatusEffectKind>();

        /// <summary>True when this entity refuses the given status effect kind outright.</summary>
        public bool IsImmuneTo(StatusEffectKind kind)
        {
            if (immuneKinds == null) return false;
            for (int i = 0; i < immuneKinds.Length; i++)
                if (immuneKinds[i] == kind) return true;
            return false;
        }

        /// <summary>
        /// Apply a status effect. If an effect of the same type already exists it is
        /// removed first (refresh/replace), then the new effect is applied. Refused
        /// outright — no OnApply, no OnEffectApplied — when the entity is immune to the
        /// effect's <see cref="StatusEffect.Kind"/> (see <see cref="SetImmunities"/>).
        /// </summary>
        public void Apply(StatusEffect effect)
        {
            if (effect == null) return;

            if (IsImmuneTo(effect.Kind))
            {
                OnEffectImmune?.Invoke(effect.Kind);
                return;
            }

            var type = effect.GetType();

            // Remove existing effect of same type first
            if (_active.TryGetValue(type, out var existing))
                RemoveEffect(type, existing);

            _active[type] = effect;
            effect.OnApply(this);
            OnEffectApplied?.Invoke(effect);
        }

        /// <summary>Remove a specific effect type immediately.</summary>
        public void Remove<T>() where T : StatusEffect
        {
            var type = typeof(T);
            if (_active.TryGetValue(type, out var effect))
                RemoveEffect(type, effect);
        }

        /// <summary>Returns true if the entity currently has the given effect type.</summary>
        public bool HasEffect<T>() where T : StatusEffect
            => _active.ContainsKey(typeof(T));

        /// <summary>Returns true if the entity is currently stunned.</summary>
        public bool IsStunned => HasEffect<StunEffect>();

        /// <summary>Remove all active status effects.</summary>
        public void ClearAll()
        {
            foreach (var kv in new Dictionary<Type, StatusEffect>(_active))
                RemoveEffect(kv.Key, kv.Value);
        }

        // ── Lifecycle ────────────────────────────────────────────────────────

        private void Update()
        {
            if (_active.Count == 0) return;

            // Reuse the hoisted buffer instead of allocating a fresh list each
            // frame. Capacity stays at the high-water mark across frames, which
            // is acceptable for a per-entity manager (a handful of effects max).
            _removalBuffer.Clear();

            foreach (var kv in _active)
            {
                var effect = kv.Value;
                if (effect.IsExpired)
                {
                    _removalBuffer.Add(kv.Key);
                    continue;
                }
                effect.Tick(this);
            }

            for (int i = 0; i < _removalBuffer.Count; i++)
            {
                var type = _removalBuffer[i];
                if (_active.TryGetValue(type, out var expired))
                    RemoveEffect(type, expired);
            }
        }

        private void RemoveEffect(Type type, StatusEffect effect)
        {
            effect.OnRemove(this);
            _active.Remove(type);
            OnEffectRemoved?.Invoke(effect);
        }

        // ── Serialization helper (for Save system) ──────────────────────────

        /// <summary>
        /// Returns a snapshot of currently active effect types and their remaining duration.
        /// Used by SaveService to persist status effects if desired.
        /// </summary>
        public List<(string typeName, float remaining)> GetSnapshot()
        {
            var list = new List<(string, float)>();
            foreach (var kv in _active)
                list.Add((kv.Key.Name, kv.Value.EndTime - Time.time));
            return list;
        }
    }
}
