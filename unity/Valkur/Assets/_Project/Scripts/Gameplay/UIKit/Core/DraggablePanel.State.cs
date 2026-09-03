using UnityEngine;
using Valkur.Core.Editors;

namespace Valkur.UIKit
{
    /// <summary>
    /// A panel's ability to describe itself as data and to be put back from it.
    ///
    /// This is the generic half of the editor workspace layer: thirteen of the sixteen
    /// runtime editors already build their panels with <see cref="DraggablePanel"/>, so
    /// every one of them gets its layout remembered without a line of per-editor code.
    /// Anything that cannot be captured here costs an implementation of
    /// <see cref="IProvidesWorkspaceState"/> in each editor that wants it — so prefer this
    /// side whenever the choice exists.
    /// </summary>
    public partial class DraggablePanel
    {
        // ── Where the remembered open/closed bit lives ──────────────────────────

        private static IPanelStateSink _stateSink;

        /// <summary>
        /// The backend for remembered panel visibility. Defaults to the historical
        /// PlayerPrefs behaviour, so a panel nobody manages behaves exactly as before;
        /// <c>EditorWorkspaceService</c> installs its own so a managed panel's visibility
        /// lives in the same document as its geometry.
        ///
        /// Assigning null restores the default — which is what test teardown wants.
        /// </summary>
        public static IPanelStateSink StateSink
        {
            get => _stateSink ??= new PlayerPrefsPanelStateSink();
            set => _stateSink = value;
        }

        /// <summary>
        /// Domain Reload is OFF in this project, so a sink installed by one Play session
        /// would otherwise still be here — pointing at a destroyed service — on the next.
        /// A plain field assignment is also the only shape
        /// <c>DomainReloadStaticResetTests</c> recognises: it reads the hook's raw IL and
        /// accepts <c>stsfld</c> or <c>field.Clear()</c>, nothing else.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStateSinkOnPlayModeEnter()
        {
            _stateSink = null;
        }

        // ── Identity ───────────────────────────────────────────────────────────

        /// <summary>
        /// The editor that owns this panel, used to namespace <see cref="PersistenceKey"/>.
        ///
        /// Empty by default, which resolves to the bare key — exactly the historical
        /// behaviour. <c>EditorWorkspaceService</c> stamps it from
        /// <c>IGameEditor.EditorName</c> for every panel it manages, which is what makes
        /// two editors naming a panel the same thing harmless. They do: measured
        /// 2026-09-02, Buildings (F10) and Map (F11) both build a
        /// <c>"PropertiesPanel"</c>, and with <see cref="PersistenceKey"/> assigned nowhere
        /// in the project both fell back to the GameObject name and shared one bit —
        /// closing Properties in one closed it in the other.
        /// </summary>
        public string Owner = string.Empty;

        /// <summary>
        /// Fully-qualified identity: <c>"Buildings/PropertiesPanel"</c>, or just
        /// <c>"PropertiesPanel"</c> while <see cref="Owner"/> is unset.
        /// </summary>
        public string WorkspacePanelId =>
            string.IsNullOrEmpty(Owner) ? ResolvedKey : Owner + "/" + ResolvedKey;

        // ── Capture / apply ────────────────────────────────────────────────────

        /// <summary>
        /// Snapshot this panel's geometry and visibility. Call while the panel is still
        /// alive — an editor closing does <c>SetActive(false)</c> on its root immediately
        /// after, and a disabled RectTransform still reports its rect, but a destroyed one
        /// reports nothing.
        /// </summary>
        public EditorPanelState CaptureState()
        {
            var rt = _rt != null ? _rt : GetComponent<RectTransform>();
            return new EditorPanelState
            {
                panelId          = WorkspacePanelId,
                anchoredPosition = rt != null ? rt.anchoredPosition : Vector2.zero,
                size             = rt != null ? rt.sizeDelta : Vector2.zero,
                minimized        = _minimized,
                maximized        = _maximized,
                open             = gameObject.activeSelf,
                siblingIndex     = transform.GetSiblingIndex(),
            };
        }

        /// <summary>
        /// Put the panel back. Geometry is applied only when the record actually carries
        /// some (<see cref="EditorPanelState.HasGeometry"/>) — a record holding just the
        /// open bit must not stamp a zero rect onto a freshly built panel.
        ///
        /// The caller is responsible for having rescued an off-screen rect first; see
        /// <c>EditorWorkspaceService</c>. This method does clamp, but clamping alone cannot
        /// save a panel whose remembered position is a screen and a half to the right — it
        /// would pin it to the edge at its remembered SIZE, which on a smaller display can
        /// still be unusable.
        /// </summary>
        public void ApplyState(EditorPanelState state)
        {
            if (state == null) return;

            var rt = _rt != null ? _rt : GetComponent<RectTransform>();
            if (rt == null) return;

            if (state.HasGeometry)
            {
                // Anchors must be normalized first or anchoredPosition means something
                // different from what was captured: the builders dock panels with corner
                // anchors, and NormalizeAnchor re-expresses that as a centre-anchored
                // offset. Capture always runs post-normalization (it happens a frame after
                // enable); restore has to match.
                NormalizeAnchor();

                rt.sizeDelta        = state.size;
                rt.anchoredPosition = state.anchoredPosition;
                _restoredHeight     = state.size.y;
                ClampToBounds();
            }

            // Minimize/Maximize both early-out or toggle, so drive them off the current
            // state rather than calling them blind — Maximize() in particular TOGGLES.
            if (state.minimized && !_minimized) Minimize();
            else if (!state.minimized && _minimized) RestoreFromMinimized();

            if (state.maximized != _maximized) Maximize();

            if (state.siblingIndex >= 0) transform.SetSiblingIndex(state.siblingIndex);

            if (state.open) { gameObject.SetActive(true); MarkOpened(); }
            else            { OnRestoredClosed?.Invoke(); ClosePanel(); }
        }

        /// <summary>
        /// Undo a minimize without going through <see cref="Maximize"/>, which toggles.
        /// </summary>
        private void RestoreFromMinimized()
        {
            if (!_minimized) return;
            _minimized = false;
            if (ContentRoot != null) ContentRoot.SetActive(true);
            var rt = _rt != null ? _rt : GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = new Vector2(rt.sizeDelta.x, _restoredHeight);
        }
    }

    /// <summary>
    /// The historical backend: one PlayerPrefs int per panel key. Kept as the default so a
    /// panel outside any managed editor — a HUD widget, a modal, a panel in an editor that
    /// has not adopted <see cref="IProvidesWorkspaceState"/> yet — behaves exactly as it
    /// always has.
    /// </summary>
    public sealed class PlayerPrefsPanelStateSink : IPanelStateSink
    {
        internal const string PREFS_PREFIX = "Valkur.Panel.Closed.";

        public bool IsClosed(string key)
            => !string.IsNullOrEmpty(key) && PlayerPrefs.GetInt(PREFS_PREFIX + key, 0) == 1;

        public void SetClosed(string key, bool closed)
        {
            if (string.IsNullOrEmpty(key)) return;
            PlayerPrefs.SetInt(PREFS_PREFIX + key, closed ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void Forget(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            PlayerPrefs.DeleteKey(PREFS_PREFIX + key);
            PlayerPrefs.Save();
        }
    }
}
