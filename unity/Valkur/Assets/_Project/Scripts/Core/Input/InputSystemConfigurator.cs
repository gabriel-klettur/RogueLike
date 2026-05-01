using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Valkur.Core.Input
{
    /// <summary>
    /// Boot-time input pipeline fix-ups. Solves the recurring bug where, in the
    /// Unity Editor, Play Mode starts but no <see cref="InputAction"/> ever fires:
    /// UI clicks ignored, F-keys dead, attack click-to-cast inert, etc.
    ///
    /// Root cause: when the user (or MCP) presses Play, the Editor sometimes
    /// enters Play Mode WITHOUT a focus event ever reaching the InputSystem
    /// runtime. Internally <c>InputManager.m_HasFocus = false</c> stays false,
    /// and with the default
    /// <see cref="InputSettings.BackgroundBehavior.ResetAndDisableNonBackgroundDevices"/>
    /// every <c>Mouse</c>/<c>Keyboard</c> event arriving from the OS is reset
    /// before the action pipeline can read it. Symptom seen in the trace:
    /// <c>InputEventTrace.eventCount &gt; 0</c> (events DID arrive) but
    /// <c>Mouse.current.position</c> stays at <c>(0, 0)</c> and no action ever
    /// transitions to Performed.
    ///
    /// Fix-ups applied at <c>BeforeSceneLoad</c> in Play Mode only:
    ///   1. <b>Pin runtime settings</b> — <see cref="InputSettings.backgroundBehavior"/>
    ///      to <see cref="InputSettings.BackgroundBehavior.IgnoreFocus"/> so the
    ///      InputSystem stops resetting device state on focus changes; and
    ///      <see cref="InputSettings.editorInputBehaviorInPlayMode"/> to
    ///      <see cref="InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView"/>
    ///      so OS events are routed to the GameView regardless of which
    ///      EditorWindow currently has keyboard focus.
    ///   2. <b>Force-restore m_HasFocus</b> — via reflection set the manager's
    ///      private flag to <c>true</c> and call its <c>OnFocusChanged(true)</c>
    ///      so already-disabled devices come back online and queued events
    ///      get processed normally.
    ///   3. <b>Dedup duplicate Mouse/Keyboard</b> — Domain-Reload-OFF leaves
    ///      virtual + real devices coexisting; the action binding resolver
    ///      then picks up controls from both and the interaction state machine
    ///      evaluates ambiguously and never fires.
    ///   4. <b>Flip CanRunInBackground bits on every device</b> — both
    ///      <c>CanRunInBackground</c> AND <c>CanRunInBackgroundHasBeenQueried</c>
    ///      (without the second bit the property getter re-queries the native
    ///      runtime, which always answers false for Mouse/Keyboard).
    ///
    /// <para>
    /// <b>EditMode safety:</b> the runtime settings (1) are reverted on Play
    /// Mode exit by <see cref="PlayLifeline.OnDestroy"/>, so EditMode tests
    /// always see Unity defaults. The asset on disk is never modified.
    /// </para>
    /// </summary>
    public static class InputSystemConfigurator
    {
        private static bool _applied;
        private static bool _settingsCaptured;
        private static InputSettings.BackgroundBehavior _origBackgroundBehavior;
        private static InputSettings.EditorInputBehaviorInPlayMode _origEditorInputBehavior;

        /// <summary>
        /// Apply the boot-time fix-ups. Idempotent. In EditMode (outside Play)
        /// only the dedup + canRunInBackground sweeps run — settings are
        /// untouched so EditMode tests see Unity defaults.
        /// </summary>
        public static void Apply()
        {
            // Always-safe steps (run in EditMode + Play).
            RemoveDuplicateMouseAndKeyboard();
            EnableCanRunInBackgroundOnAllDevices();

            // Play-only steps. Mutating InputSettings + the manager's focus flag
            // outside Play would contaminate EditMode test fixtures that depend
            // on default Unity behaviour for synthetic event delivery.
            if (Application.isPlaying)
            {
                CaptureOriginalSettingsOnce();
                ApplyRuntimeSettings();
                ForceFocusFlagTrue();
                EnsureApplicationRunInBackground();
            }

            _applied = true;
        }

        /// <summary>
        /// Restores the InputSettings captured by the first <see cref="Apply"/>
        /// call so EditMode tests run against vanilla Unity defaults. Invoked
        /// when Play Mode exits (the <see cref="PlayLifeline"/> sentinel
        /// MonoBehaviour's <c>OnDestroy</c> fires).
        /// </summary>
        public static void RestoreOriginalSettings()
        {
            if (!_settingsCaptured) return;
            var s = InputSystem.settings;
            if (s != null)
            {
                s.backgroundBehavior = _origBackgroundBehavior;
                s.editorInputBehaviorInPlayMode = _origEditorInputBehavior;
            }
            _settingsCaptured = false;
            _applied = false;
        }

        public static bool HasApplied => _applied;

        // ── Sub-routines (public so tests can target them) ──────────────────

        public static int RemoveDuplicateMouseAndKeyboard()
        {
            var keepMouse = Mouse.current;
            var keepKeyboard = Keyboard.current;
            var toRemove = new List<InputDevice>();
            foreach (var d in InputSystem.devices)
            {
                if (d is Mouse && d != keepMouse) toRemove.Add(d);
                else if (d is Keyboard && d != keepKeyboard) toRemove.Add(d);
            }
            foreach (var d in toRemove) InputSystem.RemoveDevice(d);
            return toRemove.Count;
        }

        public static void EnableCanRunInBackgroundOnAllDevices()
        {
            var deviceType = typeof(InputDevice);
            var flagsField = deviceType.GetField("m_DeviceFlags", BindingFlags.NonPublic | BindingFlags.Instance);
            if (flagsField == null) return;
            var flagsType = flagsField.FieldType;
            int canRunFlag, queriedFlag;
            try
            {
                canRunFlag = (int)System.Enum.Parse(flagsType, "CanRunInBackground");
                queriedFlag = (int)System.Enum.Parse(flagsType, "CanRunInBackgroundHasBeenQueried");
            }
            catch { return; }
            int wantBits = canRunFlag | queriedFlag;
            foreach (var d in InputSystem.devices)
            {
                int cur = (int)flagsField.GetValue(d);
                if ((cur & wantBits) == wantBits) continue;
                flagsField.SetValue(d, System.Enum.ToObject(flagsType, cur | wantBits));
            }
        }

        public static void EnsureApplicationRunInBackground()
        {
            if (!Application.runInBackground) Application.runInBackground = true;
        }

        public static void ApplyRuntimeSettings()
        {
            var s = InputSystem.settings;
            if (s == null) return;
            if (s.backgroundBehavior != InputSettings.BackgroundBehavior.IgnoreFocus)
                s.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            if (s.editorInputBehaviorInPlayMode != InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView)
                s.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        }

        /// <summary>
        /// Force the InputSystem manager's internal <c>m_HasFocus</c> flag to
        /// <c>true</c> and replay <c>OnFocusChanged(true)</c>. Without this
        /// step, Play Mode entered while another EditorWindow has keyboard
        /// focus leaves the manager convinced the app has no focus, and
        /// every event from the OS is reset before reaching action handlers.
        /// </summary>
        public static void ForceFocusFlagTrue()
        {
            try
            {
                var inputSystemType = typeof(InputSystem);
                var managerField = inputSystemType.GetField("s_Manager",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var manager = managerField?.GetValue(null);
                if (manager == null) return;

                var managerType = manager.GetType();
                var hasFocusField = managerType.GetField("m_HasFocus",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                hasFocusField?.SetValue(manager, true);

                var onFocusChanged = managerType.GetMethod("OnFocusChanged",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                onFocusChanged?.Invoke(manager, new object[] { true });
            }
            catch { /* Reflection failures are non-fatal — the settings already cover most cases. */ }
        }

        // ── Internals ───────────────────────────────────────────────────────

        private static void CaptureOriginalSettingsOnce()
        {
            if (_settingsCaptured) return;
            var s = InputSystem.settings;
            if (s == null) return;
            _origBackgroundBehavior = s.backgroundBehavior;
            _origEditorInputBehavior = s.editorInputBehaviorInPlayMode;
            _settingsCaptured = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _applied = false;
            _settingsCaptured = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRestoreOnPlayExit()
        {
            if (!Application.isPlaying) return;
            var go = new GameObject("[InputSystemConfiguratorPlayLifeline]");
            go.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(go);
            go.AddComponent<PlayLifeline>();
        }

        /// <summary>
        /// Sentinel MonoBehaviour whose <c>OnDestroy</c> fires when Play Mode
        /// tears down — the moment we restore the captured input settings so
        /// EditMode tests run against vanilla Unity defaults.
        /// </summary>
        private sealed class PlayLifeline : MonoBehaviour
        {
            private void OnDestroy() => RestoreOriginalSettings();
        }
    }
}
