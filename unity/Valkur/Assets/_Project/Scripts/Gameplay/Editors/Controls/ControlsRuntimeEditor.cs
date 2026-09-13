using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.Controls
{
    /// <summary>
    /// The Controls editor: a drawn keyboard and mouse where every key can be given any
    /// action, per context, and every clash is visible.
    ///
    /// <para>WHY IT EXISTS. This project had TWO binding models that did not talk to each
    /// other. The <c>.inputactions</c> asset is what gameplay reads; a wall of
    /// <c>GameSettings.*KeyA</c> strings is what the old Controls panel wrote, and only twelve
    /// editor F-keys were ever bridged between them. Every gameplay field in that file —
    /// <c>moveUpKeyA</c>, <c>dashKeyA</c>, <c>spell1KeyA</c>.., <c>primaryAttackMouse</c> —
    /// had zero readers in production, measured: a player could rebind their movement and
    /// nothing changed. Underneath that, half the gameplay verbs carried a HARDCODED legacy
    /// <see cref="UnityEngine.KeyCode"/> beside the action to survive the 2022.3 event-drop
    /// bug, so even an override that reached the asset applied only half of itself and the old
    /// key went on working. There is one model now, and this is its surface.</para>
    ///
    /// <para>WHY A PICTURE. A list answers "what is jump bound to". It cannot answer "what is
    /// free", "what did I put on F5", or "is anything doubled" — and the last is not
    /// hypothetical: the shipped asset has had two pairs of bindings sharing an ID (so a
    /// rebind of either would have moved both) and a live Tab binding built in C# that no
    /// audit over the asset could see.</para>
    ///
    /// <para>NO HOTKEY, by the same reasoning as the Camera editor: a configuration surface
    /// does not need a shortcut the player can hit by accident. It is opened from the General
    /// Editor (ESC).</para>
    /// </summary>
    public sealed partial class ControlsRuntimeEditor
        : SingletonMonoBehaviour<ControlsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        private bool _active;
        private bool _uiBuilt;

        private Canvas _canvas;
        private GameObject _root;
        private ControlsEditorUIBuilder.UIRefs _ui;
        private ControlsEditorUIBuilder.Callbacks _callbacks;

        private readonly ControlsKeyboardView _keyboard = new ControlsKeyboardView();
        private readonly ControlsMouseView _mouse = new ControlsMouseView();

        /// <summary>The context the board is CURRENTLY PAINTING. Not the live one: an author
        /// configuring the Tile editor's tools has to see them without being inside the Tile
        /// editor, and a board that changed under them the moment they opened this one would
        /// be unusable — this editor IS an editor context, so the live answer is always
        /// "editor/Controls".</summary>
        private string _viewContext = InputContexts.War;
        private KeyboardLayoutKind _layout = KeyboardLayoutKind.Iso;
        private string _selectedControl;         // keyboard control name, or ""
        private MouseControl _selectedMouse = MouseControl.None;
        private string _search = "";

        // ── Confirm dialog (its own, not the launcher's) ─────────────────────
        //
        // GeneralEditorManager.Confirm reopens the LAUNCHER when it is dismissed, which is
        // right for the entry that leaves the session and wrong here: cancelling a reset would
        // close this editor. One dialog on this editor's own canvas has no such coupling.
        private GameObject _confirmRoot;
        private TextMeshProUGUI _confirmMessage;
        private Action _pendingConfirm;

        public string EditorName => "Controls";
        public bool IsActive => _active;

        internal string ViewContext => _viewContext;
        internal bool IsConfirmOpen => _confirmRoot != null && _confirmRoot.activeSelf;

        private void Start()
        {
            _active = false;
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
        }

        protected override void OnDestroy()
        {
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            InputContextPolicy.OnChanged -= OnPolicyChanged;
            InputBindingStore.OnDirtyChanged -= OnDirtyChanged;
            ReleaseEscapeClaims();
            base.OnDestroy();
        }

        public void Activate()
        {
            if (InputService.Instance == null) InputService.Initialize();
            if (InputService.Instance == null)
            {
                Debug.LogWarning("[ControlsEditor] InputService is not available — cannot open.");
                return;
            }

            if (!_uiBuilt)
            {
                try { BuildUI(); _uiBuilt = true; }
                catch (Exception ex)
                {
                    Debug.LogError($"[ControlsEditor] BuildUI failed: {ex.GetType().Name} :: {ex.Message}");
                    Debug.LogException(ex);
                    return;
                }
            }

            _active = true;
            _root.SetActive(true);
            CancelCapture();
            CloseConfirm();

            // The caps are labelled from the OS layout, and the board may have been built
            // before a keyboard device existed (a boot race, or an EditMode fixture). Asking
            // again on every open is what makes a Spanish ISO board print n-tilde on the key
            // Unity calls "semicolon" instead of the fallback label.
            _keyboard.RefreshLegends();

            // The restored filter has to reach the BOX, not only the field behind it: a
            // remembered search applied to a visibly empty input is a short list with no
            // visible cause. Setting the text fires onValueChanged, which early-returns on an
            // unchanged value, so this cannot loop.
            if (_ui.Search != null && !string.Equals(_ui.Search.text, _search, StringComparison.Ordinal))
                _ui.Search.SetTextWithoutNotify(_search ?? "");

            ControlsEditorUIBuilder.PopulateContextStrip(_ui, _callbacks, BuildContextList());
            RebuildActionList();
            RepaintAll();
            SetStatus("Pulsa una tecla del teclado dibujado para ver que tiene encima, o «...» " +
                      "en una accion para reasignarla. Ctrl+Z deshace, Ctrl+S guarda el perfil.");
        }

        public void Deactivate()
        {
            _active = false;
            CancelCapture();
            CloseConfirm();
            if (_root != null) _root.SetActive(false);

            // NO console warning for unsaved changes. It fires on an ordinary close, and a
            // warning on a normal flow is how a console this project requires to be clean
            // stops being read. What replaced it is worth more than the log line: the GUARDAR
            // button carries a dot while anything is unsaved, and InputBindingStore.Apply now
            // refuses to overwrite live edits — before that, a scene load silently reverted
            // them, which is the failure the warning could only describe after the fact.
            VerboseLog.Log(VerboseLog.Category.Settings, () =>
                $"[ControlsEditor] Cerrado. Cambios sin guardar: {InputBindingStore.IsDirty}.");

            if (GameEditorManager.HasInstance) GameEditorManager.Instance.NotifyDeactivated(this);
        }

        private void Update()
        {
            if (!_active) return;

            if (IsConfirmOpen)
            {
                if (KeyboardInputManager.WasEscapePressedThisFrame()) CloseConfirm();
                return;
            }

            TickCapture();
            if (IsCapturing) return;

            // The shared editor verbs, read through EditorInput rather than as literals — which
            // is what keeps them moving when the player rebinds them, including from this very
            // panel. All three were declared live in every editor context and ignored here.
            if (EditorInput.SavePressed()) { Save(); return; }
            if (EditorInput.UndoPressed()) { Undo(); return; }
            if (EditorInput.RedoPressed()) { Redo(); return; }
        }

        // ── Build ────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("ControlsEditorCanvas", 114);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            _callbacks = new ControlsEditorUIBuilder.Callbacks
                {
                    OnContext        = SetViewContext,
                    OnLayoutTab      = SetLayout,
                    OnSave           = Save,
                    OnReset          = AskReset,
                    OnCancelCapture  = CancelCapture,
                    OnSearch         = OnSearchChanged,
                    OnToggleHelp     = ToggleHelp,
                };
            _ui = ControlsEditorUIBuilder.BuildAll(_root.transform, _callbacks);
            _ui.HelpOverlay = ControlsEditorUIBuilder.BuildHelpOverlay(_root.transform);

            BuildBoard();
            BuildConfirmDialog();

            InputContextPolicy.OnChanged += OnPolicyChanged;
            InputBindingStore.OnDirtyChanged += OnDirtyChanged;
            RefreshDirtyIndicator();
        }

        private void BuildBoard()
        {
            _keyboard.Destroy();
            _mouse.Destroy();

            // A margin inside the scroll mask. Without it the Escape cap and the function row
            // are flush against the clipped edge and read as cut off rather than as the corner
            // of a keyboard.
            const float PAD = 8f;
            const float DEVICE_GAP = 28f;

            var keyboardRt = _keyboard.Build(_ui.BoardHost, _layout, OnKeyClicked);
            keyboardRt.anchoredPosition = new Vector2(PAD, -PAD);
            var mouseRt = _mouse.Build(_ui.BoardHost, OnMouseClicked);
            mouseRt.anchoredPosition = new Vector2(PAD + _keyboard.Size.x + DEVICE_GAP, -PAD);

            // The scroll content is sized here and by nothing else. No ContentSizeFitter: it
            // would shrink the content to whatever the board happens to have realised and make
            // the rest unreachable, which is half of the pair ItemsTableVirtualizationTests
            // pins for the same reason.
            _ui.BoardHost.sizeDelta = new Vector2(
                PAD + _keyboard.Size.x + DEVICE_GAP + _mouse.Size.x + PAD,
                PAD + Mathf.Max(_keyboard.Size.y, _mouse.Size.y) + PAD);
        }

        private void BuildConfirmDialog()
        {
            var (root, message, ok, cancel) = UIConfirmDialog.Make(_root.transform, "Controles");
            _confirmRoot = root;
            _confirmMessage = message;
            ok.onClick.AddListener(AcceptConfirm);
            cancel.onClick.AddListener(CloseConfirm);
        }

        // ── Tabs ─────────────────────────────────────────────────────────────

        private void SetViewContext(string contextId)
        {
            if (string.Equals(_viewContext, contextId, StringComparison.Ordinal)) return;
            _viewContext = contextId;
            CancelCapture();
            RebuildActionList();
            RepaintAll();

            if (string.Equals(contextId, InputContexts.EditorsAny, StringComparison.Ordinal))
                SetStatus("Verbos COMUNES a los dieciseis editores: seleccionar, zoom, " +
                          "desplazar, deshacer, guardar, cerrar y borrar. Se comportan igual " +
                          "en todos, que es lo que los hace comunes.");
            else if (InputContexts.IsEditor(contextId))
                SetStatus($"Herramientas propias del {InputContexts.Label(contextId)}, mas los " +
                          "verbos comunes. Un editor abierto se queda con el teclado entero: " +
                          "Guerra y Paz no cuentan aqui, y por eso dos editores pueden usar " +
                          "la misma tecla para cosas distintas.");
            else if (string.Equals(contextId, InputContexts.Peace, StringComparison.Ordinal))
                SetStatus("Postura PAZ. Nada que haga dano aparece aqui: el rechazo es " +
                          "estructural, no un aviso, y por eso la lista es mas corta.");
            else
                SetStatus("Postura GUERRA. Aqui vive todo el combate.");
        }

        /// <summary>
        /// The contexts the strip offers: the two postures, the shared-editor view, then one
        /// tab per editor that owns tools of its own.
        ///
        /// <para>It used to be one tab per REGISTERED editor, which is eighteen tabs of which
        /// twelve were identical: an editor with no tools of its own shows the shared verbs
        /// and nothing else, so twelve boards were the same board. <see cref="InputContexts.EditorsAny"/>
        /// answers that question once, and the tabs that remain are the four boards that
        /// really differ. Derived from the catalog, not listed: an editor that grows its first
        /// tool gets a tab without anybody remembering to add one.</para>
        /// </summary>
        private List<string> BuildContextList()
        {
            var list = new List<string>
            {
                InputContexts.War,
                InputContexts.Peace,
                InputContexts.EditorsAny,
            };

            var owners = new List<string>();
            foreach (var d in InputActionCatalog.All)
            {
                if (string.IsNullOrEmpty(d.OwnerEditor)) continue;
                if (!owners.Contains(d.OwnerEditor)) owners.Add(d.OwnerEditor);
            }

            owners.Sort(StringComparer.Ordinal);
            foreach (var n in owners) list.Add(InputContexts.ForEditor(n));
            return list;
        }

        private void SetLayout(KeyboardLayoutKind kind)
        {
            if (_layout == kind) return;
            _layout = kind;
            _selectedControl = null;
            BuildBoard();
            _keyboard.RefreshLegends();
            RepaintAll();
        }

        private void OnSearchChanged(string text)
        {
            string next = text ?? "";
            if (string.Equals(next, _search, StringComparison.Ordinal)) return;
            _search = next;
            // A filter pass, never a rebuild: rebuilding the sixty-three rows costs 213 ms and
            // the shipped code paid it on every character typed.
            ApplyFilter();
        }

        private void OnPolicyChanged()
        {
            if (!_active) return;
            RepaintAll();
        }

        private void ToggleHelp()
        {
            if (_ui?.HelpOverlay == null) return;
            _ui.HelpOverlay.SetActive(!_ui.HelpOverlay.activeSelf);
        }

        // ── Persistence ──────────────────────────────────────────────────────

        private void Save()
        {
            if (InputBindingStore.Save())
                SetStatus($"Guardado en {InputBindingStore.FilePath}");
            else
                SetStatus("No se pudo guardar. Mira la consola.");
        }

        /// <summary>
        /// Reset is the one irreversible control on this panel — it drops every rebind, every
        /// context mask AND the file on disk — so it asks first. It shipped as a single click
        /// beside GUARDAR, which is the arrangement where an author loses an afternoon to a
        /// mis-click and has nothing to undo with.
        /// </summary>
        private void AskReset()
        {
            AskConfirm("Esto descarta TODAS tus reasignaciones y mascaras de postura, y borra " +
                       "el perfil del disco. No se puede deshacer.", ResetToDefaults);
        }

        private void ResetToDefaults()
        {
            InputBindingStore.ResetToDefaults();
            InputBindingResolver.Invalidate();
            // Reset drops the file from disk as well as the live state, so an undo could only
            // ever put back half of what it took — and a half-undo is worse than none.
            ClearHistory();
            CancelCapture();
            RebuildActionList();
            RepaintAll();
            SetStatus("Controles restaurados a los valores de fabrica.");
        }

        private void OnDirtyChanged() => RefreshDirtyIndicator();

        /// <summary>
        /// The GUARDAR button carries a dot while anything is unsaved.
        ///
        /// <para>It is the whole of the answer to "did that take". A rebind is live in the
        /// session the instant it is made and is not on disk until this button is pressed, and
        /// nothing on screen used to say which of the two states the panel was in.</para>
        /// </summary>
        private void RefreshDirtyIndicator()
        {
            if (_ui?.SaveLabel == null) return;
            bool dirty = InputBindingStore.IsDirty;
            _ui.SaveLabel.text = dirty ? "GUARDAR *" : "GUARDAR";
            _ui.SaveLabel.color = dirty ? UITheme.WARNING : UITheme.TEXT_PRIMARY;
        }

        // ── Confirm ──────────────────────────────────────────────────────────

        private void AskConfirm(string message, Action onConfirm)
        {
            if (_confirmRoot == null) { onConfirm?.Invoke(); return; }
            CancelCapture();
            _pendingConfirm = onConfirm;
            _confirmMessage.text = message;
            _confirmRoot.SetActive(true);
            _confirmRoot.transform.SetAsLastSibling();
            // Escape dismisses the dialog and must not ALSO reach the launcher behind it.
            EscapeOwnership.Claim(_confirmRoot);
        }

        private void AcceptConfirm()
        {
            var action = _pendingConfirm;
            CloseConfirm();
            action?.Invoke();
        }

        private void CloseConfirm()
        {
            _pendingConfirm = null;
            if (_confirmRoot == null) return;
            EscapeOwnership.Release(_confirmRoot);
            _confirmRoot.SetActive(false);
        }

        private void ReleaseEscapeClaims()
        {
            if (_confirmRoot != null) EscapeOwnership.Release(_confirmRoot);
            EscapeOwnership.Release(this);
        }

        internal void SetStatus(string text)
        {
            if (_ui?.Status != null) _ui.Status.text = text ?? "";
        }
    }
}
