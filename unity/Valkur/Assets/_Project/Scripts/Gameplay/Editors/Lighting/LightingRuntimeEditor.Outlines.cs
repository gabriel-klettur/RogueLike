using System.Collections.Generic;
using UnityEngine;
using Valkur.Core.Input;
using Valkur.UIKit;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Alt-toggled visualization of every placed light, the same gesture and the same shape the
    /// Spawner Editor uses for its spawner instances: one pooled
    /// <see cref="LightOutlineRenderer"/> per live light, its ring sized to that light's own
    /// <c>pointLightOuterRadius</c>, plus a centre dot that lights up under the cursor as a
    /// click affordance.
    ///
    /// A spawner is invisible at runtime, so its overlay answers "is anything here at all". A
    /// light is not invisible — but WHERE it is and HOW FAR IT REACHES are two different
    /// questions, and the lit pool answers neither: the source is a bright blob with no edge, and
    /// in daylight, or off screen, the light's own GameObject is deactivated outright. So the
    /// markers are drawn from <see cref="WorldLightLoader.CollectActiveLights"/>, which reports
    /// deactivated lights too, and <see cref="LightOutlineRenderer"/> never consults
    /// <c>activeInHierarchy</c>. An overlay that went blank at noon would be missing at the one
    /// hour the author most needs it.
    ///
    /// Hover comes from <c>_hoveredLight</c>, which HandleMapInteraction has already resolved
    /// this frame — a second hit-test here would be a second answer to "what is under the
    /// cursor", and the two would eventually disagree about which light a click selects.
    /// </summary>
    public partial class LightingRuntimeEditor
    {
        // ── Visual constants ─────────────────────────────────────────────────

        /// <summary>An authored light — one this editor can move, delete and save.</summary>
        private static readonly Color OUTLINE_AUTHORED = UITheme.MARKER_RING;

        /// <summary>
        /// A light DERIVED from a light-fixture building. It is drawn, because an author
        /// wondering why a corner is bright needs to see it, and drawn differently, because
        /// every edit gesture on it is refused — a marker identical to the authored one would
        /// promise an edit that cannot happen.
        /// </summary>
        private static readonly Color OUTLINE_DERIVED  = UITheme.MARKER_RING_LOCKED;

        private const float OUTLINE_THICKNESS       = 0.06f;
        private const float OUTLINE_FALLBACK_RADIUS = 1f;

        // ── Pool — kept index-aligned with _outlineHandles ────────────────────

        private readonly List<LightOutlineRenderer>          _outlinePool    = new List<LightOutlineRenderer>();
        private readonly List<WorldLightLoader.LightHandle>  _outlineHandles = new List<WorldLightLoader.LightHandle>();

        // ── State ────────────────────────────────────────────────────────────

        private bool _showAllOutlines;

        /// <summary>Are the light markers currently on? Read by tests via reflection.</summary>
        public bool OutlinesVisible => _showAllOutlines;

        // ── Toggle ───────────────────────────────────────────────────────────

        private void ToggleAllOutlines()
        {
            _showAllOutlines = !_showAllOutlines;
            SetStatus(_showAllOutlines
                ? "Light outlines ON — each ring is that light's reach. Alt to hide."
                : "Light outlines OFF.");
            if (!_showAllOutlines) HideAllOutlineFx();
        }

        private void HideAllOutlineFx()
        {
            for (int i = 0; i < _outlinePool.Count; i++)
            {
                var fx = _outlinePool[i];
                if (fx == null) continue;
                fx.Follow(null);
                fx.SetHovered(false);
                fx.SetVisible(false);
            }
            _outlineHandles.Clear();
        }

        // ── Per-frame update (called from Update while the editor is active) ──

        private void UpdateOutlineState()
        {
            // One-shot toggle, not held. Routed through the centralized shared-verb helper so
            // the legacy backend still answers when the InputSystem package drops events.
            if (EditorInput.ToggleOutlinesPressed()) ToggleAllOutlines();

            if (_showAllOutlines) UpdateAllOutlines();
        }

        private void UpdateAllOutlines()
        {
            var loader = WorldLightLoader.Instance;
            if (loader == null) { HideAllOutlineFx(); return; }

            loader.CollectActiveLights(_outlineHandles);

            // Grow the pool as needed. It is never shrunk — surplus entries are simply hidden,
            // so toggling the overlay on a big map does not churn GameObjects.
            while (_outlinePool.Count < _outlineHandles.Count)
            {
                var go = new GameObject("LightingEditor.OutlineFx");
                go.transform.SetParent(transform, false);
                var created = go.AddComponent<LightOutlineRenderer>();
                created.Configure(OUTLINE_AUTHORED, OUTLINE_THICKNESS, OUTLINE_FALLBACK_RADIUS);
                _outlinePool.Add(created);
            }

            for (int i = 0; i < _outlinePool.Count; i++)
            {
                var fx = _outlinePool[i];
                if (fx == null) continue;

                if (i >= _outlineHandles.Count || _outlineHandles[i].Go == null)
                {
                    fx.Follow(null);
                    fx.SetHovered(false);
                    fx.SetVisible(false);
                    continue;
                }

                var handle = _outlineHandles[i];
                fx.SetRadius(handle.OuterRadius > 0f ? handle.OuterRadius : OUTLINE_FALLBACK_RADIUS);
                fx.SetColor(handle.Persistent ? OUTLINE_AUTHORED : OUTLINE_DERIVED);
                fx.Follow(handle.Go.transform);
                // _hoveredLight was resolved this frame by HandleMapInteraction, and is already
                // null while the cursor is over a panel.
                fx.SetHovered(_hoveredLight != null && ReferenceEquals(handle.Go, _hoveredLight));
                fx.SetVisible(true);
            }
        }
    }
}
