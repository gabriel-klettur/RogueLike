using System;
using System.Collections.Generic;
using UnityEngine;

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

        public event Action<StatusEffect> OnEffectApplied;
        public event Action<StatusEffect> OnEffectRemoved;

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Apply a status effect. If an effect of the same type already exists it is
        /// removed first (refresh/replace), then the new effect is applied.
        /// </summary>
        public void Apply(StatusEffect effect)
        {
            if (effect == null) return;

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

            var toRemove = new List<Type>();

            foreach (var kv in _active)
            {
                var effect = kv.Value;
                if (effect.IsExpired)
                {
                    toRemove.Add(kv.Key);
                    continue;
                }
                effect.Tick(this);
            }

            foreach (var type in toRemove)
            {
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
