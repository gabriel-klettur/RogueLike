using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI
{
    /// <summary>
    /// Segmented dash-charge meter below player health.
    /// Python: bar_height=4, fill_color=(40,200,255), bg_color=(40,40,40), segment_gap=2.
    /// Shows segments matching DashAbility.maxCharges.
    /// </summary>
    public class DashMeterHUD : MonoBehaviour
    {
        [SerializeField, Tooltip("Reference to the player's DashAbility.")]
        private Valkur.Gameplay.Combat.DashAbility dashAbility;

        [SerializeField, Tooltip("Height of the meter bar in pixels.")]
        private float barHeight = 8f;

        [SerializeField, Tooltip("Total bar width in pixels.")]
        private float barWidth = 160f;

        [SerializeField, Tooltip("Gap between segments in pixels.")]
        private float segmentGap = 2f;

        private Canvas _canvas;
        private RectTransform _panel;
        private Image[] _segments;
        private int _segmentCount;

        // Python: fill_color=(40,200,255)
        private static readonly Color FillColor = new Color(40f/255f, 200f/255f, 255f/255f, 1f);
        private static readonly Color EmptyColor = new Color(40f/255f, 40f/255f, 40f/255f, 0.8f);

        private void Start()
        {
            if (dashAbility == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                    dashAbility = player.GetComponent<Valkur.Gameplay.Combat.DashAbility>();
            }
        }

        private void Update()
        {
            if (dashAbility == null) return;

            // Lazy-build segments when dash ability is ready
            int maxCharges = GetMaxCharges();
            if (_segments == null || _segmentCount != maxCharges)
                BuildSegments(maxCharges);

            UpdateSegments();
        }

        private int GetMaxCharges()
        {
            // DashAbility uses cooldown-based single charge; treat as 1 segment
            return 1;
        }

        private void BuildSegments(int count)
        {
            // Cleanup old
            if (_panel != null) Destroy(_panel.gameObject);

            _segmentCount = count;
            _segments = new Image[count];

            _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null)
            {
                var cGo = new GameObject("DashMeterCanvas");
                _canvas = cGo.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 99;
                cGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                cGo.AddComponent<GraphicRaycaster>();
                transform.SetParent(cGo.transform, false);
            }

            var panelGo = new GameObject("DashMeterPanel");
            panelGo.transform.SetParent(transform, false);
            _panel = panelGo.AddComponent<RectTransform>();
            _panel.anchorMin = new Vector2(0, 1);
            _panel.anchorMax = new Vector2(0, 1);
            _panel.pivot = new Vector2(0, 1);
            _panel.anchoredPosition = new Vector2(16, -80); // Below health bar area
            _panel.sizeDelta = new Vector2(barWidth, barHeight);

            float totalGap = (count - 1) * segmentGap;
            float segWidth = (barWidth - totalGap) / count;

            for (int i = 0; i < count; i++)
            {
                var segGo = new GameObject($"Seg_{i}");
                segGo.transform.SetParent(_panel, false);
                var rt = segGo.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 0);
                rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0, 0.5f);
                rt.anchoredPosition = new Vector2(i * (segWidth + segmentGap), 0);
                rt.sizeDelta = new Vector2(segWidth, 0);

                _segments[i] = segGo.AddComponent<Image>();
            }
        }

        private void UpdateSegments()
        {
            if (_segments == null || dashAbility == null) return;

            bool canDash = dashAbility.CanDash;
            float cdNorm = 1f - Mathf.Clamp01(dashAbility.CooldownRemaining / 1f); // 1s cooldown

            for (int i = 0; i < _segments.Length; i++)
            {
                if (canDash)
                    _segments[i].color = FillColor;
                else
                    _segments[i].color = Color.Lerp(EmptyColor, FillColor, cdNorm);
            }
        }
    }
}
