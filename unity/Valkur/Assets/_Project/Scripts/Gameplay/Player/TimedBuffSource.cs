using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Owns the <see cref="StatLayer.Buff"/> layer: stat changes that expire on their own
    /// clock — potions, food, shrines.
    ///
    /// It replaces the honest placeholder <c>ItemConsumer</c> shipped with, which was a
    /// <c>Debug.Log</c>, a <c>WaitForSeconds</c> and a second <c>Debug.Log</c> under a
    /// "TODO: integrate with a StatComponent when implemented" — the component it was
    /// waiting for is <see cref="PlayerStats"/>. A "+5 Strength for 30 s" flask wrote two
    /// lines to the console and changed nothing about the character.
    ///
    /// Buffs are keyed so a second flask of the same kind REFRESHES rather than stacks.
    /// That is the same rule <c>StatusEffectManager.Apply</c> follows for burns and the
    /// same one CLAUDE.md records for the cone breath's DoT: re-applying on every tick is
    /// churn, not stacking. A designer who wants stacking authors distinct keys.
    /// </summary>
    [RequireComponent(typeof(PlayerStats))]
    public sealed class TimedBuffSource : MonoBehaviour
    {
        private sealed class ActiveBuff
        {
            public string Key;
            public float ExpiresAt;
            public StatModifier[] Modifiers;
        }

        private PlayerStats _stats;
        private readonly List<ActiveBuff> _active = new List<ActiveBuff>(4);
        private readonly List<StatModifier> _scratch = new List<StatModifier>(16);

        /// <summary>Number of buffs currently running. Read by the HUD and the tests.</summary>
        public int ActiveCount => _active.Count;

        private void Awake() => _stats = GetComponent<PlayerStats>();

        /// <summary>
        /// Applies (or refreshes) a buff for <paramref name="duration"/> seconds.
        /// A duration of zero or less is refused rather than treated as permanent: a
        /// permanent stat change belongs in a layer with an owner who can remove it, and
        /// the buff layer's owner is a clock.
        /// </summary>
        public void Apply(string key, IEnumerable<StatModifier> modifiers, float duration)
        {
            if (string.IsNullOrWhiteSpace(key) || modifiers == null || duration <= 0f) return;
            if (_stats == null) _stats = GetComponent<PlayerStats>();
            if (_stats == null) return;

            var list = new List<StatModifier>(modifiers);
            if (list.Count == 0) return;

            var existing = Find(key);
            if (existing != null)
            {
                existing.Modifiers = list.ToArray();
                existing.ExpiresAt = Time.time + duration;
            }
            else
            {
                _active.Add(new ActiveBuff
                {
                    Key = key,
                    Modifiers = list.ToArray(),
                    ExpiresAt = Time.time + duration,
                });
            }

            Rebuild();
        }

        /// <summary>Convenience for the common single-stat flask.</summary>
        public void Apply(string key, StatKind stat, float value, float duration)
            => Apply(key, new[] { StatModifier.Flat(stat, value) }, duration);

        public bool IsActive(string key) => Find(key) != null;

        /// <summary>Seconds left on a buff, or 0 when it is not running.</summary>
        public float RemainingSeconds(string key)
        {
            var buff = Find(key);
            return buff == null ? 0f : Mathf.Max(0f, buff.ExpiresAt - Time.time);
        }

        public void Remove(string key)
        {
            var buff = Find(key);
            if (buff == null) return;
            _active.Remove(buff);
            Rebuild();
        }

        public void ClearAll()
        {
            if (_active.Count == 0) return;
            _active.Clear();
            Rebuild();
        }

        private void Update()
        {
            if (_active.Count == 0) return;

            bool expired = false;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (Time.time < _active[i].ExpiresAt) continue;
                _active.RemoveAt(i);
                expired = true;
            }

            // Only rebuild when something actually left. A rebuild pushes to Health and
            // Mana, and doing that every frame would burn the whole point of caching the
            // resolved values.
            if (expired) Rebuild();
        }

        private ActiveBuff Find(string key)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (string.Equals(_active[i].Key, key, System.StringComparison.OrdinalIgnoreCase))
                    return _active[i];
            }
            return null;
        }

        private void Rebuild()
        {
            if (_stats == null) return;
            _scratch.Clear();
            foreach (var buff in _active) _scratch.AddRange(buff.Modifiers);
            _stats.SetLayer(StatLayer.Buff, _scratch);
        }
    }
}
