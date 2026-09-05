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

        // ── Test override ────────────────────────────────────────────────────
        // EditMode tests inject a synthetic Mouse via InputSystem.QueueStateEvent
        // and expect PlayerController / MouseTargetDetector / friends to read
        // exactly that position. The production OR-gate (InputSystem ∨ legacy
        // UnityEngine.Input.mousePosition) is great for surviving the recurring
        // Unity 2022.3 InputSystem-drops-events bug but is fragile in tests:
        //   • The Editor's Game view Camera.pixelRect can be invalid when the
        //     window is unfocused, so requireInView gates fail.
        //   • UnityEngine.Input.mousePosition reports the OS cursor (anywhere),
        //     not the synthetic queue state, which can override or clash.
        // Tests set _testOverridePosition to bypass both fallbacks deterministically.
        private static Vector2? _testOverridePosition;

        /// <summary>
        /// Watches the InputSystem pointer against the legacy one so a device that has
        /// stopped delivering events loses its priority. See <see cref="MouseFreezeTracker"/>
        /// for the failure it exists for: a freeze at a NON-ZERO position, which the
        /// stale-zero guard below cannot see.
        /// </summary>
        private static MouseFreezeTracker _freezeTracker = new MouseFreezeTracker();

        /// <summary>Test seam: forget any freeze verdict a previous fixture built up.</summary>
        internal static void ResetFreezeTracking() => _freezeTracker.Reset();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetFreezeTrackerStatic()
        {
            _freezeTracker = new MouseFreezeTracker();
        }

        /// <summary>Editor/test seam: force a deterministic screen-space mouse
        /// position. Pass null to clear (production behaviour). Always pair a
        /// SetTestMousePosition(...) call with a SetTestMousePosition(null) in
        /// [TearDown] so subsequent fixtures don't see leaked state.</summary>
        public static void SetTestMousePosition(Vector2? screenPosition)
        {
            _testOverridePosition = screenPosition;
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
            if (_testOverridePosition.HasValue)
            {
                position = _testOverridePosition.Value;
                if (!IsFinite(position)) return false;
                // Honour requireInView even on the test path: tests like
                // MovingPlayer_MouseOutsideViewport_FallsBackToMovementWalkSprite
                // intentionally place the override outside the camera rect to
                // verify the production fallback to movement direction. If we
                // unconditionally returned true here that fallback would never
                // trigger.
                if (requireInView)
                {
                    Rect overrideViewRect = ResolveViewRect(camera);
                    if (!IsInsideView(position, overrideViewRect)) return false;
                }
                return true;
            }

            bool hasInputSystem = TryGetInputSystemScreenMousePosition(out Vector2 inputSystemPos);
            bool hasLegacy = TryGetLegacyScreenMousePosition(out Vector2 legacyPos);
            Rect viewRect = ResolveViewRect(camera);

            bool inputSystemFrozen = _freezeTracker.Observe(
                Time.frameCount, inputSystemPos, hasInputSystem, legacyPos, hasLegacy);

            return TrySelectScreenMousePosition(
                inputSystemPos,
                hasInputSystem,
                legacyPos,
                hasLegacy,
                viewRect,
                requireInView,
                inputSystemFrozen,
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
            => TrySelectScreenMousePosition(inputSystemPosition, hasInputSystemPosition,
                                            legacyPosition, hasLegacyPosition, viewRect,
                                            requireInView, inputSystemFrozen: false,
                                            out position);

        /// <summary>
        /// Pick the pointer position to trust this frame.
        ///
        /// <para>The InputSystem wins whenever it is credible. It stops being credible in two
        /// shapes, and both hand the answer to the legacy backend: a stale <c>(0,0)</c> while
        /// legacy reads something real (per frame, no history needed), and a FROZEN position
        /// — any value at all that has not moved while the legacy pointer has
        /// (<paramref name="inputSystemFrozen"/>, decided by <see cref="MouseFreezeTracker"/>).
        /// The second is the one that shipped: a device frozen at the screen centre aimed
        /// every spell at the player's own feet.</para>
        ///
        /// <para>Distrust never invents a position. A distrusted InputSystem with the legacy
        /// pointer out of view still answers false under <paramref name="requireInView"/>,
        /// which is what stops the player snapping to face a corner of the screen.</para>
        /// </summary>
        public static bool TrySelectScreenMousePosition(
            Vector2 inputSystemPosition,
            bool hasInputSystemPosition,
            Vector2 legacyPosition,
            bool hasLegacyPosition,
            Rect viewRect,
            bool requireInView,
            bool inputSystemFrozen,
            out Vector2 position)
        {
            position = Vector2.zero;

            bool inputFinite = hasInputSystemPosition && IsFinite(inputSystemPosition);
            bool legacyFinite = hasLegacyPosition && IsFinite(legacyPosition);
            bool staleInputZero = inputFinite && IsStaleInputSystemZero(inputSystemPosition, legacyFinite, legacyPosition);
            // A freeze only means anything while there is a live legacy reading to prefer.
            bool distrustInput = staleInputZero || (inputSystemFrozen && legacyFinite);
            bool inputInView = inputFinite && IsInsideView(inputSystemPosition, viewRect);
            bool legacyInView = legacyFinite && IsInsideView(legacyPosition, viewRect);

            if (inputFinite && !distrustInput && (!requireInView || inputInView))
            {
                position = inputSystemPosition;
                return true;
            }

            if (inputFinite && !distrustInput && requireInView && !inputInView)
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
