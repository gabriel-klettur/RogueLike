using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// World-space segmented dash charge bar rendered above the health bar.
    /// Mirrors Python's DashBarRenderSystem: cyan segments with gaps, partial-fill recharging.
    /// Player-only. Stacks between health bar and mana bar.
    /// </summary>
    public class WorldDashBar : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField, Tooltip("Spacing above the health bar.")]
        private float stackSpacing = 0.06f;

        [SerializeField, Tooltip("Total bar width in world units (matches health bar).")]
        private float barWidth = 0.8f;

        [SerializeField, Tooltip("Bar height per segment. Python: 4px → 4/16 ≈ 0.07.")]
        private float barHeight = 0.07f;

        [SerializeField, Tooltip("Gap between segments in world units. Python: 2px → 2/16 ≈ 0.03.")]
        private float segmentGap = 0.03f;

        [Header("Colors")]
        [SerializeField] private Color fillColor = new Color(0.157f, 0.784f, 1f, 1f);
        [SerializeField] private Color rechargeColor = new Color(0.47f, 0.863f, 1f, 1f);
        [SerializeField] private Color bgColor = new Color(0.157f, 0.157f, 0.157f, 0.95f);
        [SerializeField] private Color borderColor = new Color(0f, 0f, 0f, 0.9f);

        private DashAbility _dash;
        private Transform _barRoot;

        // Segment rendering
        private SpriteRenderer[] _segBorders;
        private SpriteRenderer[] _segBgs;
        private SpriteRenderer[] _segFills;
        private int _segmentCount = 1;

        private const string SORTING_LAYER = "UI_World";
        private const int SORT_BORDER = 205;
        private const int SORT_BG = 206;
        private const int SORT_FILL = 207;

        private void Awake()
        {
            _dash = GetComponent<DashAbility>();
            if (_dash != null)
                CreateBarVisuals();
        }

        private void CreateBarVisuals()
        {
            _barRoot = new GameObject("DashBar").transform;
            _barRoot.SetParent(transform);

            // Stack above health bar: spriteTop + healthMargin + healthHeight + spacing
            float spriteTopY = WorldHealthBar.GetSpriteTopY(gameObject);
            float healthBarMargin = 0.12f;
            float healthBarH = 0.1f;
            float barY = spriteTopY + healthBarMargin + healthBarH + stackSpacing;

            float parentScaleY = transform.localScale.y;
            if (parentScaleY > 0f && !Mathf.Approximately(parentScaleY, 1f))
            {
                float inv = 1f / parentScaleY;
                _barRoot.localPosition = new Vector3(0f, barY * inv, 0f);
                _barRoot.localScale = new Vector3(inv, inv, 1f);
            }
            else
            {
                _barRoot.localPosition = new Vector3(0f, barY, 0f);
                _barRoot.localScale = Vector3.one;
            }

            // DashAbility is single-charge; use 1 segment
            _segmentCount = 1;
            _segBorders = new SpriteRenderer[_segmentCount];
            _segBgs = new SpriteRenderer[_segmentCount];
            _segFills = new SpriteRenderer[_segmentCount];

            float totalGap = segmentGap * (_segmentCount - 1);
            float segWidth = (_segmentCount > 0)
                ? (barWidth - totalGap) / _segmentCount
                : barWidth;

            for (int i = 0; i < _segmentCount; i++)
            {
                float xPos = -barWidth * 0.5f + segWidth * 0.5f
                    + i * (segWidth + segmentGap);

                float borderPad = 0.04f;

                _segBorders[i] = CreateBarPart($"Seg{i}_Border",
                    new Vector3(segWidth + borderPad, barHeight + borderPad, 1f),
                    new Vector3(xPos, 0f, 0f), borderColor, SORT_BORDER);

                _segBgs[i] = CreateBarPart($"Seg{i}_BG",
                    new Vector3(segWidth, barHeight, 1f),
                    new Vector3(xPos, 0f, 0f), bgColor, SORT_BG);

                _segFills[i] = CreateBarPart($"Seg{i}_Fill",
                    new Vector3(segWidth, barHeight, 1f),
                    new Vector3(xPos, 0f, 0f), fillColor, SORT_FILL);
            }
        }

        private SpriteRenderer CreateBarPart(string name, Vector3 scale, Vector3 localPos,
            Color color, int sortOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_barRoot);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = WorldHealthBar.GetSharedPixelSprite();
            sr.color = color;
            sr.sortingLayerName = SORTING_LAYER;
            sr.sortingOrder = sortOrder;
            sr.material = WorldHealthBar.GetSharedSpriteMaterial();
            return sr;
        }

        private void Update()
        {
            if (_barRoot == null || _dash == null) return;

            _barRoot.rotation = Quaternion.identity;

            float totalGap = segmentGap * (_segmentCount - 1);
            float segWidth = (_segmentCount > 0)
                ? (barWidth - totalGap) / _segmentCount
                : barWidth;

            for (int i = 0; i < _segmentCount; i++)
            {
                if (_segFills[i] == null) continue;

                float xPos = -barWidth * 0.5f + segWidth * 0.5f
                    + i * (segWidth + segmentGap);

                if (_dash.CanDash)
                {
                    // Fully charged
                    _segFills[i].color = fillColor;
                    _segFills[i].transform.localScale = new Vector3(segWidth, barHeight, 1f);
                    _segFills[i].transform.localPosition = new Vector3(xPos, 0f, 0f);
                }
                else
                {
                    // Recharging: show partial fill based on cooldown progress
                    float progress = 1f - (_dash.CooldownRemaining / _dash.CooldownTotal);
                    progress = Mathf.Clamp01(progress);

                    float fillW = segWidth * progress;
                    _segFills[i].color = rechargeColor;
                    _segFills[i].transform.localScale = new Vector3(fillW, barHeight, 1f);

                    // Left-align partial fill within segment
                    float fillOffset = xPos - (segWidth - fillW) * 0.5f;
                    _segFills[i].transform.localPosition = new Vector3(fillOffset, 0f, 0f);
                }
            }
        }
    }
}
