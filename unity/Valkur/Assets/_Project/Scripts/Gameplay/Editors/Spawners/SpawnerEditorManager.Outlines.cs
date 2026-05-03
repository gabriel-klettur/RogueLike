using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Core.Input;

namespace Valkur.Gameplay.Spawners
{
    /// <summary>
    /// Alt-key toggled visualization of all <see cref="SpawnerInstance"/>s on the
    /// map. Spawners are otherwise invisible at runtime, so the Spawner Editor
    /// (F3) borrows the Particles-Editor pattern: pressing Alt flips a flag that
    /// pools <see cref="SpawnerOutlineRenderer"/> components, one per live
    /// spawner, each sized to that spawner's <c>triggerRadius</c>. While the
    /// outlines are visible the cursor world-position is hit-tested against
    /// every spawner centre so the dot under the cursor highlights as a click
    /// affordance — this works in tandem with
    /// <see cref="TryHandleCenterClickInspect"/> in the Modes partial.
    /// </summary>
    public partial class SpawnerEditorManager
        : SingletonMonoBehaviour<SpawnerEditorManager>, GameEditorManager.IGameEditor
    {
        // ── Visual constants ──────────────────────────────────────────────────────

        private static readonly Color OUTLINE_COLOR = new Color(1f, 0.65f, 0.20f, 0.85f);
        private const float OUTLINE_THICKNESS       = 0.06f;
        private const float OUTLINE_FALLBACK_RADIUS = 1f;

        // ── Pool — kept index-aligned with _outlineInstances ──────────────────────

        private readonly List<SpawnerOutlineRenderer> _outlinePool      = new List<SpawnerOutlineRenderer>();
        private readonly List<SpawnerInstance>        _outlineInstances = new List<SpawnerInstance>();

        // Reusable buffers — avoid per-frame GC during UpdateAllOutlines.
        private readonly List<Vector2> _hoverProbePositions = new List<Vector2>();

        // ── State ─────────────────────────────────────────────────────────────────

        private bool _showAllOutlines;
        // Index into _outlineInstances of the spawner currently under the cursor,
        // or -1 if none. Exposed internally for tests via reflection.
        private int  _hoveredOutlineIndex = -1;

        // ── Toggle ────────────────────────────────────────────────────────────────

        private void ToggleAllOutlines()
        {
            _showAllOutlines = !_showAllOutlines;
            SetStatus(_showAllOutlines
                ? "Spawner outlines ON — hover the centre dot and click to inspect. Alt to hide."
                : "Spawner outlines OFF.");
            if (!_showAllOutlines) HideAllOutlineFx();
        }

        private void HideAllOutlineFx()
        {
            foreach (var fx in _outlinePool)
                if (fx != null)
                {
                    fx.Follow(null);
                    fx.SetHovered(false);
                    fx.SetVisible(false);
                }
            _outlineInstances.Clear();
            _hoveredOutlineIndex = -1;
        }

        // ── Per-frame update (called from Update while editor is active) ──────────

        private void UpdateOutlineState()
        {
            // Poll Alt key — one-shot toggle, not held. Routed through the
            // centralized helper so the legacy backend kicks in if the new
            // InputSystem package drops events (Unity 2022.3 Editor bug).
            if (KeyboardInputManager.WasKeyPressedThisFrame(Key.LeftAlt, KeyCode.LeftAlt))
            {
                ToggleAllOutlines();
            }

            if (_showAllOutlines) UpdateAllOutlines();
        }

        private void UpdateAllOutlines()
        {
            var all = FindObjectsOfType<SpawnerInstance>();

            // Grow pool as needed.
            while (_outlinePool.Count < all.Length)
            {
                var go = new GameObject("SpawnerEditor.OutlineFx");
                go.transform.SetParent(transform, false);
                var fx = go.AddComponent<SpawnerOutlineRenderer>();
                fx.Configure(OUTLINE_COLOR, OUTLINE_THICKNESS, OUTLINE_FALLBACK_RADIUS);
                _outlinePool.Add(fx);
            }

            // Refresh the index-aligned instance list.
            _outlineInstances.Clear();
            for (int i = 0; i < all.Length; i++) _outlineInstances.Add(all[i]);

            // Assign each pool entry to a spawner (or hide if surplus). Hover is
            // recomputed in a separate pass so we have the full position set first.
            for (int i = 0; i < _outlinePool.Count; i++)
            {
                var fx = _outlinePool[i];
                if (fx == null) continue;

                if (i < all.Length && all[i] != null && all[i].gameObject.activeInHierarchy)
                {
                    var si       = all[i];
                    var template = si.Template;
                    float radius = (template != null && template.triggerRadius > 0f)
                        ? template.triggerRadius
                        : OUTLINE_FALLBACK_RADIUS;
                    fx.SetRadius(radius);
                    fx.Follow(si.transform);
                    fx.SetHovered(false); // reset; the hover pass below sets the right one
                    fx.SetVisible(true);
                }
                else
                {
                    fx.Follow(null);
                    fx.SetHovered(false);
                    fx.SetVisible(false);
                }
            }

            UpdateCenterHover();
        }

        /// <summary>
        /// Hit-tests the cursor world position against every visible spawner
        /// centre and tells exactly one renderer to paint its hover affordance.
        /// </summary>
        private void UpdateCenterHover()
        {
            _hoveredOutlineIndex = -1;
            if (_outlineInstances.Count == 0) return;

            // Skip the hover when the pointer is over a panel — otherwise hovering
            // a UI element that overlaps a centre dot would still light it up.
            if (IsPointerOverEditorUI()) return;

            var cam = _camera != null ? _camera : Camera.main;
            if (cam == null) return;

            Vector2 screen = MouseInputManager.GetScreenMousePosition();
            Vector3 world  = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));

            _hoverProbePositions.Clear();
            for (int i = 0; i < _outlineInstances.Count; i++)
            {
                var si = _outlineInstances[i];
                _hoverProbePositions.Add(si != null
                    ? (Vector2)si.transform.position
                    : Vector2.positiveInfinity);
            }

            _hoveredOutlineIndex = SpawnerHitTester.FindClosestWithinRadius(
                _hoverProbePositions, world, CENTER_HIT_RADIUS_WORLD);

            if (_hoveredOutlineIndex >= 0 && _hoveredOutlineIndex < _outlinePool.Count)
            {
                var fx = _outlinePool[_hoveredOutlineIndex];
                if (fx != null) fx.SetHovered(true);
            }
        }
    }
}
