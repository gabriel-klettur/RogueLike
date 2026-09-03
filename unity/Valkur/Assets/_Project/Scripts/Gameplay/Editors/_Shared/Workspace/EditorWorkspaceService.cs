using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Editors;
using Valkur.Infrastructure.Persistence.EditorWorkspaces;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.Workspace
{
    /// <summary>
    /// The single owner of "put the editor back the way the author left it".
    ///
    /// Reached from <see cref="GameEditorManager"/> — the one seam every editor open and
    /// close already passes through. That is the whole point: one hook, not sixteen. An
    /// editor never calls this service directly, and adding a second call site is how the
    /// layer would start disagreeing with itself.
    ///
    /// It is also the <see cref="IPanelStateSink"/> for every panel it manages, so a
    /// managed panel's open/closed bit lives in the same document as its geometry rather
    /// than in a parallel PlayerPrefs entry. Panels it does not manage keep answering from
    /// the historical PlayerPrefs backend, unchanged.
    /// </summary>
    public sealed class EditorWorkspaceService : MonoBehaviour, IEditorWorkspaceService, IPanelStateSink
    {
        /// <summary>
        /// Restoration is deferred by this many frames. One is enough and one is needed:
        /// editors build their UI lazily on the first Activate, and every
        /// <see cref="DraggablePanel"/> normalizes its anchors one frame after enable
        /// (<c>NormalizeNextFrame</c>). Applying geometry before either has happened writes
        /// onto a rect that is about to be overwritten.
        /// </summary>
        private const int RESTORE_DELAY_FRAMES = 1;

        private IEditorWorkspaceStore _store;

        /// <summary>Workspaces in play this session, keyed by editor name.</summary>
        private readonly Dictionary<string, EditorWorkspace> _loaded =
            new Dictionary<string, EditorWorkspace>();

        /// <summary>
        /// Maps a panel id to the editor that owns it, so the sink can answer a visibility
        /// question without knowing which editor is asking. Built as panels are discovered.
        /// </summary>
        private readonly Dictionary<string, string> _panelOwner =
            new Dictionary<string, string>();

        private readonly IPanelStateSink _fallbackSink = new PlayerPrefsPanelStateSink();

        private readonly List<DraggablePanel> _panelBuffer = new List<DraggablePanel>();

        private Coroutine _pendingRestore;

        // ── Lifecycle ───────────────────────────────────────────────────────────

        /// <summary>
        /// Creates the service if it is missing and registers it. Call from bootstrap;
        /// idempotent, so a second call is free.
        /// </summary>
        public static EditorWorkspaceService EnsureInstance()
        {
            var existing = ServiceLocator.Get<IEditorWorkspaceService>() as EditorWorkspaceService;
            if (existing != null) return existing;

            var go = new GameObject("[EditorWorkspaceService]");
            return go.AddComponent<EditorWorkspaceService>();
        }

        private void Awake()
        {
            _store ??= new JsonEditorWorkspaceStore();
            ServiceLocator.Register<IEditorWorkspaceService>(this);
            DraggablePanel.StateSink = this;
        }

        private void OnDestroy()
        {
            if (ServiceLocator.Get<IEditorWorkspaceService>() == (IEditorWorkspaceService)this)
                ServiceLocator.Unregister<IEditorWorkspaceService>();

            // Hand the sink back rather than leaving a destroyed MonoBehaviour installed:
            // Domain Reload is OFF, so a stale static reference outlives the object it
            // points at and every panel built next session would ask a corpse.
            if (ReferenceEquals(DraggablePanel.StateSink, this))
                DraggablePanel.StateSink = null;
        }

        /// <summary>Swap the backing store. For tests, which must not touch the real folder.</summary>
        public void UseStore(IEditorWorkspaceStore store)
        {
            _store = store;
            _loaded.Clear();
            _panelOwner.Clear();
        }

        // ── IEditorWorkspaceService ─────────────────────────────────────────────

        public void RestoreOnOpen(GameEditorManager.IGameEditor editor)
        {
            if (editor == null) return;

            // Load eagerly even though applying is deferred: the sink is asked for panel
            // visibility during the panel's own normalize coroutine, which runs in that
            // same deferred window. Loading late would answer "open" for a panel the author
            // had closed, and it would then be recorded as open.
            GetOrLoad(editor.EditorName);

            if (_pendingRestore != null) StopCoroutine(_pendingRestore);
            if (isActiveAndEnabled) _pendingRestore = StartCoroutine(RestoreDeferred(editor));
        }

        private IEnumerator RestoreDeferred(GameEditorManager.IGameEditor editor)
        {
            for (int i = 0; i < RESTORE_DELAY_FRAMES; i++) yield return null;
            _pendingRestore = null;
            ApplyNow(editor);
        }

        /// <summary>
        /// The restore body, callable without waiting a frame. Production goes through
        /// <see cref="RestoreOnOpen"/>; tests call this so they need no coroutine runner.
        /// </summary>
        public void ApplyNow(GameEditorManager.IGameEditor editor)
        {
            if (editor == null) return;
            var ws = GetOrLoad(editor.EditorName);
            if (ws == null) return;

            var root = RootOf(editor);
            if (root != null)
            {
                CollectPanels(root, editor.EditorName);
                var canvasSize = CanvasSizeOf(root);

                foreach (var panel in _panelBuffer)
                {
                    var state = ws.FindPanel(panel.WorkspacePanelId);
                    if (state == null) continue;
                    panel.ApplyState(RescueOffScreen(state, ws.capturedCanvasSize, canvasSize));
                }
            }

            // Editor state last: an editor's own Restore may want to act on panels that
            // are, by now, back where they belong.
            (editor as IProvidesWorkspaceState)?.RestoreWorkspace(ws);
        }

        public void CaptureOnClose(GameEditorManager.IGameEditor editor)
        {
            if (editor == null) return;

            if (_pendingRestore != null)
            {
                // Closed inside the deferred window — the restore never ran, so capturing
                // now would overwrite the stored layout with whatever the builders happened
                // to dock. Abandon both.
                StopCoroutine(_pendingRestore);
                _pendingRestore = null;
                return;
            }

            var ws = GetOrLoad(editor.EditorName) ?? NewWorkspace(editor.EditorName);

            var root = RootOf(editor);
            if (root != null)
            {
                CollectPanels(root, editor.EditorName);
                ws.capturedCanvasSize = CanvasSizeOf(root);
                foreach (var panel in _panelBuffer) ws.UpsertPanel(panel.CaptureState());
            }

            (editor as IProvidesWorkspaceState)?.CaptureWorkspace(ws);

            _loaded[editor.EditorName] = ws;
            _store?.Save(ws);
        }

        public void ResetWorkspace(GameEditorManager.IGameEditor editor)
        {
            if (editor == null) return;
            _loaded.Remove(editor.EditorName);
            _store?.Delete(editor.EditorName);

            var root = RootOf(editor);
            if (root == null) return;
            CollectPanels(root, editor.EditorName);
            foreach (var panel in _panelBuffer) _panelOwner.Remove(panel.WorkspacePanelId);
        }

        // ── IPanelStateSink ─────────────────────────────────────────────────────
        //
        // A panel this service manages answers from its editor's document; anything else
        // keeps answering from PlayerPrefs exactly as before. That split is what lets the
        // layer ship without touching the sixteen editors: an editor that has not adopted
        // IProvidesWorkspaceState is simply never managed, and nothing about it changes.

        public bool IsClosed(string key)
        {
            if (TryResolve(key, out var ws))
            {
                var state = ws.FindPanel(key);
                return state != null && !state.open;
            }
            return _fallbackSink.IsClosed(key);
        }

        public void SetClosed(string key, bool closed)
        {
            if (TryResolve(key, out var ws))
            {
                var state = ws.FindPanel(key);
                if (state == null)
                {
                    state = new EditorPanelState { panelId = key };
                    ws.UpsertPanel(state);
                }
                state.open = !closed;
                return;
            }
            _fallbackSink.SetClosed(key, closed);
        }

        public void Forget(string key)
        {
            if (TryResolve(key, out var ws))
            {
                var state = ws.FindPanel(key);
                if (state != null) ws.panels.Remove(state);
                return;
            }
            _fallbackSink.Forget(key);
        }

        private bool TryResolve(string panelId, out EditorWorkspace workspace)
        {
            workspace = null;
            if (string.IsNullOrEmpty(panelId)) return false;
            return _panelOwner.TryGetValue(panelId, out var owner)
                   && _loaded.TryGetValue(owner, out workspace)
                   && workspace != null;
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private EditorWorkspace GetOrLoad(string editorName)
        {
            if (string.IsNullOrEmpty(editorName)) return null;
            if (_loaded.TryGetValue(editorName, out var cached)) return cached;

            var ws = _store?.Load(editorName) ?? NewWorkspace(editorName);
            ws.editorName = editorName;
            _loaded[editorName] = ws;

            // Re-key any panel already known for this editor, so a workspace loaded after
            // its panels were discovered still answers for them.
            foreach (var state in ws.panels)
                if (state != null && !string.IsNullOrEmpty(state.panelId))
                    _panelOwner[state.panelId] = editorName;

            return ws;
        }

        private static EditorWorkspace NewWorkspace(string editorName)
            => new EditorWorkspace { editorName = editorName };

        private static Transform RootOf(GameEditorManager.IGameEditor editor)
            => (editor as IProvidesWorkspaceState)?.WorkspaceRoot;

        /// <summary>
        /// Fills <see cref="_panelBuffer"/> with the editor's panels and stamps each with
        /// its owner, which is what namespaces the persistence key. Includes inactive
        /// panels: a panel the author closed is inactive and is exactly the one whose state
        /// must survive.
        /// </summary>
        private void CollectPanels(Transform root, string editorName)
        {
            _panelBuffer.Clear();
            root.GetComponentsInChildren(includeInactive: true, _panelBuffer);

            foreach (var panel in _panelBuffer)
            {
                panel.Owner = editorName;
                _panelOwner[panel.WorkspacePanelId] = editorName;
            }
        }

        private static Vector2 CanvasSizeOf(Transform root)
        {
            var canvas = root.GetComponentInParent<Canvas>();
            if (canvas == null) return Vector2.zero;
            var rt = canvas.GetComponent<RectTransform>();
            return rt == null ? Vector2.zero : new Vector2(rt.rect.width, rt.rect.height);
        }

        /// <summary>
        /// A layout captured on a bigger canvas leaves panels unreachable on a smaller one —
        /// and clamping alone does not save them, because a panel pinned to the edge at its
        /// remembered SIZE can still be wider than the display. Anything that would not fit,
        /// or would land outside the live canvas, gives up its geometry and takes the dock
        /// the builder gave it.
        ///
        /// Without this the persistence is a one-way trap whose only escape is deleting a
        /// file the author does not know exists.
        /// </summary>
        public static EditorPanelState RescueOffScreen(
            EditorPanelState state, Vector2 capturedCanvas, Vector2 liveCanvas)
        {
            if (state == null || !state.HasGeometry) return state;
            if (liveCanvas.x <= 0f || liveCanvas.y <= 0f) return state;

            bool tooBig = state.size.x > liveCanvas.x || state.size.y > liveCanvas.y;

            // Anchors are normalized to the canvas CENTRE by the time geometry is captured,
            // so the reachable band for a panel's anchored position is half the canvas
            // either way, shrunk by the panel's own extent.
            float halfW = liveCanvas.x * 0.5f;
            float halfH = liveCanvas.y * 0.5f;
            bool outside = Mathf.Abs(state.anchoredPosition.x) > halfW
                        || Mathf.Abs(state.anchoredPosition.y) > halfH;

            if (!tooBig && !outside) return state;

            // Keep the bits that carry no risk — whether the author had it closed or
            // collapsed is still true at any resolution.
            return new EditorPanelState
            {
                panelId      = state.panelId,
                minimized    = state.minimized,
                maximized    = false,
                open         = state.open,
                siblingIndex = state.siblingIndex,
                // size left at zero, so ApplyState skips geometry entirely.
            };
        }
    }
}
