using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Outline FX management for the runtime Particles Editor (F1).
    ///
    /// Mirrors the BuildingsRuntimeEditor outline pattern exactly:
    ///   - Hover       → cyan  outline   (LMB over emitter)
    ///   - Selected    → yellow outline  (click to select)
    ///   - Same-preset → orange outline  (all emitters sharing the selected preset)
    ///   - Alt-toggle  → yellow outline on ALL map emitters while toggled ON
    ///
    /// Uses <see cref="ParticleEmitterOutlineRenderer"/>, which draws the emitter's real
    /// emission area — a circle for the radial kinds, a rectangle for anything emitting
    /// from a spawn box — resolved by <see cref="ParticleFootprint"/> from the preset and
    /// the instance's scale multiplier. It used to draw one fixed 0.45-unit circle for
    /// every preset, which marked the ORIGIN rather than the extent: a pollen field emits
    /// over 2.2 x 1.8 units and was marked by a circle covering a fifth of it, while a
    /// scaled portal's marker sat entirely inside the effect.
    /// </summary>
    public partial class ParticlesRuntimeEditor : SingletonMonoBehaviour<ParticlesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // ── Outline visual constants (match Python particle_editor_view colour table) ─

        private static readonly Color HOVER_CYAN            = new Color(0f,    1f,    1f,    1f);
        private static readonly Color ACTIVE_YELLOW         = new Color(1f,    215f/255f, 0f, 1f);
        private static readonly Color SAME_PRESET_ORANGE    = new Color(1f,    0.55f, 0f,   1f);
        private static readonly Color ALL_OUTLINE_YELLOW    = new Color(1f,    215f/255f, 0f, 0.75f);

        // ── Extreme envelopes (selected / hovered emitter only) ───────────────────
        //
        // The live outline answers "where is this effect NOW", which is the wrong question
        // when the author is spacing two fields apart or checking whether pollen reaches a
        // doorway: for that they need the worst case, and an outline that breathes with the
        // particle count cannot show it. These two are accumulated from the same
        // measurements and drawn alongside — the live outline is never replaced.
        //
        // Only the hover and selection outlines carry them. The same-preset and Alt passes
        // can cover dozens of emitters at once, and six lines per emitter is a thicket.

        private static readonly Color EXTREME_MAX_RED   = new Color(1f, 0.35f, 0.30f, 0.85f);
        private static readonly Color EXTREME_MIN_GREEN = new Color(0.35f, 1f, 0.55f, 0.85f);

        private const float EXTREME_THICKNESS = 0.035f;  // ~1 px @ PPU 16 — a reference mark

        private const float HOVER_THICKNESS   = 0.06f;   // ~2 px @ PPU 16
        private const float ACTIVE_THICKNESS  = 0.12f;   // ~4 px @ PPU 16
        private const float SAME_THICKNESS    = 0.09f;   // ~3 px @ PPU 16
        private const float ALL_THICKNESS     = 0.06f;


        // ── Outline FX objects (created once in EnsureOutlineFx) ──────────────────

        private ParticleEmitterOutlineRenderer _hoverFx;
        private ParticleEmitterOutlineRenderer _activeFx;

        // Pool for same-preset orange outlines (one per peer emitter).
        private readonly List<ParticleEmitterOutlineRenderer> _samePresetFxPool =
            new List<ParticleEmitterOutlineRenderer>();
        private readonly List<GameObject> _samePresetEmitters = new List<GameObject>();

        // Pool for the Alt-key "all emitters" outline pass.
        private readonly List<ParticleEmitterOutlineRenderer> _allOutlinePool =
            new List<ParticleEmitterOutlineRenderer>();

        // ── Hover state (populated by UpdateHover from MapInteraction) ─────────────

        private GameObject _hoveredInstance;

        // ── Alt-toggle state ───────────────────────────────────────────────────────

        private bool _showAllOutlines;

        // ── Setup / teardown ──────────────────────────────────────────────────────

        private void EnsureOutlineFx()
        {
            if (_hoverFx == null)
            {
                var go = new GameObject("ParticlesEditor.HoverFx");
                go.transform.SetParent(transform, false);
                _hoverFx = go.AddComponent<ParticleEmitterOutlineRenderer>();
                _hoverFx.Configure(HOVER_CYAN, HOVER_THICKNESS,
                                   drawFill: false, fillColor: Color.clear);
                _hoverFx.ConfigureExtremes(true, EXTREME_MIN_GREEN, EXTREME_MAX_RED,
                                           EXTREME_THICKNESS);
                _hoverFx.ConfigureHighlight(BOUNDS_HIGHLIGHT);
                _hoverFx.SetVisible(false);
            }

            if (_activeFx == null)
            {
                var go = new GameObject("ParticlesEditor.ActiveFx");
                go.transform.SetParent(transform, false);
                _activeFx = go.AddComponent<ParticleEmitterOutlineRenderer>();
                _activeFx.Configure(ACTIVE_YELLOW, ACTIVE_THICKNESS,
                                    drawFill: false, fillColor: Color.clear);
                _activeFx.ConfigureExtremes(true, EXTREME_MIN_GREEN, EXTREME_MAX_RED,
                                            EXTREME_THICKNESS);
                _activeFx.ConfigureHighlight(BOUNDS_HIGHLIGHT);
                _activeFx.SetVisible(false);
            }
        }

        private void HideAllOutlineFx()
        {
            if (_hoverFx  != null) { _hoverFx.Follow(null);  _hoverFx.SetVisible(false); }
            if (_activeFx != null) { _activeFx.Follow(null); _activeFx.SetVisible(false); }
            foreach (var fx in _samePresetFxPool)
                if (fx != null) { fx.Follow(null); fx.SetVisible(false); }
            foreach (var fx in _allOutlinePool)
                if (fx != null) { fx.Follow(null); fx.SetVisible(false); }
        }

        // ── Alt-toggle ────────────────────────────────────────────────────────────

        private void ToggleAllOutlines()
        {
            _showAllOutlines = !_showAllOutlines;
            SetStatus(_showAllOutlines
                ? "All-outline ON — yellow borders on every emitter."
                : "All-outline OFF.");
            if (!_showAllOutlines)
            {
                foreach (var fx in _allOutlinePool)
                    if (fx != null) { fx.Follow(null); fx.SetVisible(false); }
            }
        }

        // ── Per-frame outline update (called from Update) ─────────────────────────

        private void UpdateOutlineState()
        {
            // Poll Alt key for toggle (one-shot, not held). Routed through the
            // centralized helper so the legacy backend kicks in if the new
            // InputSystem package drops events (Unity 2022.3 Editor bug).
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(
                    UnityEngine.InputSystem.Key.LeftAlt, KeyCode.LeftAlt))
            {
                ToggleAllOutlines();
            }

            EnsureOutlineFx();

            // Hover (cyan) — skip if same as active to avoid double-drawing.
            if (_hoveredInstance != null && _hoveredInstance != _activeInstance &&
                _hoveredInstance.activeInHierarchy)
            {
                TrackWithFootprint(_hoverFx, _hoveredInstance);
                PushBoundsToOutline(_hoverFx, _hoveredInstance);
            }
            else
            {
                _hoverFx.Follow(null);
                _hoverFx.SetVisible(false);
            }

            // Active (yellow selected).
            if (_activeInstance != null && _activeInstance.activeInHierarchy)
            {
                TrackWithFootprint(_activeFx, _activeInstance);
                // After Track: a retarget clears the boxes, so they have to be pushed for the
                // emitter that is being tracked NOW.
                PushBoundsToOutline(_activeFx, _activeInstance);
            }
            else
            {
                _activeFx.Follow(null);
                _activeFx.SetVisible(false);
            }

            // Same-preset (orange).
            UpdateSamePresetOutlines();

            // All-emitters toggle (yellow, lower alpha).
            if (_showAllOutlines)
                UpdateAllOutlines();
            else
            {
                foreach (var fx in _allOutlinePool)
                    if (fx != null) { fx.Follow(null); fx.SetVisible(false); }
            }
        }

        // ── Same-preset orange outlines ────────────────────────────────────────────

        private void RebuildSamePresetFx()
        {
            _samePresetEmitters.Clear();

            if (!string.IsNullOrEmpty(_selectedPresetId))
            {
                var all = FindObjectsOfType<ParticleEmitter>();
                foreach (var em in all)
                {
                    if (em == null) continue;
                    if (em.gameObject == _activeInstance) continue;
                    string pid = GetPresetIdFromGo(em.gameObject);
                    if (string.Equals(pid, _selectedPresetId,
                            System.StringComparison.OrdinalIgnoreCase))
                        _samePresetEmitters.Add(em.gameObject);
                }
            }

            // Grow pool as needed.
            while (_samePresetFxPool.Count < _samePresetEmitters.Count)
            {
                var go = new GameObject("ParticlesEditor.SamePresetFx");
                go.transform.SetParent(transform, false);
                var fx = go.AddComponent<ParticleEmitterOutlineRenderer>();
                fx.Configure(SAME_PRESET_ORANGE, SAME_THICKNESS,
                             drawFill: false, fillColor: Color.clear);
                _samePresetFxPool.Add(fx);
            }

            for (int i = 0; i < _samePresetFxPool.Count; i++)
            {
                if (i < _samePresetEmitters.Count)
                {
                    TrackWithFootprint(_samePresetFxPool[i], _samePresetEmitters[i]);
                    _samePresetFxPool[i].SetVisible(true);
                }
                else
                {
                    _samePresetFxPool[i].Follow(null);
                    _samePresetFxPool[i].SetVisible(false);
                }
            }
        }

        private void UpdateSamePresetOutlines()
        {
            // Assign follow targets each frame so destroyed emitters auto-hide.
            for (int i = 0; i < _samePresetFxPool.Count && i < _samePresetEmitters.Count; i++)
            {
                var em = _samePresetEmitters[i];
                if (em != null && em.activeInHierarchy)
                    TrackWithFootprint(_samePresetFxPool[i], em);
                else
                {
                    _samePresetFxPool[i].Follow(null);
                    _samePresetFxPool[i].SetVisible(false);
                }
            }
        }

        // ── Footprint plumbing ─────────────────────────────────────────────────────

        /// <summary>
        /// Point one pooled outline at an emitter AND size it to that emitter's own
        /// emission area. The two always happen together: these renderers are reassigned to
        /// a different emitter every frame, so a Follow without a matching footprint frames
        /// the new target at the previous one's size.
        /// </summary>
        private static void TrackWithFootprint(ParticleEmitterOutlineRenderer fx, GameObject instance)
        {
            if (fx == null || instance == null) return;
            fx.Track(instance.transform, FootprintOf(instance));
        }

        /// <summary>
        /// Area covered by a placed emitter, measured from the particles it currently has on
        /// screen. Falls back to the preset's predicted sweep for the frame or two after
        /// placement, while nothing is alive yet.
        /// </summary>
        private static ParticleFootprint FootprintOf(GameObject instance)
        {
            var emitter = instance.GetComponentInParent<ParticleEmitter>();
            if (emitter == null) return ParticleFootprint.Default;
            return ParticleFootprint.OfLive(emitter);
        }

        // ── All-emitters yellow outlines ───────────────────────────────────────────

        private void UpdateAllOutlines()
        {
            var all = FindObjectsOfType<ParticleEmitter>();

            // Grow pool.
            while (_allOutlinePool.Count < all.Length)
            {
                var go = new GameObject("ParticlesEditor.AllFx");
                go.transform.SetParent(transform, false);
                var fx = go.AddComponent<ParticleEmitterOutlineRenderer>();
                fx.Configure(ALL_OUTLINE_YELLOW, ALL_THICKNESS,
                             drawFill: false, fillColor: Color.clear);
                _allOutlinePool.Add(fx);
            }

            // Assign.
            for (int i = 0; i < _allOutlinePool.Count; i++)
            {
                if (i < all.Length && all[i] != null && all[i].gameObject.activeInHierarchy)
                {
                    TrackWithFootprint(_allOutlinePool[i], all[i].gameObject);
                    _allOutlinePool[i].SetVisible(true);
                }
                else
                {
                    _allOutlinePool[i].Follow(null);
                    _allOutlinePool[i].SetVisible(false);
                }
            }
        }
    }
}
