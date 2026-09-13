using System;
using UnityEngine;
using UnityEngine.UI;

namespace Valkur.Core.UI
{
    /// <summary>
    /// The one black curtain over the screen.
    ///
    /// The world swap behind a door was a hard cut: the base world torn down, the interior
    /// painted, the player teleported, all in one frame the player watched happen. A curtain
    /// that drops for that frame and lifts over a third of a second turns the cut into an
    /// arrival. It lives in Core so the world transition (Gameplay) and any scene flow (UI)
    /// can reach the same curtain — two curtains is one frame of double black and one frame
    /// of neither.
    ///
    /// It is NOT a raycast target: a fade must never swallow the click that caused it. And it
    /// sorts under the loading screen (9999), which owns the one transition that is longer
    /// than a curtain.
    /// </summary>
    public sealed class ScreenFade : MonoBehaviour
    {
        private const int SortingOrder = 9000;

        private static ScreenFade s_instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_instance = null;

        private CanvasGroup _group;
        private float  _alpha;
        private float  _target;
        private float  _rate;
        private Action _onCovered;

        /// <summary>Current curtain opacity, 0..1. Test seam.</summary>
        public float Alpha => _alpha;

        /// <summary>Where the curtain is heading. Test seam.</summary>
        public float Target => _target;

        /// <summary>The live curtain, built on first use.</summary>
        public static ScreenFade Instance
        {
            get
            {
                if (s_instance != null) return s_instance;
                var go = new GameObject("[ScreenFade]");
                s_instance = go.AddComponent<ScreenFade>();
                s_instance.Build();
                if (Application.isPlaying) DontDestroyOnLoad(go);
                return s_instance;
            }
        }

        /// <summary>True when a curtain exists and is not fully lifted.</summary>
        public static bool IsCovering => s_instance != null && s_instance._alpha > 0.001f;

        /// <summary>Tear the curtain down. Tests only; in Play Mode it lives with the session.</summary>
        public static void DestroyForTests()
        {
            if (s_instance != null) DestroyImmediate(s_instance.gameObject);
            s_instance = null;
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            // No CanvasScaler and no GraphicRaycaster: a full-screen stretched image needs no
            // scale, and a curtain that ate clicks would break the door that dropped it.

            var imageGo = new GameObject("Curtain", typeof(RectTransform));
            imageGo.transform.SetParent(transform, false);
            var rt = (RectTransform)imageGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var image = imageGo.AddComponent<Image>();
            image.color         = Color.black;
            image.raycastTarget = false;

            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha          = 0f;
            _group.blocksRaycasts = false;
            _group.interactable   = false;
        }

        /// <summary>
        /// Drop the curtain NOW and lift it over <paramref name="revealSeconds"/>. For a swap
        /// that happens synchronously in the caller's frame: the black covers that frame and
        /// the new world fades in behind it.
        /// </summary>
        public static void CoverAndReveal(float revealSeconds)
        {
            var f = Instance;
            f._alpha  = 1f;
            f._target = 0f;
            f._rate   = 1f / Mathf.Max(0.01f, revealSeconds);
            f._onCovered = null;
            f.Apply();
        }

        /// <summary>Fade to black over <paramref name="seconds"/>, then run <paramref name="onCovered"/>.</summary>
        public static void FadeOut(float seconds, Action onCovered)
        {
            var f = Instance;
            f._target    = 1f;
            f._rate      = 1f / Mathf.Max(0.01f, seconds);
            f._onCovered = onCovered;
        }

        /// <summary>Lift the curtain over <paramref name="seconds"/>.</summary>
        public static void FadeIn(float seconds)
        {
            var f = Instance;
            f._target    = 0f;
            f._rate      = 1f / Mathf.Max(0.01f, seconds);
            f._onCovered = null;
        }

        private void Update() => Tick(Time.unscaledDeltaTime);

        /// <summary>
        /// Advance the curtain. Unscaled time: a fade must not freeze with the hit-stop or
        /// stretch with a slow-motion capture. Public so a test can run one out.
        /// </summary>
        public void Tick(float dt)
        {
            if (Mathf.Approximately(_alpha, _target) && _onCovered == null) return;

            _alpha = Mathf.MoveTowards(_alpha, _target, _rate * dt);
            Apply();

            if (_onCovered != null && _alpha >= 0.999f)
            {
                var cb = _onCovered;
                _onCovered = null;
                cb();
            }
        }

        private void Apply()
        {
            if (_group == null) return;
            _group.alpha = _alpha;
            // An invisible full-screen canvas still costs a draw; switch the object off at rest.
            bool visible = _alpha > 0.001f;
            if (_group.transform.childCount == 0) return;
            var curtain = _group.transform.GetChild(0).gameObject;
            if (curtain.activeSelf != visible) curtain.SetActive(visible);
        }
    }
}
