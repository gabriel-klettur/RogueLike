using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Ephemeral toast messages in the bottom-right of the screen.
    /// Mirrors Python's ToastSystem + ToastRenderSystem.
    /// Max 2 visible, auto-dismiss after duration.
    /// </summary>
    public class ToastSystem : MonoBehaviour
    {
        [SerializeField, Tooltip("Maximum visible toasts at once.")]
        private int maxVisible = 2;

        [SerializeField, Tooltip("Default display duration in seconds.")]
        private float defaultDuration = 3f;

        [SerializeField, Tooltip("Font size for toast text.")]
        private int fontSize = 18;

        [SerializeField, Tooltip("Margin between toasts in pixels.")]
        private float margin = 12f;

        [SerializeField, Tooltip("Edge inset from screen border.")]
        private float edgeInset = 16f;

        private Canvas _canvas;
        private RectTransform _container;
        private readonly List<ToastEntry> _active = new List<ToastEntry>();

        private static ToastSystem _instance;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            BuildUI();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>Show a toast message globally.</summary>
        public static void Show(string message, float duration = 0f)
        {
            if (_instance == null) return;
            _instance.EnqueueToast(message, duration <= 0f ? _instance.defaultDuration : duration);
        }

        private void Update()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                _active[i].remaining -= Time.unscaledDeltaTime;
                if (_active[i].remaining <= 0f)
                {
                    Destroy(_active[i].go);
                    _active.RemoveAt(i);
                }
                else
                {
                    // Fade out in last 0.5s
                    float alpha = Mathf.Clamp01(_active[i].remaining / 0.5f);
                    var cg = _active[i].canvasGroup;
                    if (cg != null) cg.alpha = alpha;
                }
            }

            LayoutToasts();
        }

        private void EnqueueToast(string message, float duration)
        {
            // Remove oldest if exceeding max
            while (_active.Count >= maxVisible)
            {
                Destroy(_active[0].go);
                _active.RemoveAt(0);
            }

            var go = new GameObject("Toast");
            go.transform.SetParent(_container, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(1, 0);

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 180f / 255f); // Python: (0,0,0,180)

            var cg = go.AddComponent<CanvasGroup>();

            // Text child
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = new Vector2(-16, -8);
            textRt.anchoredPosition = Vector2.zero;

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = message;
            tmp.fontSize = fontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.enableWordWrapping = true;

            // Size to fit text
            float preferredW = tmp.GetPreferredValues(message).x + 24;
            float preferredH = tmp.GetPreferredValues(message, 400, 0).y + 16;
            rt.sizeDelta = new Vector2(Mathf.Min(preferredW, 400), Mathf.Max(preferredH, 36));

            _active.Add(new ToastEntry { go = go, rt = rt, canvasGroup = cg, remaining = duration });
            LayoutToasts();
        }

        private void LayoutToasts()
        {
            float y = edgeInset;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                _active[i].rt.anchoredPosition = new Vector2(-edgeInset, y);
                y += _active[i].rt.sizeDelta.y + margin;
            }
        }

        private void BuildUI()
        {
            _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null)
            {
                var canvasGo = new GameObject("ToastCanvas");
                _canvas = canvasGo.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 200;
                canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                canvasGo.AddComponent<GraphicRaycaster>();
                transform.SetParent(canvasGo.transform, false);
            }

            var containerGo = new GameObject("ToastContainer");
            containerGo.transform.SetParent(transform, false);
            _container = containerGo.AddComponent<RectTransform>();
            _container.anchorMin = Vector2.zero;
            _container.anchorMax = Vector2.one;
            _container.sizeDelta = Vector2.zero;
        }

        private class ToastEntry
        {
            public GameObject go;
            public RectTransform rt;
            public CanvasGroup canvasGroup;
            public float remaining;
        }
    }
}
