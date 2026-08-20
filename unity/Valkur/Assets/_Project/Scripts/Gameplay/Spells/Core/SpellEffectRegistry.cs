using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Tracks the free-standing GameObjects that spells leave in the world — puddles,
    /// totems, walls, mines, vortices, summons, smoke — so that something can end them.
    ///
    /// Most area spells spawn their effect with <c>new GameObject(...)</c> at scene root and
    /// hold no reference to their caster. Their ONLY termination path is their own countdown.
    /// That is survivable while every effect expires on a timer, and stops being survivable
    /// the moment any of them can be authored to persist: an effect nothing can cancel
    /// outlives the caster's death, the run, and every zone transition, still ticking damage.
    ///
    /// This also makes <see cref="SpellDefinition.maxInstances"/> mean something for the
    /// first time. It has always been authored and read by nothing — see the note in
    /// <c>LightningExecutor</c> — so the real limit on how many puddles could coexist was
    /// the cooldown measured against the duration, which is no limit at all once a spell
    /// never expires.
    /// </summary>
    public static class SpellEffectRegistry
    {
        /// <summary>Live effects per spellKey, oldest first.</summary>
        private static readonly Dictionary<string, List<SpellEffectHandle>> _live =
            new Dictionary<string, List<SpellEffectHandle>>();

        private static bool _subscribed;

        // Domain Reload is OFF: without this the dictionary keeps destroyed handles from
        // the previous play session and the first cast of the next one trips over them.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _live.Clear();
            _subscribed = false;
        }

        /// <summary>Total live tracked effects, all spells. Diagnostics only.</summary>
        public static int LiveCount
        {
            get
            {
                int n = 0;
                foreach (var kv in _live) n += kv.Value.Count;
                return n;
            }
        }

        /// <summary>How many effects of one spell are currently alive.</summary>
        public static int CountOf(string spellKey)
        {
            if (string.IsNullOrEmpty(spellKey)) return 0;
            if (!_live.TryGetValue(spellKey, out var list)) return 0;
            list.RemoveAll(h => h == null);   // Unity == is correct here: it detects destroyed
            return list.Count;
        }

        /// <summary>
        /// Take ownership of a spell-spawned root object. Attaches a
        /// <see cref="SpellEffectHandle"/> that deregisters on destroy, and enforces the
        /// spell's <c>maxInstances</c> by destroying the oldest instance first.
        ///
        /// Safe to call on an object that is about to expire on its own — tracking a
        /// short-lived effect costs one component and gives the cap something to count.
        /// </summary>
        /// <param name="enforceCap">
        /// Apply <c>maxInstances</c> before registering. A spell that spawns several objects
        /// per cast (Summon with <c>summonCount</c> &gt; 1) must pass true for the first object
        /// only, or the cap would count units instead of casts and a 3-unit summon capped at
        /// 1 would destroy two of its own summons on the way out of the loop.
        /// </param>
        public static void Track(GameObject effect, SpellDefinition spell, GameObject caster,
                                 bool enforceCap = true)
        {
            if (effect == null || spell == null || string.IsNullOrEmpty(spell.spellKey)) return;

            EnsureSubscribed();

            string key = spell.spellKey;
            if (!_live.TryGetValue(key, out var list))
            {
                list = new List<SpellEffectHandle>();
                _live[key] = list;
            }

            // Drop entries Unity destroyed without OnDestroy reaching us (scene unload).
            list.RemoveAll(h => h == null);

            // maxInstances <= 0 means "unlimited", matching how the field reads in the F4
            // editor for the spells that author 0.
            int max = spell.maxInstances;
            if (enforceCap && max > 0)
            {
                while (list.Count >= max)
                {
                    var oldest = list[0];
                    list.RemoveAt(0);
                    if (oldest != null) DestroySafely(oldest.gameObject);
                }
            }

            var handle = effect.AddComponent<SpellEffectHandle>();
            handle.Bind(key, caster);
            list.Add(handle);
        }

        /// <summary>
        /// Called by <see cref="SpellEffectHandle.OnDestroy"/>.
        ///
        /// The null check is <see cref="ReferenceEquals"/> and NOT <c>==</c> on purpose.
        /// Unity's overloaded operator already reports a MonoBehaviour as null while that
        /// object's own OnDestroy is running — which is the only moment this method is ever
        /// called. Written as <c>handle == null</c> it returned early every single time, so
        /// nothing was ever deregistered and dead entries kept counting against
        /// maxInstances for the rest of the session.
        /// </summary>
        internal static void Forget(SpellEffectHandle handle)
        {
            if (ReferenceEquals(handle, null) || string.IsNullOrEmpty(handle.SpellKey)) return;
            if (_live.TryGetValue(handle.SpellKey, out var list))
                list.Remove(handle);
        }

        /// <summary>Destroy every tracked effect. Returns how many were destroyed.</summary>
        public static int ClearAll()
        {
            int destroyed = 0;
            foreach (var kv in _live)
            {
                var list = kv.Value;
                // Backwards: each Destroy triggers Forget, which mutates this list.
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var h = list[i];
                    if (h == null) continue;
                    DestroySafely(h.gameObject);
                    destroyed++;
                }
                list.Clear();
            }
            _live.Clear();
            return destroyed;
        }

        /// <summary>
        /// Destroy every tracked effect cast by <paramref name="caster"/>. Used when an
        /// entity dies so its lingering hazards do not outlive it.
        /// </summary>
        public static int ClearOwnedBy(GameObject caster)
        {
            if (caster == null) return 0;
            int destroyed = 0;
            foreach (var kv in _live)
            {
                var list = kv.Value;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var h = list[i];
                    if (h == null) { list.RemoveAt(i); continue; }
                    if (h.Caster != caster) continue;
                    list.RemoveAt(i);
                    DestroySafely(h.gameObject);
                    destroyed++;
                }
            }
            return destroyed;
        }

        /// <summary>
        /// <c>Object.Destroy</c> is deferred to end-of-frame and Unity refuses it outside
        /// Play Mode with an error. The registry only runs at cast time in practice, but
        /// EditMode tests construct gameplay objects freely, and an error logged from a
        /// test run is indistinguishable from a real one.
        /// </summary>
        private static void DestroySafely(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go);
        }

        /// <summary>
        /// Subscribed lazily rather than from the reset hook: GameEvents clears its own
        /// handlers during SubsystemRegistration too, and relying on the order between two
        /// reset hooks is how a subscription silently goes missing. The first cast of a
        /// session happens long after both have run.
        /// </summary>
        private static void EnsureSubscribed()
        {
            if (_subscribed) return;
            GameEvents.OnZoneChanged += HandleZoneChanged;
            _subscribed = true;
        }

        private static void HandleZoneChanged(string oldZone, string newZone)
        {
            int n = ClearAll();
            if (n > 0)
                VerboseLog.Log(VerboseLog.Category.World,
                    () => $"[SpellEffectRegistry] Zone {oldZone} → {newZone}: destroyed {n} lingering spell effect(s).");
        }
    }
}
