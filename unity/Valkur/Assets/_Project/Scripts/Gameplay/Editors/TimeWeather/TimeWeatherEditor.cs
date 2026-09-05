using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.TimeWeather
{
    /// <summary>
    /// Runtime in-game Time &amp; Weather Editor (F2). Modeled on the same
    /// menu-bar + draggable-panel architecture used by the Tile (F8),
    /// Buildings (F10), Items (F7) and Lighting (Ctrl+F3) editors so the
    /// player learns one chrome and reuses it across every editor.
    ///
    /// Hosts every modifying control for the time / weather subsystems:
    ///   • Speed   — slider 1×..100× over the day/night cycle.
    ///   • Fases   — phase shortcut buttons (Amanecer / Mañana / Mediodía /
    ///               Atardecer / Medianoche) + OFF · SIN FILTRO.
    ///   • Clima   — Wind / Rain / Snow toggles + OFF · DESPEJADO.
    ///   • Ajustes — per-phase tuning (5 sliders × 2 tabs) with DEFECTO /
    ///               NEUTRO presets.
    ///
    /// The clock HUD (sundial + HH:MM + phase label) stays always-visible —
    /// only the modifying controls live in here.
    ///
    /// Coexists with <see cref="Combat.CombatRangeVisualizer"/> on the F2
    /// binding: this editor requires bare F2 (no Ctrl, no Alt). Alt+F2 still
    /// fires the combat-range visualiser.
    /// </summary>
    public partial class TimeWeatherEditor : SingletonMonoBehaviour<TimeWeatherEditor>,
        GameEditorManager.IGameEditor, IAllowsPlayerMovement
    {
        // ── State ────────────────────────────────────────────────────────────
        private bool _active;
        private bool _uiBuilt;

        // Cached for FKeyBindingParityTests-style reflection. Live resolution
        // happens through the stateless EditorHotkeyBindings API every Update.
        private InputAction _toggleAction;
        private bool        _ownsToggleAction;
        private InputAction _altModifier;
        private bool        _ownsAltModifier;
        private InputAction _ctrlModifier;
        private bool        _ownsCtrlModifier;

        // ── UI ───────────────────────────────────────────────────────────────
        private Canvas _canvas;
        private GameObject _root;
        private TimeWeatherEditorUIBuilder.UIRefs _ui;
        private GameObject _tutorial;

        // Dropdown open/close state — mirrors LightingRuntimeEditor.
        private readonly HashSet<string> _openDropdowns = new HashSet<string>();

        // ── IGameEditor ──────────────────────────────────────────────────────
        public string EditorName => "Time & Weather";
        public bool   IsActive   => _active;

        // ── Lifecycle ────────────────────────────────────────────────────────

        protected override void OnSingletonAwake()
        {
            _toggleAction = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleTimeWeather, out _ownsToggleAction);
            _altModifier  = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.AltModifier,        out _ownsAltModifier);
            _ctrlModifier = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.CtrlModifier,       out _ownsCtrlModifier);
        }

        private void Start()
        {
            _active = false;
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Register(this);
        }

        protected override void OnDestroy()
        {
            if (_ownsToggleAction) _toggleAction?.Dispose();
            if (_ownsAltModifier)  _altModifier?.Dispose();
            if (_ownsCtrlModifier) _ctrlModifier?.Dispose();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.Unregister(this);
            base.OnDestroy();
        }

        private void Update()
        {
            // Bare F2 only — Alt+F2 → CombatRangeVisualizer; Ctrl+F2 unused.
            if (EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleTimeWeather) &&
                !EditorHotkeyBindings.IsPressed(EditorHotkeyBindings.Hotkey.AltModifier) &&
                !EditorHotkeyBindings.IsPressed(EditorHotkeyBindings.Hotkey.CtrlModifier))
            {
                if (GameEditorManager.HasInstance) GameEditorManager.Instance.ToggleExclusive(this);
                else                               ToggleActive();
            }

            if (!_active) return;

            HandleKeyboardShortcuts();
            // Re-read the live cycle every frame so the panels follow the clock as it runs,
            // and so an OFF button or a phase jump lands on every widget at once.
            //
            // NOT, despite what this used to say, to track the Ctrl+F3 Lighting Editor:
            // GameEditorManager.ToggleExclusive guarantees only one runtime editor is open,
            // so that editor cannot be moving anything while this one is drawing.
            SyncSpeedFromCycle();
            SyncCycleHighlightFromLive();
            SyncWeatherHighlightsFromLive();
            SyncSettingsFromLive();
        }

        public void Activate()
        {
            if (!_uiBuilt)
            {
                try { BuildUI(); _uiBuilt = true; }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[TimeWeatherEditor] BuildUI failed: {ex.GetType().Name} :: {ex.Message}");
                    Debug.LogException(ex);
                    return;
                }
            }
            _active = true;
            _root.SetActive(true);
            OpenAllPanels();
            // Hard-refresh UI from the live cycle so the player sees current
            // state immediately rather than waiting for the next Update tick.
            SyncSpeedFromCycle();
            SyncCycleHighlightFromLive();
            SyncWeatherHighlightsFromLive();
            SyncSettingsFromLive();
            SetStatus("Time & Weather editor active. F2 to close.");
            Debug.Log("[TimeWeatherEditor] Activated (F2)");
        }

        public void Deactivate()
        {
            _active = false;
            if (_root != null) _root.SetActive(false);
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[TimeWeatherEditor] Deactivated (F2)");
        }

        private void ToggleActive()
        {
            if (_active) Deactivate(); else Activate();
        }

        // ── UI build ─────────────────────────────────────────────────────────

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("TimeWeatherEditorCanvas", 112);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            _ui = TimeWeatherEditorUIBuilder.BuildAll(
                _root.transform,
                onDropdownToggle:        ToggleDropdown,
                onSpeedChanged:          OnSpeedSliderChanged,
                onCycleRowClicked:       BuildCycleRowCallbacks(),
                onCycleOffClicked:       OnCycleOffClicked,
                onWeatherRowClicked:     BuildWeatherRowCallbacks(),
                onWeatherOffClicked:     OnWeatherOffClicked,
                onSettingsTabClicked:    OnSettingsTabClicked,
                onSettingsSliderChanged: OnSettingsSliderChanged,
                onSettingsResetClicked:  OnSettingsResetClicked,
                onSettingsNeutroClicked: OnSettingsNeutroClicked,
                onToggleTutorial:        ToggleTutorial);

            // Wire panel close buttons → keep menu-bar highlight in sync.
            WireOnClose(_ui.SpeedPanelDrag,    "speed");
            WireOnClose(_ui.CyclePanelDrag,    "cycle");
            WireOnClose(_ui.WeatherPanelDrag,  "weather");
            WireOnClose(_ui.SettingsPanelDrag, "settings");

            BuildTutorial();
            RefreshMenuBtnHighlights();
        }

        private void WireOnClose(DraggablePanel drag, string key)
        {
            if (drag == null) return;
            drag.OnClose = () =>
            {
                _openDropdowns.Remove(key);
                RefreshMenuBtnHighlights();
            };
        }

        // ── Tutorial overlay ─────────────────────────────────────────────────

        private void BuildTutorial()
        {
            _tutorial = TutorialOverlay.Build(_root.transform, "TIME & WEATHER — SHORTCUTS", new[]
            {
                ("F2",       "Open/close editor"),
                ("Alt+F2",   "Combat range visualizer"),
                ("Esc",      "Close editor"),
                ("Slider",   "Day/night cycle speed"),
                ("Phases",   "Jump to a specific phase of day"),
                ("Weather",  "Toggle wind/rain/snow (stackable)"),
                ("Settings", "Edit Hue/Saturation/Brightness/Warmth/Vignette per phase"),
                ("DEFAULT",  "Restore phase to its cinematic defaults"),
                ("NEUTRAL",  "Leave the phase completely unfiltered"),
            });
            _tutorial.SetActive(false);
        }

        private void ToggleTutorial()
        {
            if (_tutorial == null) return;
            _tutorial.SetActive(!_tutorial.activeSelf);
        }

        // ── Dropdown management ──────────────────────────────────────────────

        private void ToggleDropdown(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (_openDropdowns.Contains(name))
            {
                SetDropdownOpen(name, false);
                _openDropdowns.Remove(name);
            }
            else
            {
                SetDropdownOpen(name, true);
                _openDropdowns.Add(name);
            }
            RefreshMenuBtnHighlights();
        }

        private void OpenAllPanels()
        {
            foreach (var n in new[] { "speed", "cycle", "weather", "settings" })
            {
                SetDropdownOpen(n, true);
                _openDropdowns.Add(n);
            }
            RefreshMenuBtnHighlights();
        }

        private void SetDropdownOpen(string name, bool open)
        {
            var go = name switch
            {
                "speed"    => _ui.SpeedDropdown,
                "cycle"    => _ui.CycleDropdown,
                "weather"  => _ui.WeatherDropdown,
                "settings" => _ui.SettingsDropdown,
                _          => null
            };
            if (go != null) go.SetActive(open);
        }

        private void RefreshMenuBtnHighlights()
        {
            TimeWeatherEditorUIBuilder.ApplyMenuBtnStyle(_ui.SpeedMenuBtnImg,    _ui.SpeedMenuBtnTmp,    _openDropdowns.Contains("speed"));
            TimeWeatherEditorUIBuilder.ApplyMenuBtnStyle(_ui.CycleMenuBtnImg,    _ui.CycleMenuBtnTmp,    _openDropdowns.Contains("cycle"));
            TimeWeatherEditorUIBuilder.ApplyMenuBtnStyle(_ui.WeatherMenuBtnImg,  _ui.WeatherMenuBtnTmp,  _openDropdowns.Contains("weather"));
            TimeWeatherEditorUIBuilder.ApplyMenuBtnStyle(_ui.SettingsMenuBtnImg, _ui.SettingsMenuBtnTmp, _openDropdowns.Contains("settings"));
        }

        // ── Status helper ────────────────────────────────────────────────────

        private void SetStatus(string msg)
        {
            if (_ui.StatusText != null) _ui.StatusText.text = msg;
        }

        // ── Keyboard shortcuts ───────────────────────────────────────────────

        private void HandleKeyboardShortcuts()
        {
            if (EditorInput.ClosePressed())
            {
                if (_tutorial != null && _tutorial.activeSelf) _tutorial.SetActive(false);
                else                                            Deactivate();
            }
        }
    }
}
