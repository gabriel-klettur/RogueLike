using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Core.UI;
using Valkur.UI.MainMenu.Kit;

namespace Valkur.UI.MainMenu
{
    /// <summary>
    /// Options → Controls, and it REBINDS now.
    ///
    /// <para><b>The defect this closes is not cosmetic.</b> The only rebinding surface in the
    /// game is <c>ControlsRuntimeEditor</c>, and it is built inside the <c>if (editors)</c> block
    /// of <c>GameplaySceneSetup.Sequence</c> — behind
    /// <c>RuntimeEditorPolicy.AuthoringEditorsAvailable</c>, which is FALSE in a release player.
    /// Both read-only panels (this one and the pause menu's) and a loading tip all told the
    /// player to go and use it. In a shipped build the player could not remap a single key.</para>
    ///
    /// <para><b>It writes the real model.</b> Overrides go through the same
    /// <c>InputBindingStore</c> the in-game editor writes, so a key set here is the key the game
    /// reads — unlike the panel this replaces twice over: the one before the read-only summary
    /// wrote <c>GameSettings.*KeyA</c>, a parallel string table with zero production readers.</para>
    ///
    /// <para><b>Twelve actions, not sixty.</b> This is the pre-game panel; the 24 spell slots and
    /// the editors' verbs belong to the in-game Controls editor, which can draw a whole keyboard.
    /// The hint says so rather than leaving the player to wonder whether the list is all of
    /// them.</para>
    /// </summary>
    public partial class MainMenuUI
    {
        private MenuPanelView _controlsPanel;
        private MenuList _controlsList;
        private TextMeshProUGUI _controlsBanner;

        private readonly List<InputActionDescriptor> _controlsActions = new List<InputActionDescriptor>();
        private int _capturingRow = -1;

        [SelfHealingStatic("Immutable table built once from literals. Nothing writes to it after the static initialiser, it holds no Unity object and no subscription, so it cannot carry a destroyed reference or a session decision across Play.")]
        private static readonly string[] ControlsActionIds =
        {
            "Gameplay/Move",
            "Gameplay/Dash",
            "Gameplay/PrimaryAttack",
            "Gameplay/SecondaryAttack",
            "Gameplay/MiddleClick",
            "Gameplay/Interact",
            "Gameplay/Inventory",
            "Gameplay/DropItem",
            "Gameplay/ToggleStance",
            "Gameplay/Pause",
            "Editors/OpenGeneralEditor",
            "Editors/ToggleDevConsole",
        };

        private void BuildControlsPanel(Transform canvas)
        {
            var style = Style;
            _controlsPanel = new MenuPanelView(canvas, _art, style, MenuText.ControlsTitle,
                                               style.widePanelWidth, ReduceMotion);
            _controlsList = new MenuList(_controlsPanel.Body, _art, style, ReduceMotion);
            _controlsActions.Clear();

            foreach (var id in ControlsActionIds)
            {
                var descriptor = InputActionCatalog.Find(id);
                if (descriptor == null) continue;
                var row = _controlsList.Add(_art, descriptor.DisplayName);
                row.Value.text = string.Empty;
                // An action the catalogue marks non-rebindable is SHOWN and greyed rather than
                // hidden: a control the player can see and cannot change is information, and one
                // that is simply absent looks like a control the game does not have.
                row.Interactable = descriptor.Rebindable;
                _controlsActions.Add(descriptor);
            }

            var resetRow = _controlsList.Add(_art, MenuText.ControlsReset);
            resetRow.Value.text = string.Empty;

            _controlsList.Changed += _ => _sfx?.Move();
            _controlsList.Chosen += OnControlsRowChosen;

            _controlsPanel.FitToContent(_controlsList.ContentHeight, 44f);
            // Thirteen rows plus the reset row do not fit under the title on an 800-unit canvas,
            // so this list is the one that really scrolls. Measured before the clamp: the panel
            // reached 1100 on an 800 screen.
            _controlsList.SetViewport(_controlsPanel.BodyHeight);

            var noteRt = MenuUIKit.Rect("Note", _controlsPanel.Root);
            noteRt.anchorMin = new Vector2(0f, 0f);
            noteRt.anchorMax = new Vector2(1f, 0f);
            noteRt.pivot = new Vector2(0.5f, 0f);
            noteRt.anchoredPosition = new Vector2(0f, style.hintBarHeight + 2f);
            noteRt.sizeDelta = new Vector2(-30f, 38f);
            _controlsBanner = MenuTypography.Label(noteRt.gameObject, style,
                                                   MenuText.ControlsMoreInGame,
                                                   style.detailFontSize, style.TextMuted,
                                                   TextAlignmentOptions.Center);
            _controlsBanner.enableWordWrapping = true;

            _controlsPanel.SetHint(MenuText.ControlsHint);
            _controlsPanel.Close();
        }

