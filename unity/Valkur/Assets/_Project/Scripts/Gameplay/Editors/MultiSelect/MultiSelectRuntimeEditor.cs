using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Editors;
using Valkur.Core.Input;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.MultiSelect
{
    /// <summary>
    /// The Selection tool: pick buildings, particle emitters and authored lights AT THE SAME
    /// TIME, then move, duplicate or delete them as one group. Opened from the General
    /// Editor (ESC → HERRAMIENTAS → Seleccion).
    ///
    /// <para>WHY IT IS ITS OWN EDITOR RATHER THAN A MODE IN ONE OF THE THREE.
    /// <c>GameEditorManager.OpenExclusive</c> closes the previous editor, so at no point can
    /// the Buildings and Particles editors both be live — there is literally no state in which
    /// a selection could span them. Each editor's picking also lives inside that editor and
    /// only runs while it is active, which is why this tool carries its own hit-testing
    /// (<see cref="ISelectionDomain.TryGetRect"/>) instead of delegating it. Everything that
    /// is not picking — what a placement IS, what a duplicate must carry, when a save is
    /// refused, how each anti-wipe guard reads a falling count — is delegated, through one
    /// <c>MultiSelect</c> partial per editor.</para>
    ///
    /// <para>THE UNDO IS THIS TOOL'S OWN, AND IT HAS TO BE. A group operation touches three
    /// files; recorded through each editor's own <c>ExecutePersistedEdit</c> it would be three
    /// undo entries for one gesture, and a single undo would leave two thirds of a lamppost
    /// where the author dropped it. One <c>UndoStack.LambdaCommand</c> per operation, whose do
    /// and undo bodies call the per-domain primitives and then save each touched file once.</para>
    ///
    /// <para>It carries no hotkey, like the Camera and Controls editors: the F-row was retired
    /// and the General Editor is the only door.</para>
    /// </summary>
    public sealed partial class MultiSelectRuntimeEditor
        : SingletonMonoBehaviour<MultiSelectRuntimeEditor>,
          GameEditorManager.IGameEditor,
          IProvidesWorkspaceState
    {
        /// <summary>
        /// SPANISH, to match the launcher label, and that is load-bearing rather than a taste
        /// call. <c>EditorReachabilityTests</c> matches an editor's <c>EditorName</c> against
        /// the General Editor labels by STEM, so "Selection" against a "Seleccion" button is
        /// an editor the test reports as impossible to open. The Death editor set the
        /// precedent by naming itself "Muerte" for the same reason.
        /// </summary>
        public string EditorName => "Seleccion";
        public bool   IsActive   => _active;

        private bool _active;
        private bool _uiBuilt;

        /// <summary>The three domains, in the order their chips are drawn.</summary>
        private readonly ISelectionDomain[] _domains =
        {
            new BuildingSelectionDomain(),
            new ParticleSelectionDomain(),
            new LightSelectionDomain(),
        };

        /// <summary>Which domains the author is currently allowed to pick from. A domain
        /// switched off keeps its members selected — a filter is a picking rule, not a
        /// deselection, or switching one off to reach past it would silently drop work.</summary>
        private readonly HashSet<string> _enabled = new HashSet<string>();

        private readonly MultiSelectSet _selection = new MultiSelectSet();
        private readonly UndoStack      _undo      = new UndoStack(64);

        private readonly EditorCameraPanController  _cameraPan  = new EditorCameraPanController();
        private readonly EditorCameraZoomController _cameraZoom = new EditorCameraZoomController();

        // Reused every frame so hit-testing and the outline pass allocate nothing.
        private readonly List<GameObject> _collectBuffer = new List<GameObject>(256);

        protected override void OnSingletonAwake()
        {
            for (int i = 0; i < _domains.Length; i++) _enabled.Add(_domains[i].Id);
            GameEditorManager.EnsureInstance().Register(this);
            _undo.Changed += RefreshUndoButtons;
        }

        protected override void OnDestroy()
        {
            _undo.Changed -= RefreshUndoButtons;
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        public void Activate()
        {
            if (!_uiBuilt)
            {
                try { BuildUI(); _uiBuilt = true; }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[Selection] BuildUI failed: {ex.GetType().Name} :: {ex.Message}");
                    Debug.LogException(ex);
                    return;
                }
            }

            _active = true;
            SetPanelVisible(true);
            ResetDoubleClick();

            // A selection taken before the world was swapped points at destroyed objects. Drop
            // them on open rather than on first use, so the counts on the panel are honest the
            // moment it appears.
            int pruned = _selection.Prune();
            RefreshPanel();
            // Reopening with something still on the clipboard brings its ghost back, so the
            // pending paste is visible rather than a fact the author has to remember.
            if (HasClipboard) TickGhost();
            SetStatus(pruned > 0
                ? $"{Elements(pruned)} de la seleccion anterior ya no existen."
                : "Clic para seleccionar - Ctrl+clic anade o quita - doble clic abre su editor.");
        }

        public void Deactivate()
        {
            _active = false;
            ResetDoubleClick();
            // A primed delete must not survive the editor being closed: the author would come
            // back to a red "confirm" button with no memory of having armed it.
            DisarmDelete(repaint: false);
            // The clipboard SURVIVES the close — copying, closing to look at something, and
            // reopening to paste is a normal thing to do — but its ghost does not: a
            // translucent lamppost following the pointer around the running game is a promise
            // nothing outside this editor can keep.
            HideGhost();
            // AbortGesture, never a pair of flag-clears: a drag in flight has already MOVED
            // things, and closing the editor must put them back rather than leave the world
            // changed with no undo entry behind it.
            AbortGesture();
            HideAllOutlines();
            SetPanelVisible(false);
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.NotifyDeactivated(this);
        }

        private void Update()
        {
            if (!_active) return;

            _cameraPan.Tick();
            _cameraZoom.Tick();

            // A member can be destroyed by anything at any time — a world swap, another
            // system, this tool's own delete. Pruning once per frame is what keeps every
            // reader below from having to null-check, and what stops a stale entry reaching
            // an operation's undo record.
            if (_selection.Prune() > 0) RefreshPanel();

            TickInteraction();
            DrawOutlines();
        }

        // ── The domains ────────────────────────────────────────────────────────

        private ISelectionDomain DomainById(string id)
        {
            for (int i = 0; i < _domains.Length; i++)
                if (_domains[i].Id == id) return _domains[i];
            return null;
        }

        private bool IsEnabled(ISelectionDomain d) => d != null && _enabled.Contains(d.Id);

        // ── IProvidesWorkspaceState ────────────────────────────────────────────
        //
        // The domain FILTERS are worth keeping: an author working on lighting turns the other
        // two off and expects them off tomorrow. The SELECTION is not, and deliberately —
        // reopening onto a live group of eleven things, one keystroke away from Borrar, is the
        // destructive-restore shape every other editor already refuses.

        public Transform WorkspaceRoot => _canvas != null ? _canvas.transform : null;

        public void CaptureWorkspace(EditorWorkspace workspace)
        {
            if (workspace == null) return;
            for (int i = 0; i < _domains.Length; i++)
                workspace.SetBool($"filter.{_domains[i].Id}", _enabled.Contains(_domains[i].Id));
        }

        public void RestoreWorkspace(EditorWorkspace workspace)
        {
            if (workspace == null) return;
            for (int i = 0; i < _domains.Length; i++)
            {
                string id = _domains[i].Id;
                if (workspace.GetBool($"filter.{id}", true)) _enabled.Add(id);
                else                                         _enabled.Remove(id);
            }

            // Every filter off is a tool that can select nothing and says nothing about why.
            // A restored document in that state heals itself rather than presenting a dead
            // window, the same rescue the Controls editor performs on its panels.
            if (_enabled.Count == 0)
                for (int i = 0; i < _domains.Length; i++) _enabled.Add(_domains[i].Id);

            RefreshPanel();

            // The restored geometry carries a size this panel does not own. Re-derive it, or a
            // document from before the list existed squashes every row to half its height.
            ApplyDerivedPanelHeight();
        }
    }
}
