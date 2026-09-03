using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.Boss
{
    /// <summary>
    /// Runtime in-game Boss Editor.
    ///
    /// Accessible via the General Editor (ESC) launcher button "Boss".
    /// Provides a three-panel UI for authoring <see cref="BossDefinition"/>
    /// phase data and <see cref="BossChart"/> beat-cue charts:
    ///   Left  — Bosses list (all BossDefinition assets)
    ///   Middle — Phases and charts for the selected boss
    ///   Right  — Cue inspector table for the selected chart
    ///
    /// Charts are saved as .asset files under
    /// Assets/_Project/Data/Bosses/Charts/ via AssetDatabase (Editor-only).
    ///
    /// No F-key hotkey — reachable only through the General Editor launcher
    /// or via <see cref="OpenWithBoss"/> called by the Entities Editor (F5).
    /// </summary>
    public partial class BossEditorManager
        : SingletonMonoBehaviour<BossEditorManager>, GameEditorManager.IGameEditor
    {
        // ── State ────────────────────────────────────────────────────────────────

        private bool _active;

        // Undo/redo (capacity 64, consistent with all other editors).
        private readonly UndoStack _undo = new UndoStack(64);

        // Middle-mouse camera pan — shared controller used by all runtime editors.
        private readonly EditorCameraPanController _cameraPan = new EditorCameraPanController();

        // Mouse-wheel zoom, shared with every runtime editor that can pan. An editor that
        // lets the author move the world camera but not close in on it is the odd one out,
        // and eight of the eleven panning editors were exactly that. The controller steps
        // through CameraSetup.ComputeEditorZoomNext, which stays on the PPU ladder
        // SnapOrthoSize maintains — that is why spreading it does not fall foul of the
        // "never write orthographicSize for an effect" rule.
        private readonly EditorCameraZoomController _cameraZoom = new EditorCameraZoomController();

        // ── UI ───────────────────────────────────────────────────────────────────

        private Canvas       _canvas;
        private GameObject   _root;
        private BossEditorUIBuilder.UIRefs _ui;

        // Open-dropdown tracking (mirrors Particles / Entities pattern).
        private readonly HashSet<string> _openDropdowns = new HashSet<string>();

        // Tutorial overlay + confirm-delete modal.
        private GameObject        _tutorialRoot;
        private TMPro.TextMeshProUGUI _tutorialStepLabel;
        private TMPro.TextMeshProUGUI _tutorialBodyTmp;
        private int               _tutorialStep;

        private GameObject         _confirmModal;
        private TMPro.TextMeshProUGUI _confirmText;
        private System.Action      _pendingConfirmYes;

        private static readonly (string title, string body)[] TUTORIAL_STEPS =
        {
            ("1. Open editor",     "Click 'Boss' in the General Editor (ESC) to open this editor, " +
                                   "or select a boss monster in the Entities Editor (F5) and click " +
                                   "'Open Boss Editor →'."),
            ("2. Pick a boss",     "The Bosses panel lists every BossDefinition asset in the project. " +
                                   "Click a row to select it."),
            ("3. Pick / add chart","In the Phases & Charts panel, expand a phase row to see its charts. " +
                                   "Click a chart to inspect it, or click '+ Chart' to create one. " +
                                   "Each chart targets one music track id."),
            ("4. Set track id",    "Type the music track id in the 'Music Track ID' field at the top of " +
                                   "the Cue Inspector. This must match a MusicTrackEntry.id in your " +
                                   "audio catalog so the dispatcher knows which song drives the chart."),
            ("5. Authoring modes", "Four authoring modes appear above the cue list:\n" +
                                   "  Numeric — edit cue rows directly (default).\n" +
                                   "  Tap     — press SPACE while music plays to stamp a cue at the current beat.\n" +
                                   "  Quantize — step-sequencer grid; click cells to toggle cues at bar.beat slots.\n" +
                                   "  Auto    — import beat times from a pre-analysed MusicTrackEntry.\n" +
                                   "All modes wrap every cue change in Undo (Ctrl+Z / Ctrl+Y)."),
            ("6. Set type + target","Choose the cue type from the dropdown " +
                                   "(CastSpell / PlaySfx / SwitchPhase / SpawnAdd / Taunt / PlayAnim). " +
                                   "When type = CastSpell, pick the spell from the Target Key dropdown " +
                                   "which is populated from the SpellCatalog."),
            ("7. Timeline strip",  "A colour-coded timeline strip at the bottom of the Cue Inspector " +
                                   "shows all cues mapped to the loop window. Orange = CastSpell, " +
                                   "cyan = PlaySfx, purple = SwitchPhase, green = SpawnAdd. " +
                                   "A white playhead moves in real time when music is playing."),
            ("8. Live Preview",    "Click 'Live Preview' in the menu bar to spawn a configured boss " +
                                   "in front of the player. The boss runs the full choreography pipeline " +
                                   "so you can hear and see spells fire in sync. " +
                                   "The preview is torn down automatically when you close the editor, " +
                                   "change the selected boss, or click 'Stop Preview'."),
            ("9. Save & close",    "Click 'Save Chart' to write the BossChart .asset file to " +
                                   "Assets/_Project/Data/Bosses/Charts/ (Ctrl+S). " +
                                   "Press ESC or click the General Editor button to close. " +
                                   "Any unsaved changes are noted in the status bar."),
        };

        // ── IGameEditor ─────────────────────────────────────────────────────────

        public string EditorName => "Boss Editor";
        public bool   IsActive   => _active;

        // ── Lifecycle ───────────────────────────────────────────────────────────

        private void Start()
        {
            BuildUI();
            _root.SetActive(false);
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
        }

        protected override void OnDestroy()
        {
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        private void Update()
        {
            _cameraPan.Tick();
            _cameraZoom.Tick();
            if (!_active) return;
            HandleKeyboardShortcuts();
            TickEditorExtensions();
        }

        // ── Keyboard shortcuts ─────────────────────────────────────────────────

        private void HandleKeyboardShortcuts()
        {
            // Routed through KeyboardInputManager so the legacy backend keeps
            // these shortcuts working under the InputSystem-drops-events bug.
            bool ctrl = KeyboardInputManager.IsCtrlHeld();
            if (ctrl && KeyboardInputManager.WasKeyPressedThisFrame(Key.Z, KeyCode.Z))
            {
                _undo.Undo();
                RefreshUndoRedoButtons();
                SetStatus("Undo");
            }
            if (ctrl && KeyboardInputManager.WasKeyPressedThisFrame(Key.Y, KeyCode.Y))
            {
                _undo.Redo();
                RefreshUndoRedoButtons();
                SetStatus("Redo");
            }
            if (ctrl && KeyboardInputManager.WasKeyPressedThisFrame(Key.S, KeyCode.S))
            {
                SaveSelectedChart();
            }
            if (KeyboardInputManager.WasEscapePressedThisFrame())
            {
                if (_tutorialRoot != null && _tutorialRoot.activeSelf)
                    _tutorialRoot.SetActive(false);
                else if (_confirmModal != null && _confirmModal.activeSelf)
                    HideConfirm();
                else
                    Deactivate();
            }
        }

        public void Activate()
        {
            _active = true;
            _root.SetActive(true);
            OpenDefaultDropdowns();
            RefreshBossList();
            RefreshUndoRedoButtons();
            RefreshPreviewButton();   // restore highlight after re-activation
            SetStatus("Boss Editor active. ESC to close.");
            Debug.Log("[BossEditor] Activated");
        }

        public void Deactivate()
        {
            _active = false;
            DeactivateLivePreview();
            _root.SetActive(false);
            _cameraPan.Reset();
            Valkur.Gameplay.CameraSetup.Instance?.ReattachFollow();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[BossEditor] Deactivated");
        }

        // ── Dropdown management ────────────────────────────────────────────────

        private void OpenDefaultDropdowns()
        {
            _openDropdowns.Clear();
            SetDropdownOpen("bosses",  true);
            SetDropdownOpen("phases",  true);
            SetDropdownOpen("cues",    true);
            RefreshMenuBtnHighlights();
        }

        private void ToggleDropdown(string name)
        {
            bool willOpen = !_openDropdowns.Contains(name);
            SetDropdownOpen(name, willOpen);
            RefreshMenuBtnHighlights();
        }

        private void SetDropdownOpen(string name, bool open)
        {
            var go = GetDropdownPanel(name);
            if (go == null) return;
            if (open) _openDropdowns.Add(name);
            else      _openDropdowns.Remove(name);
            go.SetActive(open);
        }

        private GameObject GetDropdownPanel(string name) => name switch
        {
            "bosses" => _ui.BossesDropdown,
            "phases" => _ui.PhasesDropdown,
            "cues"   => _ui.CuesDropdown,
            _        => null
        };

        private void RefreshMenuBtnHighlights()
        {
            EditorUIHelpers.ApplyMenuBtnStyle(_ui.BossesMenuBtnImg, _ui.BossesMenuBtnTmp, _openDropdowns.Contains("bosses"));
            EditorUIHelpers.ApplyMenuBtnStyle(_ui.PhasesMenuBtnImg, _ui.PhasesMenuBtnTmp, _openDropdowns.Contains("phases"));
            EditorUIHelpers.ApplyMenuBtnStyle(_ui.CuesMenuBtnImg,   _ui.CuesMenuBtnTmp,   _openDropdowns.Contains("cues"));
        }

        // ── Status helper ──────────────────────────────────────────────────────

        private void SetStatus(string msg)
        {
            if (_ui.StatusText != null) _ui.StatusText.text = msg;
        }
    }
}
