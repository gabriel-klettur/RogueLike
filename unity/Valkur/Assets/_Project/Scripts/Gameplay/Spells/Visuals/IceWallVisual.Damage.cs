using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// How the wall shows what has been done to it: fractures spread, the outermost
    /// crystals snap off, and the barrier NARROWS as it is worn down.
    ///
    /// <para>The narrowing is not decoration. The collider is resized to the surviving span
    /// (see <see cref="SurvivingHalfSpan"/>), so what the player reads off the silhouette is
    /// what the physics actually does. A wall that shows damage while still blocking the
    /// full width teaches the player to ignore the damage.</para>
    /// </summary>
    internal sealed partial class IceWallVisual
    {
        /// <summary>
        /// Fraction of the crystals that can be broken off by damage alone. The rest survive
        /// to be shattered by the killing blow — a wall that erodes to nothing before it
        /// "dies" has no death left to play.
        /// </summary>
        private const float MaxBreakFraction = 0.62f;

        private List<int> _breakOrder;
        private int _brokenCount;

        /// <summary>
        /// Push the accumulated damage, 0 = untouched, 1 = about to die. Idempotent: it may
        /// be called every frame and only acts when it crosses a break threshold.
        /// </summary>
        public void SetDamage01(float damage01)
        {
            damage01 = Mathf.Clamp01(damage01);

            // Fractures open early and keep darkening, so the player sees the wall failing
            // well before the first crystal actually goes.
            float crack = Mathf.Clamp01((damage01 - 0.08f) / 0.55f) * 0.92f;
            for (int i = 0; i < _shards.Count; i++)
                _shards[i].CrackAlpha = crack;

            EnsureBreakOrder();
            int target = Mathf.FloorToInt(_shards.Count * MaxBreakFraction * damage01);
            while (_brokenCount < target && _brokenCount < _breakOrder.Count)
                BreakShard(_shards[_breakOrder[_brokenCount++]], violent: false);
        }

        /// <summary>
        /// A blow landed at <paramref name="worldPoint"/>: flash the crystals around it and
        /// throw a few chips off the contact point.
        /// </summary>
        public void Hit(Vector3 worldPoint)
        {
            float nearest = float.MaxValue;
            for (int i = 0; i < _shards.Count; i++)
            {
                var shard = _shards[i];
                if (shard.Broken || shard.Root == null) continue;

                float distance = Vector2.Distance(worldPoint, shard.Root.position);
                nearest = Mathf.Min(nearest, distance);
                // Everything within a crystal's own width lights up, so a hit reads as
                // landing on a place rather than on the wall as an abstraction.
                if (distance < shard.Width * 2.2f)
                    shard.Flash = Mathf.Max(shard.Flash, Mathf.Clamp01(1f - distance / (shard.Width * 2.2f)));
            }

            if (nearest > _config.Height * 2f) return;
            IceWallDebris.Burst(worldPoint, Vector2.up, count: 5, speed: 3.2f, size: 0.10f);
            IceWallBurstFX.Spawn(worldPoint, radius: 0.55f, seconds: 0.24f, axis: _config.Axis);
        }

        /// <summary>
        /// Half the distance from the centre to the outermost surviving crystal, plus that
        /// crystal's own width. What the collider is resized to as the wall erodes.
        /// </summary>
        public float SurvivingHalfSpan()
        {
            float span = 0f;
            for (int i = 0; i < _shards.Count; i++)
            {
                var shard = _shards[i];
                if (shard.Broken || shard.BackRow) continue;
                span = Mathf.Max(span, Mathf.Abs(shard.Along) + shard.Width * 0.5f);
            }
            return span;
        }

        /// <summary>Break every surviving crystal at once, plus the flash and shockwave.</summary>
        public void Shatter()
        {
            Vector3 centre = _root.position;

            for (int i = 0; i < _shards.Count; i++)
            {
                var shard = _shards[i];
                if (shard.Broken) continue;
                BreakShard(shard, violent: true);
            }

            IceWallBurstFX.Spawn(centre, radius: Mathf.Max(1.2f, _config.Length * 0.5f),
                seconds: 0.42f, axis: _config.Axis);

            if (_mist != null) _mist.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (_sparkle != null) _sparkle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void EnsureBreakOrder()
        {
            if (_breakOrder != null) return;

            // Outermost first: the barrier is eaten from its ends towards the middle, which
            // is both what erosion looks like and what keeps the surviving span contiguous.
            _breakOrder = new List<int>(_shards.Count);
            for (int i = 0; i < _shards.Count; i++) _breakOrder.Add(i);
            _breakOrder.Sort((a, b) =>
                Mathf.Abs(_shards[b].T - 0.5f).CompareTo(Mathf.Abs(_shards[a].T - 0.5f)));
        }

        private void BreakShard(Shard shard, bool violent)
        {
            if (shard.Broken) return;
            shard.Broken = true;

            if (shard.Root != null)
            {
                Vector3 at = shard.Root.position + new Vector3(0f, shard.Height * 0.4f, 0f);
                // Away from the wall's line, so the pieces scatter forwards and back rather
                // than sliding along the barrier they came from.
                Vector2 outward = new Vector2(-_config.Axis.y, _config.Axis.x);
                IceWallDebris.Burst(at, outward,
                    count: violent ? 7 : 4,
                    speed: violent ? 5.5f : 3.0f,
                    size: shard.Width * 0.42f);

                if (violent)
                    IceWallBurstFX.Spawn(at, radius: shard.Width * 1.6f, seconds: 0.26f, axis: _config.Axis);

                shard.Root.gameObject.SetActive(false);
            }
        }
    }
}
