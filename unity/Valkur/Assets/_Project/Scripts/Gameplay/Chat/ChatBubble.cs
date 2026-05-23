using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// Floating chat bubble above an entity. Shows text that fades after TTL.
    /// Maps to Python's FloatingChatBubbleComponent + ChatBubble.
    ///
    /// Python constants preserved:
    ///   Default TTL: 2500ms
    ///   Player message TTL: 2800ms
    ///   NPC reply TTL: 2600ms
    ///   Cancel TTL: 2000ms
    /// </summary>
    public class ChatBubble : MonoBehaviour
    {
        [SerializeField, Tooltip("Vertical offset above entity pivot.")]
        private float _yOffset = 1.5f;

        [SerializeField, Tooltip("Max width in world units before wrapping.")]
        private float _maxWidth = 3f;

        private readonly List<BubbleEntry> _bubbles = new List<BubbleEntry>();
        private Transform _target;
        private Canvas _canvas;
        private RectTransform _canvasRect;
        // Camera.main walks the tagged-camera index on every access. Caching
        // once in Initialize avoids two per-frame lookups inside LateUpdate.
        private Transform _camTransform;

        private struct BubbleEntry
        {
            public GameObject go;
            public TextMeshProUGUI text;
            public CanvasGroup canvasGroup;
            public float expireTime;
            public float fadeStart;
        }

        public void Initialize(Transform target)
        {
            _target = target;

            // World-space canvas for the bubble
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingLayerName = "Overhead";
            _canvas.sortingOrder = 100;

            _canvasRect = GetComponent<RectTransform>();
            _canvasRect.sizeDelta = new Vector2(_maxWidth, 1f);
            _canvasRect.localScale = Vector3.one * 0.02f; // Scale to world units
        }

        /// <summary>
        /// Push a new bubble message.
        /// </summary>
        /// <param name="message">Text to display.</param>
        /// <param name="ttlMs">Time-to-live in milliseconds. Python defaults: NPC=2600, Player=2800.</param>
        /// <param name="color">Text color.</param>
        /// <param name="bgColor">Background color.</param>
        public void PushBubble(string message, int ttlMs = 2500, Color? color = null, Color? bgColor = null)
        {
            Color textColor = color ?? Color.white;
            Color bg = bgColor ?? new Color(0.08f, 0.08f, 0.08f, 0.85f);

            var bubbleGo = new GameObject("Bubble");
            bubbleGo.transform.SetParent(_canvasRect, false);

            var rt = bubbleGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);

            // Background
            var bgImage = bubbleGo.AddComponent<UnityEngine.UI.Image>();
            bgImage.color = bg;

            // Text
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(rt, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(4f, 2f);
            textRt.offsetMax = new Vector2(-4f, -2f);

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = message;
            tmp.fontSize = 14f;
            tmp.color = textColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Overflow;

            var cg = bubbleGo.AddComponent<CanvasGroup>();
            float ttlSec = ttlMs / 1000f;

            // Stack the bubble above existing ones
            float yPos = 0f;
            foreach (var existing in _bubbles)
            {
                if (existing.go != null)
                    yPos += existing.go.GetComponent<RectTransform>().sizeDelta.y + 5f;
            }
            rt.anchoredPosition = new Vector2(0f, yPos);

            // Size to fit text
            var preferredSize = tmp.GetPreferredValues(message, _maxWidth / 0.02f, 0);
            rt.sizeDelta = new Vector2(preferredSize.x + 12f, preferredSize.y + 8f);

            _bubbles.Add(new BubbleEntry
            {
                go = bubbleGo,
                text = tmp,
                canvasGroup = cg,
                expireTime = Time.time + ttlSec,
                fadeStart = Time.time + ttlSec * 0.7f,
            });
        }

        private void LateUpdate()
        {
            // Follow target
            if (_target != null)
                transform.position = _target.position + Vector3.up * _yOffset;

            // Face camera. Cache Camera.main lazily — its property accessor
            // walks the tag index on every call, and we'd otherwise hit it
            // twice per LateUpdate per chat bubble.
            if (_camTransform == null)
            {
                var c = Camera.main;
                if (c != null) _camTransform = c.transform;
            }
            if (_camTransform != null)
                transform.forward = _camTransform.forward;

            // Expire and fade bubbles
            for (int i = _bubbles.Count - 1; i >= 0; i--)
            {
                var b = _bubbles[i];
                if (b.go == null) { _bubbles.RemoveAt(i); continue; }

                if (Time.time >= b.expireTime)
                {
                    Destroy(b.go);
                    _bubbles.RemoveAt(i);
                    continue;
                }

                if (Time.time >= b.fadeStart)
                {
                    float t = (Time.time - b.fadeStart) / (b.expireTime - b.fadeStart);
                    b.canvasGroup.alpha = 1f - t;
                }
            }
        }

        private void OnDestroy()
        {
            foreach (var b in _bubbles)
                if (b.go != null) Destroy(b.go);
            _bubbles.Clear();
        }
    }
}
