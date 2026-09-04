using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// How the barrier shows what has been done to it: fractures open across the cells, the
    /// outermost columns fail, and the weave NARROWS as it is worn down.
    ///
    /// <para>The narrowing is not decoration. <c>WallController</c> resizes the collider to
    /// <see cref="SurvivingHalfSpan"/>, so what the player reads off the silhouette is what the
    /// physics actually does. A barrier that shows damage while still blocking its full width
    /// teaches the player to ignore the damage.</para>
    /// </summary>
    internal sealed partial class ArcaneBarrierVisual
    {
        /// <summary>
        /// Fraction of the cells that damage alone can take. The rest survive to be torn apart
        /// by the killing blow — a barrier that erodes to nothing before it dies has no death
        /// left to play.
        /// </summary>
        private const float MaxBreakFraction = 0.62f;

        private List<int> _breakOrder;
        private int _brokenCount;

        /// <summary>
        /// Push the accumulated damage, 0 = untouched, 1 = about to die. Idempotent: called
        /// every frame, it only acts when it crosses a break threshold.
        /// </summary>
        public void SetDamage01(float damage01)
        {
            damage01 = Mathf.Clamp01(damage01);

            // Fractures open early and keep spreading, so the player sees the weave failing
            // well before the first cell actually goes.
            float crack = Mathf.Clamp01((damage01 - 0.08f) / 0.55f) * 0.92f;
            for (int i = 0; i < _panels.Count; i++)
            {
                var panel = _panels[i];
                if (panel.Broken) continue;
                if (crack > 0.001f) EnsureCrack(panel);
                panel.CrackAlpha = crack;
            }

            EnsureBreakOrder();
            int target = Mathf.FloorToInt(_panels.Count * MaxBreakFraction * damage01);
            while (_brokenCount < target && _brokenCount < _breakOrder.Count)
                BreakPanel(_panels[_breakOrder[_brokenCount++]], violent: false);
        }

        /// <summary>
        /// The fracture overlay is created only when damage first reaches a cell.
        ///
        /// <para>Building it up front would DOUBLE the rig's renderer count for a layer that is
        /// invisible on the great majority of barriers, which expire untouched. The panel keeps
        /// its sprite variant precisely so the fracture lines match the hexagon they split.</para>
        /// </summary>
        private void EnsureCrack(Panel panel)
        {
            if (panel.Crack != null || panel.Root == null) return;

            var go = new GameObject("Fracture");
            go.transform.SetParent(panel.Root, false);
            panel.Crack = Paint(go, ArcaneSprites.Fracture(panel.Variant), _palette.Lattice,
                additive: true, SortingConfig.LAYER_ENTITIES, OrderFor(Part.Crack));
        }

        /// <summary>
        /// A blow landed at <paramref name="worldPoint"/>: light the cells around it and throw
        /// a few chips off the face.
        /// </summary>
        public void Hit(Vector3 worldPoint)
        {
            float cellRadius = PanelHalfWidth * (_panels.Count > 0 ? _panels[0].Size : 0.5f);
            float reach = Mathf.Max(0.45f, cellRadius * 2.6f);

            float nearest = float.MaxValue;
            for (int i = 0; i < _panels.Count; i++)
            {
                var panel = _panels[i];
                if (panel.Broken || panel.Root == null) continue;

                float distance = Vector2.Distance(worldPoint, panel.Root.position);
                nearest = Mathf.Min(nearest, distance);
                // Everything within a couple of cells lights up, so a hit reads as landing on a
                // PLACE rather than on the barrier as an abstraction.
                if (distance < reach)
                    panel.Flash = Mathf.Max(panel.Flash, Mathf.Clamp01(1f - distance / reach));
            }

            if (nearest > _config.Height * 1.4f) return;

            Vector2 outward = new Vector2(-_config.Axis.y, _config.Axis.x);
            ArcaneWeaveFX.Chips(worldPoint, outward, count: 4, speed: 2.8f, size: 0.10f,
                tint: _palette.Rune);
            ArcaneWeaveFX.Burst(worldPoint, radius: 0.50f, seconds: 0.22f, axis: _config.Axis,
                hot: _palette.Lattice, ring: _palette.Weave);
        }

        /// <summary>
        /// Half the distance from the centre to the outermost surviving column, plus that
        /// cell's own half-width. What the collider is resized to as the weave erodes.
        /// </summary>
        public float SurvivingHalfSpan()
        {
            float span = 0f;
            for (int i = 0; i < _panels.Count; i++)
            {
                var panel = _panels[i];
                if (panel.Broken) continue;
                span = Mathf.Max(span, Mathf.Abs(panel.Along) + PanelHalfWidth * panel.Size);
            }
            return span;
        }

        /// <summary>Tear apart everything still woven, at once, plus the flash and shockwave.</summary>
        public void Shatter()
        {
            _shattered = true;
            Vector3 centre = _root.position + new Vector3(0f, _config.Height * 0.45f, 0f);

            for (int i = 0; i < _panels.Count; i++)
            {
                var panel = _panels[i];
                if (panel.Broken) continue;
                BreakPanel(panel, violent: true);
            }

            ArcaneWeaveFX.Burst(centre, radius: Mathf.Max(1.2f, _config.Length * 0.5f),
                seconds: 0.42f, axis: _config.Axis,
                hot: _palette.Lattice, ring: _palette.Rune);

            if (_motes != null) _motes.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void EnsureBreakOrder()
        {
            if (_breakOrder != null) return;

            // Outermost first: the barrier is eaten from its ends towards the middle, which is
            // both what erosion looks like and what keeps the surviving span contiguous — the
            // collider follows that span, so a hole punched in the middle would leave physics
            // blocking ground the art says is open.
            _breakOrder = new List<int>(_panels.Count);
            for (int i = 0; i < _panels.Count; i++) _breakOrder.Add(i);
            _breakOrder.Sort((a, b) =>
                Mathf.Abs(_panels[b].Along).CompareTo(Mathf.Abs(_panels[a].Along)));
        }

        private void BreakPanel(Panel panel, bool violent)
        {
            if (panel.Broken) return;
            panel.Broken = true;

            if (panel.Root == null) return;

            Vector3 at = panel.Root.position;
            // Away from the barrier's line, so the pieces scatter forwards and back rather than
            // sliding along the surface they came from.
            Vector2 outward = new Vector2(-_config.Axis.y, _config.Axis.x);
            ArcaneWeaveFX.Chips(at, outward,
                count: violent ? 5 : 3,
                speed: violent ? 4.6f : 2.4f,
                size: PanelHalfWidth * panel.Size * 0.75f,
                tint: _palette.Rune);

            if (violent)
                ArcaneWeaveFX.Burst(at, radius: PanelHalfWidth * panel.Size * 2.2f, seconds: 0.24f,
                    axis: _config.Axis, hot: _palette.Lattice, ring: _palette.Weave);

            panel.Root.gameObject.SetActive(false);
        }
    }
}
