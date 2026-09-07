using UnityEngine;
using UnityEngine.UI;
using Valkur.Core.Input;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// A ring that opens at the mouse pointer when the player acts — a cast, a chop, a pick
    /// strike. The cursor answering back.
    ///
    /// <para><b>The OS pointer is left completely alone, and that was the decision.</b> The
    /// alternative was hiding it and drawing our own crosshair, which is what lets a cursor
    /// genuinely recoil — and it costs a frame of lag on the one thing the player aims with,
    /// stutters whenever the framerate dips, has to be handed back in sixteen runtime editors,
    /// the inventory's drag-and-drop, the chat and every menu, and would put the documented
    /// InputSystem mouse-freeze bug on screen, where today the OS pointer is ground truth and
    /// always right. A transient effect has none of that: nobody can tell that a ring which
    /// lives a fifth of a second started sixteen milliseconds late.</para>
    ///
    /// <para><b>It FOLLOWS the pointer for its whole life rather than being left where it
    /// spawned.</b> Anchored, it reads as a mark on the ground you clicked; following, it reads
    /// as the cursor itself reacting, which is the thing being asked for. Whipping the mouse
    /// during those five frames smears it, and that is the good version of the behaviour.</para>
    ///
    /// <para>Same grammar as the ground aim marker it fires alongside: white, hard onset,
    /// quadratic decay, and a re-fire RESTARTS rather than stacking — a harvest rhythm has to
    /// read as separate beats and never as a pointer that is permanently lit.</para>
    /// </summary>
    public sealed class CursorImpactFX : MonoBehaviour
    {
        // ── Tuning. Constants in code for the reason FacingIndicatorStyle records: there is
        // exactly one of these, it ships with the game, and a ScriptableObject would be a file
        // nobody opens plus an inspector slot on a component that is AddComponent-ed.
        //
        // SIZES ARE TRUE SCREEN PIXELS. The canvas carries no CanvasScaler on purpose: a cursor
        // accent should be the size the OS pointer is, which does not grow with the render
        // resolution. A ScaleWithScreenSize canvas would make it swell on a 4K display while
        // the pointer it decorates stayed put.

        private const float LIFE_SECONDS = 0.22f;

        /// <summary>Ring diameter at the start and end of its expansion, in screen pixels.
        /// The start is deliberately smaller than a pointer glyph so the ring is seen to come
        /// OUT of the cursor rather than to appear around it.</summary>
        private const float RING_START_PX = 12f;
        private const float RING_END_PX = 54f;
        private const float RING_ALPHA = 0.85f;

        /// <summary>The bright point at the pointer itself. Shorter-lived than the ring, so the
        /// two separate into a flash and a wave instead of reading as one fading blob.</summary>
        private const float CORE_PX = 20f;
        private const float CORE_ALPHA = 0.95f;
        private const float CORE_LIFE_FRACTION = 0.45f;

        /// <summary>Above every other canvas in the project (chat is 100, toasts 200, the
        /// editors sit below that). A cursor accent belongs on top of whatever it is over.</summary>
        private const int CANVAS_SORTING_ORDER = 900;

        private Canvas _canvas;
        private RectTransform _root;
        private Image _ring;
        private Image _core;
        private float _age = float.PositiveInfinity;

        // ── Read by tests ─────────────────────────────────────────────────────────────

        internal Canvas OverlayCanvas => _canvas;
        internal RectTransform Root => _root;
        internal Image RingImage => _ring;
        internal Image CoreImage => _core;
        internal bool IsShowing => _age < LIFE_SECONDS;
        internal float RingDiameterPx => _ring != null ? _ring.rectTransform.sizeDelta.x : 0f;
        internal float RingAlpha => _ring != null ? _ring.color.a : 0f;
        internal float CoreAlpha => _core != null ? _core.color.a : 0f;

        private void Start() => BuildRig();

        private void OnDestroy()
        {
            // The canvas is not a child of whatever owns this component, so nothing else
            // would take it down.
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        /// <summary>
        /// Build the overlay. Internal so an EditMode test can assemble one without Play Mode:
        /// <c>Start</c> never runs on a component added outside it, and a test that only adds
        /// the component would be measuring an object that was never built.
        /// </summary>
        internal void BuildRig()
        {
            if (_canvas != null) return;

            var canvasGo = new GameObject("CursorImpactCanvas");
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = CANVAS_SORTING_ORDER;
            // NO GraphicRaycaster and NO CanvasScaler, and both omissions are load-bearing: a
            // raycaster would put a full-screen click-eater over every panel in the game, and
            // a scaler would break the one-canvas-unit-is-one-screen-pixel identity that lets
            // the pointer position be written straight into `position` below.
            var uiContainer = GameObject.Find("[UI]");
            if (uiContainer != null) canvasGo.transform.SetParent(uiContainer.transform, false);

            var rootGo = new GameObject("CursorImpact");
            _root = rootGo.AddComponent<RectTransform>();
            _root.SetParent(_canvas.transform, worldPositionStays: false);
            _root.sizeDelta = Vector2.zero;

            _ring = MakeLayer("Ring", Spells.ElementalSprites.Ring);
            _core = MakeLayer("Core", Spells.ElementalSprites.Glow);

            Paint(0f);
        }

        private Image MakeLayer(string layerName, Sprite sprite)
        {
            var go = new GameObject(layerName);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(_root, worldPositionStays: false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            // Never a raycast target. An effect that ate clicks would break the very action
            // that spawned it, and it would do it only while it was on screen — an
            // intermittent, un-reproducible input bug is the worst kind this could cause.
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Fire the ring. Pushed in by whoever acted; this class never asks who.</summary>
        internal void Fire() => _age = 0f;

        private void LateUpdate()
        {
            if (_canvas == null) return;
            Tick(Time.deltaTime, MouseInputManager.GetScreenMousePosition());
        }

        /// <summary>
        /// Advance the effect and place it at <paramref name="screenPos"/>. Both are parameters
        /// rather than being read here for the reason the vortex records: a rig that reads the
        /// clock and the mouse itself cannot be measured from a test or a probe.
        /// </summary>
        internal void Tick(float dt, Vector2 screenPos)
        {
            if (_canvas == null) return;
            if (_age >= LIFE_SECONDS)
            {
                // At rest nothing is written at all — not the colours, not the position. A
                // Graphic colour set dirties the canvas batch, and this thing is idle for
                // almost the whole session.
                if (_ring.enabled) SetShown(false);
                return;
            }

            _age += dt;
            if (!_ring.enabled) SetShown(true);

            // For a ScreenSpaceOverlay canvas with no scaler, world space IS screen space, so
            // the pointer position goes straight in.
            _root.position = new Vector3(screenPos.x, screenPos.y, 0f);
            Paint(Envelope01());
        }

        private void SetShown(bool shown)
        {
            _ring.enabled = shown;
            _core.enabled = shown;
        }

        /// <summary>Progress through the life, 0 at the strike and 1 at the end.</summary>
        private float Envelope01() => Mathf.Clamp01(_age / LIFE_SECONDS);

        /// <summary>
        /// Draw the ring and the core at a given progress. The ring EASES OUT — fast at the
        /// strike and slowing as it opens — which is what separates an impact from a bubble
        /// growing at a constant rate.
        /// </summary>
        private void Paint(float t)
        {
            float easedOut = 1f - (1f - t) * (1f - t);
            float diameter = Mathf.Lerp(RING_START_PX, RING_END_PX, easedOut);
            _ring.rectTransform.sizeDelta = new Vector2(diameter, diameter);

            float fade = (1f - t) * (1f - t);
            _ring.color = new Color(1f, 1f, 1f, RING_ALPHA * fade);

            // The core dies first and then stays dead, so the two beats do not blur together.
            float coreT = Mathf.Clamp01(t / CORE_LIFE_FRACTION);
            float coreFade = (1f - coreT) * (1f - coreT);
            _core.rectTransform.sizeDelta = new Vector2(CORE_PX, CORE_PX);
            _core.color = new Color(1f, 1f, 1f, CORE_ALPHA * coreFade);
        }
    }
}