        private void RefreshControlsRows()
        {
            if (_controlsList == null) return;   // not built yet: nothing to repaint
            var asset = InputService.Instance?.Asset;
            var rows = _controlsList.Rows;

            for (int i = 0; i < _controlsActions.Count && i < rows.Count; i++)
            {
                var descriptor = _controlsActions[i];
                var map = asset?.FindActionMap(descriptor.Map, throwIfNotFound: false);
                var action = map?.FindAction(descriptor.Action, throwIfNotFound: false);
                string label = action == null ? "?" : InputBindingResolver.PrimaryLabel(action);
                bool unbound = string.IsNullOrEmpty(label);
                rows[i].Value.text = unbound ? MenuText.ControlsUnbound : label;
                rows[i].Value.color = unbound ? Style.TextMuted
                                     : rows[i].Selected ? Style.textOnSelection : Style.Gold;
            }

            if (_controlsBanner != null && _capturingRow < 0)
                _controlsBanner.text = MenuText.ControlsMoreInGame;
        }

        private void OnControlsRowChosen(int index)
        {
            if (index == _controlsActions.Count) { ResetControlsToDefaults(); return; }
            BeginControlsCapture(index);
        }

        private void BeginControlsCapture(int index)
        {
            if (index < 0 || index >= _controlsActions.Count) { _sfx?.Refuse(); return; }
            if (!_controlsActions[index].Rebindable) { _sfx?.Refuse(); return; }
            _capturingRow = index;
            if (_controlsBanner != null)
            {
                _controlsBanner.text = MenuText.ControlsCapture;
                _controlsBanner.color = Style.Gold;
            }
            _sfx?.Confirm();
        }

        /// <summary>True when there WAS a capture to cancel, so Esc can be consumed by it.</summary>
        private bool CancelControlsCapture()
        {
            if (_capturingRow < 0) return false;
            _capturingRow = -1;
            if (_controlsBanner != null)
            {
                _controlsBanner.text = MenuText.ControlsMoreInGame;
                _controlsBanner.color = Style.TextMuted;
            }
            _sfx?.Cancel();
            return true;
        }

        private void HandleControlsInput()
        {
            if (_capturingRow >= 0) { PollControlsCapture(); return; }
            HandleListInput(_controlsList, OptionsGoBack);
        }

        /// <summary>
        /// Reads the next key or mouse button pressed and binds it.
        ///
        /// <para><b>Through the centralized helpers, never the raw device.</b> The in-game
        /// editor's first version walked <c>Keyboard.current</c> directly, which meant no mouse
        /// button could ever be assigned AND that it stopped working under exactly the
        /// InputSystem event-drop bug that sends a player looking for the Controls screen in the
        /// first place. <c>KeyboardInputManager</c> and <c>MouseInputManager</c> OR both
        /// backends.</para>
        ///
        /// <para><b>The LEFT mouse button is not polled.</b> It is how the player clicked the row
        /// to start the capture, so polling it would bind LMB to whatever they were pointing at.
        /// Right click CLEARS the binding, which is also why it is not a candidate.</para>
        /// </summary>
        private void PollControlsCapture()
        {
            if (KeyboardInputManager.WasEscapePressedThisFrame()) { CancelControlsCapture(); return; }

            if (MouseInputManager.WasRightMouseButtonPressedThisFrame())
            {
                ApplyCapturedPath(string.Empty);
                return;
            }
            if (MouseInputManager.WasMiddleMouseButtonPressedThisFrame())
            {
                ApplyCapturedPath(InputControlPaths.PathForMouse(MouseControl.Middle));
                return;
            }

            foreach (var entry in InputControlPaths.Entries)
            {
                if (!InputControlPaths.IsKeyboardPath(entry.Path)) continue;
                if (!KeyboardInputManager.WasKeyPressedThisFrame(entry.Key, entry.Legacy)) continue;
                ApplyCapturedPath(entry.Path);
                return;
            }
        }

        private void ApplyCapturedPath(string path)
        {
            int index = _capturingRow;
            _capturingRow = -1;
            if (index < 0 || index >= _controlsActions.Count) return;

            var descriptor = _controlsActions[index];
            var asset = InputService.Instance?.Asset;
            var map = asset?.FindActionMap(descriptor.Map, throwIfNotFound: false);
            var action = map?.FindAction(descriptor.Action, throwIfNotFound: false);
            if (action == null) { _sfx?.Refuse(); RefreshControlsRows(); return; }

            // Slot 0 of the action's own bindings, skipping composite headers: an override that
            // lands on a 2DVector header moves nothing while reporting success — the defect the
            // in-game editor already had to fix.
            int slot = FirstBindableSlot(action);
            if (slot < 0) { _sfx?.Refuse(); RefreshControlsRows(); return; }

            action.ApplyBindingOverride(slot, path);
            InputBindingResolver.Invalidate();
            InputBindingStore.MarkDirty();
            InputBindingStore.Save();

            _sfx?.Confirm();
            RefreshControlsRows();
        }

        private static int FirstBindableSlot(InputAction action)
        {
            // InputChord.Slots folds a Shift+key chord into one slot whose index is the KEY, so a
            // rebind from the menu moves the key and keeps the modifier.
            var slots = InputChord.Slots(action);
            if (slots.Count > 0) return slots[0].Index;
            return action.bindings.Count > 0 ? 0 : -1;
        }

        private void ResetControlsToDefaults()
        {
            InputBindingStore.ResetToDefaults();
            InputBindingResolver.Invalidate();
            _sfx?.Confirm();
            RefreshControlsRows();
        }
    }
}
