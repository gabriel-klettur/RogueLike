using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Static registry of passive-aura handlers. Each aura is a handler
    /// (Action&lt;GameObject, float&gt;) keyed by an aura id; <see cref="SkillEffectApplicator"/>
    /// looks up the id when a <see cref="Valkur.Data.SkillEffectKind.PassiveAura"/>
    /// effect fires and invokes the handler against the entity that
    /// learned the skill.
    ///
    /// Why a static registry instead of a MonoBehaviour bus: aura
    /// definitions are pure code (regen tick, damage reduction, crit
    /// chance bump), not assets. Putting them in a registry keeps the
    /// authoring location next to the effect's implementation. Mods
    /// (Phase 3, deprecated) would have replaced this; for now the
    /// registry is closed-set.
    ///
    /// Lifecycle: handlers are typically registered at boot via a static
    /// init or a bootstrap step. <see cref="Register"/> is idempotent
    /// per id (re-registering the same id replaces the handler), which
    /// matches editor hot-reload semantics — re-running InitializeBuiltins
    /// after a recompile is safe.
    ///
    /// Domain Reload OFF: Unity does NOT reset static state between play
    /// sessions when Enter Play Mode Options have Domain Reload disabled.
    /// <see cref="ResetForRuntime"/> is wired via
    /// [RuntimeInitializeOnLoadMethod(SubsystemRegistration)] to clear
    /// the registry on each Play start so stale handlers from a previous
    /// session don't leak in.
    /// </summary>
    public static class AuraRegistry
    {
        private static readonly Dictionary<string, Action<GameObject, float>> _handlers
            = new Dictionary<string, Action<GameObject, float>>(StringComparer.OrdinalIgnoreCase);

        // Auto-clear on Play start to dodge the Domain-Reload-OFF gotcha.
        // Without this, a handler registered during one Play session
        // survives into the next, where the GameObject it captured is
        // already destroyed.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForRuntime()
        {
            _handlers.Clear();
            InitializeBuiltins();
        }

        public static int HandlerCount => _handlers.Count;

        /// <summary>
        /// Register or replace the handler for an aura id. The handler
        /// receives (entity, magnitude) where magnitude is the value field
        /// from <see cref="Valkur.Data.SkillEffect.value"/>.
        /// </summary>
        public static void Register(string auraId, Action<GameObject, float> handler)
        {
            if (string.IsNullOrEmpty(auraId) || handler == null) return;
            _handlers[auraId] = handler;
        }

        public static bool Unregister(string auraId)
        {
            return !string.IsNullOrEmpty(auraId) && _handlers.Remove(auraId);
        }

        public static bool TryApply(string auraId, GameObject entity, float magnitude)
        {
            if (string.IsNullOrEmpty(auraId)) return false;
            if (!_handlers.TryGetValue(auraId, out var handler)) return false;
            try
            {
                handler.Invoke(entity, magnitude);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AuraRegistry] Handler for '{auraId}' threw: {ex.Message}");
                return false;
            }
        }

        public static bool IsRegistered(string auraId)
            => !string.IsNullOrEmpty(auraId) && _handlers.ContainsKey(auraId);

        // ── Built-in auras ──────────────────────────────────────────────────────
        // Auras shipped with the base game. Mods (deprecated) would extend
        // this list externally. Each handler is intentionally tiny — auras
        // that need per-frame ticks should add a MonoBehaviour to the
        // entity here and let it handle its own lifecycle.

        private static void InitializeBuiltins()
        {
            // Toughness: passive HP regen at the magnitude (HP/sec). Wires
            // a HpRegenAura component onto the entity if not already present.
            Register("toughness", (entity, magnitude) =>
            {
                if (entity == null || magnitude <= 0f) return;
                var aura = entity.GetComponent<HpRegenAura>();
                if (aura == null) aura = entity.AddComponent<HpRegenAura>();
                aura.AddRegen(magnitude);
            });

            // Manaflow: passive mana regen bonus, same pattern.
            Register("manaflow", (entity, magnitude) =>
            {
                if (entity == null || magnitude <= 0f) return;
                var mana = entity.GetComponent<Mana>();
                if (mana == null) return;
                // Mana already exposes regenPerSecond as a private field;
                // we read+write via a dedicated Mana.AddRegenBonus method
                // (added separately so the field stays encapsulated).
                mana.AddRegenBonus(magnitude);
            });
        }

        // Test seam — clears state without re-running InitializeBuiltins so
        // unit tests can register their own handlers in isolation.
        public static void ClearForTesting()
        {
            _handlers.Clear();
        }

        public static void InitializeBuiltinsForTesting()
        {
            InitializeBuiltins();
        }
    }
}
