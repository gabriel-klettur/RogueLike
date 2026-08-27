using System.Collections.Generic;
using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Marks the entities currently standing in the flame.
    ///
    /// <para>Without this, being inside an arcane hazard is invisible between beats: a DoT
    /// tick fires a 0.14 s hit flash, which covers under a quarter of the 0.6 s period, so
    /// for most of every beat a burning monster looks exactly like one standing safely
    /// beside the fire. A held tint says "this one is in it" continuously.</para>
    ///
    /// <para>The tint goes through <see cref="SpriteTintStack"/> and nowhere else.
    /// <c>SpriteRenderer.color</c> on an entity body has exactly one owner: nine systems
    /// used to cache it, tint, and write the cache back, and a monster hit while burning
    /// captured orange as its baseline and kept it forever. Layers compose, so an entity
    /// that is burning AND standing in arcane fire shows both.</para>
    /// </summary>
    public partial class ArcaneFlameController
    {
        /// <summary>The colour the mark would produce on a white sprite.</summary>
        private static readonly Color ArcaneMarkTint = new Color(0.80f, 0.58f, 1.00f);

        /// <summary>
        /// Extra time a mark outlives the tick that set it. Must exceed one tick period or
        /// the mark blinks out between beats — the exact flicker this layer exists to
        /// remove. Below it is the fade-out that follows an entity walking clear.
        /// </summary>
        private const float MarkGrace = 0.22f;

        private readonly Dictionary<Health, float> _marked = new Dictionary<Health, float>();
        private readonly List<Health> _markSweepBuffer = new List<Health>();

        private void MarkVictims()
        {
            float until = Time.time + _tickPeriod + MarkGrace;
            foreach (var health in _tickVictims)
            {
                if (health == null) continue;
                // Attach on the ENTITY ROOT, resolved from the Health component's own
                // GameObject — never off the collider that OverlapCircle returned. A stack
                // on a child renderer mints a SECOND base colour and reopens the bug.
                var stack = SpriteTintStack.Attach(health.gameObject);
                // Null is the documented answer for an entity with no body sprite
                // (spawners, triggers, test doubles all reach this without one).
                if (stack == null) continue;
                stack.Set(TintLayer.Arcane, ArcaneMarkTint);
                _marked[health] = until;
            }
        }

        /// <summary>Drop the mark from anything that walked out, died, or was destroyed.</summary>
        private void SweepMarks()
        {
            if (_marked.Count == 0) return;

            float now = Time.time;
            _markSweepBuffer.Clear();
            foreach (var kv in _marked)
            {
                if (kv.Value <= now || kv.Key == null) _markSweepBuffer.Add(kv.Key);
            }

            for (int i = 0; i < _markSweepBuffer.Count; i++)
            {
                var health = _markSweepBuffer[i];
                ClearMark(health);
                _marked.Remove(health);
            }
        }

        /// <summary>
        /// Called from <c>OnDestroy</c>, which is the only callback reached on all five
        /// exit paths. Skipping it would leave every monster that was standing in the fire
        /// permanently violet — the fade is on OUR timeline, and our timeline just ended.
        /// </summary>
        private void ClearAllMarks()
        {
            foreach (var kv in _marked) ClearMark(kv.Key);
            _marked.Clear();
        }

        private static void ClearMark(Health health)
        {
            // Unity fake-null: a destroyed entity compares equal to null while still being
            // a live dictionary key, so this must be checked rather than assumed.
            if (health == null) return;
            var stack = health.GetComponent<SpriteTintStack>();
            if (stack != null) stack.Clear(TintLayer.Arcane);
        }
    }
}
