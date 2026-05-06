using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Infrastructure;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.Editors.Boss
{
    /// <summary>
    /// Timeline visual for the Boss Editor.
    ///
    /// Manages the <see cref="BossEditorTimelineRenderer"/> MonoBehaviour that
    /// lives on the timeline strip GameObject at the bottom of the Cue Inspector
    /// panel. The renderer does the actual pixel work; this partial wires it to
    /// the editor's selection state and exposes <see cref="BuildTimelineStrip"/>
    /// so <see cref="BossEditorManager.UI"/> can call it from
    /// <see cref="RefreshCuesPanel"/>.
    /// </summary>
    public partial class BossEditorManager
        : SingletonMonoBehaviour<BossEditorManager>, GameEditorManager.IGameEditor
    {
        // ── State ──────────────────────────────────────────────────────────────

        private BossEditorTimelineRenderer _timelineRenderer;

        // ── Called from HandleKeyboardShortcuts / Update ───────────────────────

        // Delegated from Update() so the authoring-modes tick can share the
        // same "editor is active" guard.
        private void TickEditorExtensions()
        {
            TickAuthoringModes();   // defined in AuthoringModes partial
        }

        // ── Build ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Injects a timeline strip RawImage at the bottom of
        /// <paramref name="parent"/> (the CuesContent rect). A new
        /// <see cref="BossEditorTimelineRenderer"/> MonoBehaviour is added to the
        /// strip's GameObject; if one already exists it is reused.
        /// </summary>
        internal void BuildTimelineStrip(RectTransform parent)
        {
            var strip = EditorUIHelpers.CreateUI("TimelineStrip", parent);
            strip.AddComponent<LayoutElement>().preferredHeight = 28f;

            // RawImage fills the strip.
            strip.AddComponent<RawImage>().color = Color.white;

            // The renderer needs to be a MonoBehaviour — attach to same GO.
            // Note: ApplyTimelineChart() is called by the caller (RefreshCuesPanel) after
            // BuildTimelineStrip returns, once the renderer is fully initialised.
            _timelineRenderer = strip.AddComponent<BossEditorTimelineRenderer>();
        }

        /// <summary>
        /// Tells the renderer which chart and meter to use.  Called whenever
        /// the selection changes or the chart is modified.
        /// </summary>
        private void ApplyTimelineChart()
        {
            if (_timelineRenderer == null) return;

            var clock = MusicBeatClock.Instance;
            int beatsPerBar = (clock != null) ? Mathf.Max(1, clock.BeatsPerBar) : 4;

            // Fall back to audio catalog metadata if the clock isn't live yet.
            if ((clock == null || !clock.IsActive) && _selectedChart != null)
            {
                var catalog = GetAudioCatalog();
                MusicTrackEntry t = catalog?.GetTrack(_selectedChart.musicTrackId);
                if (t != null && t.beatsPerBar > 0) beatsPerBar = t.beatsPerBar;
            }

            _timelineRenderer.SetChart(_selectedChart, beatsPerBar);
        }
    }
}
