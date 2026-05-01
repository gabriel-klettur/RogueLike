using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Runtime in-game Spells Editor (F4) — PHASE 1: UI/UX scaffolding only.
    ///
    /// Visual chrome mirrors the Items Editor (F7) and Buildings Editor (F10):
    ///   • 30 px menu bar at top:
    ///       brand "SPELLS EDITOR" + Modes / Spells / Properties / Tutorial dropdowns + ? + PERF.
    ///   • Floating, draggable panels for each section.
    ///
    /// Content mirrors the Python spells_editor (F4) panel set:
    ///   • Picker grid     → Spells panel (search + 4-col grid)
    ///   • Properties      → Properties panel (TabStrip [Properties | Assets/Particles])
    ///   • Add/Remove/Save → Modes panel (action buttons + Undo/Redo/Reload)
    ///   • Tutorial        → 6-step Tutorial panel
    ///
    /// PHASE 1 scope: chrome + dropdown management + tutorial. All mutate callbacks
    /// (Add/Remove/Save/Reload/property edits) are stubs that emit a status toast —
    /// Phase 2 will wire them to the data layer.
    /// </summary>
    public partial class SpellsRuntimeEditor : SingletonMonoBehaviour<SpellsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        [SerializeField, Tooltip("Spell catalog asset")]
        private SpellCatalog _catalog;

        [SerializeField, Tooltip("Optional particle preset catalog — used to derive a tint colour for spells with no sprite.")]
        private ParticlePresetCatalog _particleCatalog;

        // ── State ──
        private bool _active;
        private bool _uiBuilt;
        private InputAction _toggleAction;
        private bool _ownsToggleAction;

        // UI
        private Canvas _canvas;
        private GameObject _root;
        private SpellsEditorUIBuilder.UIRefs _uiRefs;

        // Selection / filter
        private string _selectedKey;
        private string _searchFilter = "";

        // Dropdown open/close — mirrors ItemsRuntimeEditor / BuildingsRuntimeEditor
        private readonly HashSet<string> _openDropdowns = new HashSet<string>();

        // Undo
        private readonly UndoStack _undo = new UndoStack(64);

        // Middle-mouse camera pan — shared controller used by every runtime editor.
        private readonly EditorCameraPanController _cameraPan = new EditorCameraPanController();

        // Tutorial state (6-step guided walkthrough)
        private int _tutorialStep;
        private static readonly (string title, string body)[] TUTORIAL_STEPS =
        {
            ("1. Bienvenido",
             "Este tutorial te guiará por las funciones clave: abrir el picker, seleccionar un hechizo, duplicar/eliminar con Add/Remove y panel de propiedades."),
            ("2. Mostrar el Picker",
             "Usa el botón Spells del menú superior para mostrar el grid de hechizos."),
            ("3. Seleccionar un hechizo",
             "Haz clic sobre un hechizo del picker para seleccionarlo."),
            ("4. Add / Remove",
             "Usa Add para duplicar el hechizo seleccionado, Remove para eliminarlo."),
            ("5. Panel de Propiedades",
             "Con un hechizo seleccionado, edita valores en la pestaña Properties; cambia sprite/vfx en Assets/Particles."),
            ("6. Finalizar",
             "Pulsa F4 o ESC para cerrar el editor."),
        };

        // ── IGameEditor ──
        public string EditorName => "Spells Editor";
        public bool IsActive => _active;

        // ── Lifecycle ──

        protected override void OnSingletonAwake()
        {
            _toggleAction = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleSpells, out _ownsToggleAction);
        }

        private void Start()
        {
            // Lazy UI build (mirrors ItemsRuntimeEditor): nothing is created or visible
            // until the user presses F4 the first time.
            _active = false;
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
        }

        protected override void OnDestroy()
        {
            if (_ownsToggleAction) _toggleAction?.Dispose();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        private void Update()
        {
            if (EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleSpells))
            {
                if (GameEditorManager.HasInstance)
                    GameEditorManager.Instance.ToggleExclusive(this);
                else
                    ToggleActive();
            }

            if (!_active) return;

            // Middle-mouse camera pan — same UX as every other runtime editor.
            _cameraPan.Tick();

            // Esc: close tutorial first if open, otherwise close the editor.
            if (Valkur.Core.Input.KeyboardInputManager.WasEscapePressedThisFrame())
            {
                if (_uiRefs.TutorialDropdown != null && _uiRefs.TutorialDropdown.activeSelf)
                {
                    _openDropdowns.Remove("tutorial");
                    _uiRefs.TutorialDropdown.SetActive(false);
                    RefreshMenuBtnHighlights();
                }
                else
                {
                    if (GameEditorManager.HasInstance)
                        GameEditorManager.Instance.ToggleExclusive(this);
                    else
                        Deactivate();
                }
            }
        }

        public void Activate()
        {
            if (!_uiBuilt)
            {
                try { BuildUI(); _uiBuilt = true; }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[SpellsEditor] BuildUI failed at: {ex.GetType().Name} :: {ex.Message}");
                    Debug.LogException(ex);
                    return;
                }
            }
            _active = true;
            _root.SetActive(true);
            OpenAllPanels();
            RefreshPicker();
            RefreshPropertiesForm();
            SetStatus("Spells Editor active. F4 to close.");
            Debug.Log("[SpellsEditor] Activated (F4)");
        }

        public void Deactivate()
        {
            _active = false;
            if (_root != null) _root.SetActive(false);
            // Reattach the camera follow target if MMB pan had detached it.
            _cameraPan.Reset();
            Valkur.Gameplay.CameraSetup.Instance?.ReattachFollow();
            if (GameEditorManager.HasInstance)
                GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[SpellsEditor] Deactivated (F4)");
        }

        private void ToggleActive()
        {
            if (_active) Deactivate(); else Activate();
        }

        // ── Status / toast ──

        private void SetStatus(string msg)
        {
            if (_uiRefs.StatusText != null) _uiRefs.StatusText.text = msg;
        }

        private void Toast(string msg)
        {
            SetStatus(msg);
            Debug.Log($"[SpellsEditor] {msg}");
        }
    }
}