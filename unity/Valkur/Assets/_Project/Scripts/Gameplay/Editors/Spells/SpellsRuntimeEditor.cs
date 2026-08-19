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
    public partial class SpellsRuntimeEditor : SingletonMonoBehaviour<SpellsRuntimeEditor>,
        GameEditorManager.IGameEditor, Valkur.Core.IChoosesPrimaryCastSpell
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
            ("1. Welcome",
             "This tutorial walks you through the key features: opening the picker, selecting a spell, duplicate/delete via Add/Remove, and the properties panel."),
            ("2. Show the Picker",
             "Use the Spells button in the top menu to show the spell grid."),
            ("3. Select a spell",
             "Click a spell in the picker to select it."),
            ("4. Add / Remove",
             "Use Add to duplicate the selected spell, Remove to delete it."),
            ("5. Properties Panel",
             "With a spell selected, edit values in the Properties tab; change sprite/vfx in Assets/Particles."),
            ("6. Finish",
             "Press F4 or ESC to close the editor."),
        };

        // ── IGameEditor ──
        public string EditorName => "Spells Editor";
        public bool IsActive => _active;

        /// <summary>
        /// The spell LEFT CLICK casts while this editor is open, so a spell can be tried out in
        /// the world without leaving the editor or rebinding a slot.
        ///
        /// Gated on <see cref="IsActive"/> rather than just returning the selection: the editor
        /// is a scene singleton that outlives its open state, and a stale key here would keep
        /// redirecting the player's primary attack long after F4 was closed. Null while closed
        /// is what makes "closed behaves exactly as before" true by construction rather than by
        /// remembering to clear something.
        /// </summary>
        public string PrimaryCastSpellKey => _active ? _selectedKey : null;

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
            ShutdownPreview();
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

            // Drive the live spell preview when the View panel is open.
            if (_openDropdowns.Contains("view"))
            {
                _previewService?.Tick();
                TickPreviewInput();
            }

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
            RefreshActivePicker();
            RefreshPropertiesForm();
            SetStatus("Spells Editor active. F4 to close.");
            Debug.Log("[SpellsEditor] Activated (F4)");
        }

        public void Deactivate()
        {
            _active = false;
            // Tear down the live preview before hiding the canvas so the camera /
            // RenderTexture / spawned spell objects are released and audio mute is
            // restored. Safe to call even if the View panel was never opened.
            ShutdownPreview();
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