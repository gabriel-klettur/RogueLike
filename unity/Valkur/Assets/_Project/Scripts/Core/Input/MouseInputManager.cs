using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

namespace Valkur.Core.Input
{
    /// <summary>
    /// Centralized mouse input manager for the entire game.
    /// Provides safe, tested access to mouse position, buttons, and raycasts.
    /// Handles null safety and device initialization automatically.
    /// </summary>
    public class MouseInputManager : MonoBehaviour
    {
        private const float StaleZeroTolerance = 0.5f;

        private static MouseInputManager _instance;

        public static MouseInputManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<MouseInputManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[MouseInputManager]");
                        _instance = go.AddComponent<MouseInputManager>();
                    }
                }
                return _instance;
            }
        }

        /// <summary>Fired when mouse position changes by at least 1 world unit.</summary>
        public event Action<Vector2> OnMousePositionChanged;

        /// <summary>Fired when left mouse button is pressed.</summary>
        public event Action<Vector2> OnLeftMouseDown;

        /// <summary>Fired when left mouse button is released.</summary>
        public event Action<Vector2> OnLeftMouseUp;

        /// <summary>Fired when right mouse button is pressed.</summary>
        public event Action<Vector2> OnRightMouseDown;

        /// <summary>Fired when right mouse button is released.</summary>
        public event Action<Vector2> OnRightMouseUp;

        /// <summary>Fired when mouse wheel scrolls.</summary>
        public event Action<float> OnMouseWheelScroll;

        private Vector2 _lastMousePosition = Vector2.zero;
        private bool _lastLeftButtonState;
        private bool _lastRightButtonState;
        private Camera _mainCamera;
        private bool _diagnosticsRun;

        private void Awake()
        {
            if (_instance != this && _instance != null)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            RuntimeInputBootstrap.EnsureRuntimeInput();
        }

        private void Start()
        {
            _mainCamera = Camera.main;
        }

        private void Update()
        {
            Tick();
        }

        public void Tick()
        {
            if (!_diagnosticsRun)
            {
                InputDiagnostics.RunDiagnostics();
                _diagnosticsRun = true;
            }

            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                if (_mainCamera == null) return;
            }

            var mouse = Mouse.current;
            if (mouse == null) return;

            if (!TryGetWorldMousePosition(out Vector2 worldMousePos, _mainCamera, requireInView: false))
                return;

            if (Vector2.Distance(_lastMousePosition, worldMousePos) > 0.01f)
            {
                _lastMousePosition = worldMousePos;
                OnMousePositionChanged?.Invoke(worldMousePos);
            }

            bool leftButtonDown = IsLeftMouseButtonPressed();
            if (leftButtonDown && !_lastLeftButtonState)
            {
                OnLeftMouseDown?.Invoke(worldMousePos);
            }
            else if (!leftButtonDown && _lastLeftButtonState)
            {
                OnLeftMouseUp?.Invoke(worldMousePos);
            }
            _lastLeftButtonState = leftButtonDown;

            bool rightButtonDown = IsRightMouseButtonPressed();
            if (rightButtonDown && !_lastRightButtonState)
            {
                OnRightMouseDown?.Invoke(worldMousePos);
            }
            else if (!rightButtonDown && _lastRightButtonState)
            {
                OnRightMouseUp?.Invoke(worldMousePos);
            }
            _lastRightButtonState = rightButtonDown;

            float scrollDelta = GetMouseWheelDelta();
            if (Mathf.Abs(scrollDelta) > 0.01f)
            {
                OnMouseWheelScroll?.Invoke(scrollDelta);
            }
        }

        /// <summary>
        /// Get current mouse position in screen space.
        /// </summary>
        public static Vector2 GetScreenMousePosition()
        {
            return TryGetScreenMousePosition(out Vector2 position) ? position : Vector2.zero;
        }

        /// <summary>
        /// Get current mouse position in world space.
        /// </summary>
        public static Vector2 GetWorldMousePosition()
        {
            return TryGetWorldMousePosition(out Vector2 position) ? position : Vector2.zero;
        }

        /// <summary>
        /// Try to get current mouse position in screen space.
        /// </summary>
        public static bool TryGetScreenMousePosition(out Vector2 position)
        {
            return TryGetScreenMousePosition(out position, false, null);
        }

        /// <summary>
        /// Try to get current mouse position in screen space, optionally requiring it
        /// to be inside the active camera viewport.
        /// </summary>
        public static bool TryGetScreenMousePosition(out Vector2 position, bool requireInView, Camera camera)
        {
            bool hasInputSystem = TryGetInputSystemScreenMousePosition(out Vector2 inputSystemPos);
            bool hasLegacy = TryGetLegacyScreenMousePosition(out Vector2 legacyPos);
            Rect viewRect = ResolveViewRect(camera);

            return TrySelectScreenMousePosition(
                inputSystemPos,
                hasInputSystem,
                legacyPos,
                hasLegacy,
                viewRect,
                requireInView,
                out position);
        }

        /// <summary>
        /// Try to get current mouse position in world space.
        /// </summary>
        public static bool TryGetWorldMousePosition(
            out Vector2 position,
            Camera camera = null,
            bool requireInView = true,
            bool requireApplicationFocus = false)
        {
            position = Vector2.zero;

            if (camera == null)
                camera = Camera.main;
            if (camera == null)
                return false;

            if (!TryGetScreenMousePosition(out Vector2 screenPos, requireInView, camera))
                return false;

            if (requireInView && requireApplicationFocus)
            {
                if (requireApplicationFocus && Application.isPlaying && !Application.isFocused && !Application.isBatchMode)
                    return false;
            }

            Vector3 worldPos = camera.ScreenToWorldPoint(screenPos);
            position = new Vector2(worldPos.x, worldPos.y);
            return !float.IsNaN(position.x) && !float.IsNaN(position.y);
        }

        public static bool TrySelectScreenMousePosition(
            Vector2 inputSystemPosition,
            bool hasInputSystemPosition,
            Vector2 legacyPosition,
            bool hasLegacyPosition,
            Rect viewRect,
            bool requireInView,
            out Vector2 position)
        {
            position = Vector2.zero;

            bool inputFinite = hasInputSystemPosition && IsFinite(inputSystemPosition);
            bool legacyFinite = hasLegacyPosition && IsFinite(legacyPosition);
            bool staleInputZero = inputFinite && IsStaleInputSystemZero(inputSystemPosition, legacyFinite, legacyPosition);
            bool inputInView = inputFinite && IsInsideView(inputSystemPosition, viewRect);
            bool legacyInView = legacyFinite && IsInsideView(legacyPosition, viewRect);

            if (inputFinite && !staleInputZero && (!requireInView || inputInView))
            {
                position = inputSystemPosition;
                return true;
            }

            if (inputFinite && !staleInputZero && requireInView && !inputInView)
                return false;

            if (legacyFinite && (!requireInView || legacyInView))
            {
                position = legacyPosition;
                return true;
            }

            if (!requireInView && inputFinite)
            {
                position = inputSystemPosition;
                return true;
            }

            return false;
        }

        private static bool TryGetInputSystemScreenMousePosition(out Vector2 position)
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                position = Vector2.zero;
                return false;
            }

            position = mouse.position.ReadValue();
            return IsFinite(position);
        }

        private static bool TryGetLegacyScreenMousePosition(out Vector2 position)
        {
            try
            {
                Vector3 legacy = UnityEngine.Input.mousePosition;
                position = new Vector2(legacy.x, legacy.y);
                return IsFinite(position);
            }
            catch (System.InvalidOperationException)
            {
                position = Vector2.zero;
                return false;
            }
        }

        private static Rect ResolveViewRect(Camera camera)
        {
            if (camera != null && camera.pixelRect.width > 0f && camera.pixelRect.height > 0f)
                return camera.pixelRect;

            return new Rect(0f, 0f, Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
        }

        private static bool IsInsideView(Vector2 position, Rect viewRect)
        {
            return viewRect.width > 0f && viewRect.height > 0f &&
                   position.x >= viewRect.xMin && position.x <= viewRect.xMax &&
                   position.y >= viewRect.yMin && position.y <= viewRect.yMax;
        }

        private static bool IsStaleInputSystemZero(Vector2 inputSystemPosition, bool hasLegacyPosition, Vector2 legacyPosition)
        {
            return inputSystemPosition.sqrMagnitude <= StaleZeroTolerance * StaleZeroTolerance &&
                   hasLegacyPosition &&
                   legacyPosition.sqrMagnitude > StaleZeroTolerance * StaleZeroTolerance;
        }

        private static bool IsFinite(Vector2 value)
        {
            return !float.IsNaN(value.x) && !float.IsNaN(value.y) &&
                   !float.IsInfinity(value.x) && !float.IsInfinity(value.y);
        }

        // ── Mouse-button polling ─────────────────────────────────────────────
        // All button queries OR the new InputSystem result with the legacy
        // UnityEngine.Input backend. Reason: in the Editor (Unity 2022.3.62f1)
        // the new InputSystem package intermittently drops OS event delivery
        // for mouse + keyboard while the legacy backend keeps working — the
        // new device's `wasPressedThisFrame` stays false even though the user
        // just clicked. ORing both backends restores reliable input across
        // all of Valkur (UI, gameplay attack/cast, dash, etc.) without
        // depending on whether the new pipeline currently has events flowing.

        public static bool IsLeftMouseButtonPressed()
        {
            if (InputBlocker.IsGameplayBlocked) return false;
            var mouse = Mouse.current;
            bool newSystem = mouse != null && mouse.leftButton.isPressed;
            return newSystem || UnityEngine.Input.GetMouseButton(0);
        }

        public static bool WasLeftMouseButtonPressedThisFrame()
        {
            if (InputBlocker.IsGameplayBlocked) return false;
            var mouse = Mouse.current;
            bool newSystem = mouse != null && mouse.leftButton.wasPressedThisFrame;
            return newSystem || UnityEngine.Input.GetMouseButtonDown(0);
        }

        public static bool WasLeftMouseButtonReleasedThisFrame()
        {
            if (InputBlocker.IsGameplayBlocked) return false;
            var mouse = Mouse.current;
            bool newSystem = mouse != null && mouse.leftButton.wasReleasedThisFrame;
            return newSystem || UnityEngine.Input.GetMouseButtonUp(0);
        }

        public static bool IsRightMouseButtonPressed()
        {
            if (InputBlocker.IsGameplayBlocked) return false;
            var mouse = Mouse.current;
            bool newSystem = mouse != null && mouse.rightButton.isPressed;
            return newSystem || UnityEngine.Input.GetMouseButton(1);
        }

        public static bool WasRightMouseButtonPressedThisFrame()
        {
            if (InputBlocker.IsGameplayBlocked) return false;
            var mouse = Mouse.current;
            bool newSystem = mouse != null && mouse.rightButton.wasPressedThisFrame;
            return newSystem || UnityEngine.Input.GetMouseButtonDown(1);
        }

        public static bool WasRightMouseButtonReleasedThisFrame()
        {
            if (InputBlocker.IsGameplayBlocked) return false;
            var mouse = Mouse.current;
            bool newSystem = mouse != null && mouse.rightButton.wasReleasedThisFrame;
            return newSystem || UnityEngine.Input.GetMouseButtonUp(1);
        }

        public static bool IsMiddleMouseButtonPressed()
        {
            if (InputBlocker.IsGameplayBlocked) return false;
            var mouse = Mouse.current;
            bool newSystem = mouse != null && mouse.middleButton.isPressed;
            return newSystem || UnityEngine.Input.GetMouseButton(2);
        }

        public static bool WasMiddleMouseButtonPressedThisFrame()
        {
            if (InputBlocker.IsGameplayBlocked) return false;
            var mouse = Mouse.current;
            bool newSystem = mouse != null && mouse.middleButton.wasPressedThisFrame;
            return newSystem || UnityEngine.Input.GetMouseButtonDown(2);
        }

        public static bool WasMiddleMouseButtonReleasedThisFrame()
        {
            if (InputBlocker.IsGameplayBlocked) return false;
            var mouse = Mouse.current;
            bool newSystem = mouse != null && mouse.middleButton.wasReleasedThisFrame;
            return newSystem || UnityEngine.Input.GetMouseButtonUp(2);
        }

        /// <summary>
        /// Get mouse wheel scroll delta. ORs the new InputSystem backend with the
        /// legacy <see cref="UnityEngine.Input"/> backend so the wheel keeps
        /// working when the new package drops OS events (recurring Unity 2022.3
        /// Editor bug). The new backend reports pixels (~±120 per detent) while
        /// the legacy backend reports ticks (~±1 per detent), so we scale the
        /// legacy value ×120 to keep callers' thresholds consistent.
        /// Returns 0 while a modal panel (chat / dev console) holds focus so
        /// the camera does not zoom while the user scrolls the panel.
        /// </summary>
        public static float GetMouseWheelDelta()
        {
            if (InputBlocker.IsGameplayBlocked) return 0f;
            var mouse = Mouse.current;
            float newScroll = mouse != null ? mouse.scroll.ReadValue().y : 0f;
            if (Mathf.Abs(newScroll) >= 0.1f) return newScroll;
            return UnityEngine.Input.mouseScrollDelta.y * 120f;
        }

        /// <summary>
        /// Check if pointer is over any UI element.
        /// </summary>
        public static bool IsPointerOverUI()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return false;
            return eventSystem.IsPointerOverGameObject();
        }

        /// <summary>
        /// Get all entities under mouse cursor using Physics2D raycast.
        /// </summary>
        public static Collider2D[] GetCollidersUnderMouse(LayerMask layerMask, float radius = 0.1f)
        {
            if (!TryGetWorldMousePosition(out Vector2 worldMousePos))
                return Array.Empty<Collider2D>();

            return Physics2D.OverlapCircleAll(worldMousePos, radius, layerMask);
        }

        /// <summary>
        /// Raycast from mouse position in a given direction.
        /// </summary>
        public static RaycastHit2D Raycast(Vector2 direction, float distance, LayerMask layerMask)
        {
            if (!TryGetWorldMousePosition(out Vector2 worldMousePos))
                return default;

            return Physics2D.Raycast(worldMousePos, direction, distance, layerMask);
        }

        /// <summary>
        /// Get the topmost collider under the mouse cursor.
        /// </summary>
        public static Collider2D GetTopmostColliderUnderMouse(LayerMask layerMask, float radius = 0.1f)
        {
            var colliders = GetCollidersUnderMouse(layerMask, radius);
            if (colliders.Length == 0) return null;
            
            // Sort by z position, highest z first.
            System.Array.Sort(colliders, (a, b) => 
                b.gameObject.transform.position.z.CompareTo(a.gameObject.transform.position.z));
            
            return colliders[0];
        }

        /// <summary>
        /// Check if mouse is within a rect in screen space.
        /// </summary>
        public static bool IsMouseInScreenRect(Rect rect)
        {
            if (!TryGetScreenMousePosition(out Vector2 screenMousePos))
                return false;

            return rect.Contains(screenMousePos);
        }

        /// <summary>
        /// Check if mouse is within a rect in world space.
        /// </summary>
        public static bool IsMouseInWorldRect(Bounds2D bounds)
        {
            if (!TryGetWorldMousePosition(out Vector2 worldMousePos))
                return false;

            return bounds.Contains(worldMousePos);
        }

        private static void EnsureInputDevices()
        {
            if (Mouse.current == null)
                InputSystem.AddDevice<Mouse>();

            if (Keyboard.current == null)
                InputSystem.AddDevice<Keyboard>();
        }

        /// <summary>
        /// Simple 2D bounds structure for world space checks.
        /// </summary>
        public struct Bounds2D
        {
            public float xMin, xMax, yMin, yMax;

            public Bounds2D(float xMin, float xMax, float yMin, float yMax)
            {
                this.xMin = xMin;
                this.xMax = xMax;
                this.yMin = yMin;
                this.yMax = yMax;
            }

            public bool Contains(Vector2 point)
            {
                return point.x >= xMin && point.x <= xMax && 
                       point.y >= yMin && point.y <= yMax;
            }
        }
    }
}
