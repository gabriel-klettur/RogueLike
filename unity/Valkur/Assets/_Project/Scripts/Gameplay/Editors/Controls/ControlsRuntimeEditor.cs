using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.Controls
{
    /// <summary>
    /// The Controls editor: a drawn keyboard and mouse where every key can be given any
    /// action, per stance, and every conflict is visible.
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
    /// hypothetical: the shipped asset has four same-map F-key collisions, had two pairs of
    /// bindings sharing an ID (so a rebind of either would have moved both), and had a live
    /// Tab binding built in C# that no audit over the asset could see.</para>
    ///
    /// <para>NO HOTKEY, by the same reasoning as the Camera editor: all thirteen function keys
    /// are taken, and a configuration surface does not need a shortcut the player can hit by
    /// accident. It is opened from the General Editor (ESC).</para>
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
        private bool _dirty;

        public string EditorName => "Controls";
        public bool IsActive => _active;

        internal string ViewContext => _viewContext;

        private void Start()
        {
            _active = false;
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
        }

        protected override void OnDestroy()
        {
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            InputContextPolicy.OnChanged -= OnPolicyChanged;
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
            ControlsEditorUIBuilder.PopulateContextStrip(_ui, _callbacks, BuildContextList());
            RebuildActionList();
            RepaintAll();
            SetStatus("Pulsa una tecla del teclado dibujado para ver que tiene encima, o una " +
                      "accion de la lista para reasignarla. GUARDAR escribe el perfil.");
        }

        public void Deactivate()
        {
            _active = false;
            CancelCapture();
            if (_root != null) _root.SetActive(false);

            // An unsaved rebind is live in the session but not on disk. Saying so is the only
            // thing standing between the author and losing it to a Play-mode restart, and it
            // is deliberately a warning rather than a modal: the editor closes when the author
            // asked it to.
            if (_dirty)
                Debug.LogWarning("[ControlsEditor] Cerrado con cambios sin guardar. Siguen " +
                                 "activos en esta sesion, pero no estan en disco.");

            if (GameEditorManager.HasInstance) GameEditorManager.Instance.NotifyDeactivated(this);
        }

        private void Update()
        {
            if (!_active) return;
            TickCapture();
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
                    OnReset          = ResetToDefaults,
                    OnCancelCapture  = CancelCapture,
                    OnSearch         = OnSearchChanged,
                };
            _ui = ControlsEditorUIBuilder.BuildAll(_root.transform, _callbacks);

            BuildBoard();
            InputContextPolicy.OnChanged += OnPolicyChanged;
        }

        private void BuildBoard()
        {
            _keyboard.Destroy();
            _mouse.Destroy();

            _keyboard.Build(_ui.BoardHost, _layout, OnKeyClicked);
            var mouseRt = _mouse.Build(_ui.BoardHost, OnMouseClicked);
            mouseRt.anchoredPosition = new Vector2(_keyboard.Size.x + 28f, 0f);

            // The scroll content is sized here and by nothing else. No ContentSizeFitter: it
            // would shrink the content to whatever the board happens to have realised and make
            // the rest unreachable, which is half of the pair ItemsTableVirtualizationTests
            // pins for the same reason.
            _ui.BoardHost.sizeDelta = new Vector2(
                _keyboard.Size.x + 28f + _mouse.Size.x + 12f,
                Mathf.Max(_keyboard.Size.y, _mouse.Size.y) + 12f);
        }

        // ── Tabs ─────────────────────────────────────────────────────────────

        private void SetViewContext(string contextId)
        {
            if (string.Equals(_viewContext, contextId, StringComparison.Ordinal)) return;
            _viewContext = contextId;
            RebuildActionList();
            RepaintAll();

            if (InputContexts.IsEditor(contextId))
                SetStatus($"Editor {InputContexts.Label(contextId)}. Mientras este editor esta " +
                          "abierto se queda con el teclado y el raton enteros: Guerra y Paz no " +
                          "cuentan aqui. Los verbos comunes (seleccionar, zoom, desplazar, " +
                          "deshacer, guardar, cerrar) son los mismos en los dieciseis.");
            else if (string.Equals(contextId, InputContexts.Peace, StringComparison.Ordinal))
                SetStatus("Postura PAZ. Nada que haga dano puede asignarse aqui: la lista solo " +
                          "ofrece verbos seguros, y el rechazo es estructural, no un aviso.");
            else
                SetStatus("Postura GUERRA. Aqui vive todo el combate.");
        }

        /// <summary>
        /// The contexts the strip offers: the two postures, then one per REGISTERED editor.
        /// Read from the live registry rather than a literal list, so an editor added tomorrow
        /// gets a tab without anybody remembering to add one.
        /// </summary>
        private List<string> BuildContextList()
        {
            var list = new List<string> { InputContexts.War, InputContexts.Peace };

            var names = new List<string>();
            if (GameEditorManager.HasInstance)
                foreach (var editor in GameEditorManager.Instance.RegisteredEditors)
                    if (editor != null && !string.IsNullOrEmpty(editor.EditorName))
                        names.Add(editor.EditorName);

            names.Sort(StringComparer.Ordinal);
            foreach (var n in names) list.Add(InputContexts.ForEditor(n));
            return list;
        }

        private void SetLayout(KeyboardLayoutKind kind)
        {
            if (_layout == kind) return;
            _layout = kind;
            _selectedControl = null;
            BuildBoard();
            RepaintAll();
        }

        private void OnSearchChanged(string text)
        {
            string next = text ?? "";
            if (string.Equals(next, _search, StringComparison.Ordinal)) return;
            _search = next;
            RebuildActionList();
        }

        private void OnPolicyChanged()
        {
            if (!_active) return;
            RepaintAll();
        }

        // ── Persistence ──────────────────────────────────────────────────────

        private void Save()
        {
            if (InputBindingStore.Save())
            {
                _dirty = false;
                SetStatus($"Guardado en {InputBindingStore.FilePath}");
            }
            else
            {
                SetStatus("No se pudo guardar. Mira la consola.");
            }
        }

        private void ResetToDefaults()
        {
            InputBindingStore.ResetToDefaults();
            InputBindingResolver.Invalidate();
            _dirty = false;
            RebuildActionList();
            RepaintAll();
            SetStatus("Controles restaurados a los valores de fabrica.");
        }

        internal void SetStatus(string text)
        {
            if (_ui?.Status != null) _ui.Status.text = text ?? "";
        }
    }
}
