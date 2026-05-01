using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Valkur.Core.Input
{
    /// <summary>
    /// Persistent guard that pins the <c>InputManager.m_HasFocus</c> private flag
    /// to <c>true</c> every frame. Required because in the Unity Editor the flag
    /// flips to <c>false</c> any time another EditorWindow takes keyboard focus
    /// (Console, Inspector, MCP shell, even another part of the OS), and with the
    /// default focus-respecting settings that means OS Mouse / Keyboard events
    /// get reset before reaching <see cref="InputAction"/> handlers — clicks on
    /// the GameView never register, F-keys never fire, etc.
    ///
    /// <para>
    /// Combined with <see cref="InputSystemConfigurator"/> (which pins
    /// <see cref="InputSettings.backgroundBehavior"/> to <c>IgnoreFocus</c> and
    /// <see cref="InputSettings.editorInputBehaviorInPlayMode"/> to
    /// <c>AllDeviceInputAlwaysGoesToGameView</c> at boot), this is the second
    /// half of the fix: settings tell the InputSystem "ignore focus", and this
    /// keeps the cached focus flag itself permanently true so any OnFocusChanged
    /// callback Unity fires can't interrupt it.
    /// </para>
    ///
    /// Lifetime: Play-Mode-only. Spawned by <see cref="RuntimeInputBootstrap"/>
    /// at <c>BeforeSceneLoad</c>; persists with <c>DontDestroyOnLoad</c> for the
    /// rest of the session; auto-destroys on Play Mode exit.
    /// </summary>
    public sealed class InputFocusKeepalive : MonoBehaviour
    {
        private static InputFocusKeepalive _instance;

        private static FieldInfo  _managerField;
        private static FieldInfo  _hasFocusField;
        private static MethodInfo _onFocusChangedMethod;
        private static object     _manager;

        // Diagnostic counters — surface from runtime probes via MCP.
        public static int OsEventCount      { get; private set; }
        public static int MouseEventCount   { get; private set; }
        public static int KeyboardEventCount { get; private set; }
        public static int LeftClickFrameCount { get; private set; }
        public static int AnyKeyFrameCount    { get; private set; }

        public static InputFocusKeepalive Ensure()
        {
            if (_instance != null) return _instance;
            if (!Application.isPlaying) return null;

            var go = new GameObject("[InputFocusKeepalive]");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<InputFocusKeepalive>();
            return _instance;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _manager = null;
            _managerField = null;
            _hasFocusField = null;
            _onFocusChangedMethod = null;
            OsEventCount = 0;
            MouseEventCount = 0;
            KeyboardEventCount = 0;
            LeftClickFrameCount = 0;
            AnyKeyFrameCount = 0;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            CacheReflection();
            PinFocusFlag();
            UnityEngine.InputSystem.InputSystem.onEvent += OnAnyInputEvent;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            try { UnityEngine.InputSystem.InputSystem.onEvent -= OnAnyInputEvent; } catch { }
        }

        private void Update()
        {
            // Cheap: one private field set per frame. Always-true beats a
            // race against OnFocusChanged events from the editor.
            PinFocusFlag();

            // Diagnostic frame counters — exposed for MCP probes.
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m != null && m.leftButton.wasPressedThisFrame) LeftClickFrameCount++;
            var k = UnityEngine.InputSystem.Keyboard.current;
            if (k != null && k.anyKey.wasPressedThisFrame) AnyKeyFrameCount++;
        }

        private static void OnAnyInputEvent(UnityEngine.InputSystem.LowLevel.InputEventPtr evt,
                                            UnityEngine.InputSystem.InputDevice device)
        {
            OsEventCount++;
            if (device is UnityEngine.InputSystem.Mouse) MouseEventCount++;
            else if (device is UnityEngine.InputSystem.Keyboard) KeyboardEventCount++;
        }

        private static void CacheReflection()
        {
            try
            {
                var inputSystemType = typeof(InputSystem);
                _managerField = inputSystemType.GetField("s_Manager",
                    BindingFlags.NonPublic | BindingFlags.Static);
                _manager = _managerField?.GetValue(null);
                if (_manager == null) return;

                var managerType = _manager.GetType();
                _hasFocusField = managerType.GetField("m_HasFocus",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                _onFocusChangedMethod = managerType.GetMethod("OnFocusChanged",
                    BindingFlags.NonPublic | BindingFlags.Instance);
            }
            catch { /* Best-effort — InputSystem internals may differ across versions. */ }
        }

        private static void PinFocusFlag()
        {
            if (_hasFocusField == null) CacheReflection();
            if (_hasFocusField == null || _manager == null) return;
            try
            {
                var current = (bool)_hasFocusField.GetValue(_manager);
                if (current) return; // already true
                _hasFocusField.SetValue(_manager, true);
                _onFocusChangedMethod?.Invoke(_manager, new object[] { true });
            }
            catch { /* Reflection drift across InputSystem versions — non-fatal. */ }
        }
    }
}
