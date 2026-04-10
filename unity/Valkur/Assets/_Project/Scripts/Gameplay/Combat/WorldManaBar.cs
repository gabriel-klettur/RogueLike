using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// World-space mana bar rendered above the health bar using SpriteRenderers.
    /// Mirrors Python's ManaBarRenderSystem: blue fill, same width as health bar.
    /// Player-only. Stacks above WorldDashBar (or health bar if no dash bar).
    /// </summary>
    public class WorldManaBar : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField, Tooltip("Spacing above the dash bar (or health bar if no dash).")]
        private float stackSpacing = 0.09f;

        [SerializeField, Tooltip("Bar width in world units (matches health bar).")]
        private float barWidth = 0.8f;

        [SerializeField, Tooltip("Bar height in world units. Python: 4px → 4/16 ≈ 0.07.")]
        private float barHeight = 0.07f;

        [Header("Colors")]
        [SerializeField] private Color fillColor = new Color(0.314f, 0.471f, 1f, 1f);
        [SerializeField] private Color bgColor = new Color(0.157f, 0.157f, 0.157f, 0.95f);
        [SerializeField] private Color borderColor = new Color(0f, 0f, 0f, 0.9f);

        [Header("Flash")]
        [SerializeField, Tooltip("Flash when mana is empty.")]
        private bool flashWhenEmpty = true;
        [SerializeField] private Color flashColor = new Color(0.39f, 0.627f, 1f, 1f);
        [SerializeField] private float flashFrequency = 10f;

        private Mana _mana;
        private Transform _barRoot;
        private SpriteRenderer _borderRenderer;
        private SpriteRenderer _bgRenderer;
        private SpriteRenderer _fillRenderer;
        private float _targetFill = 1f;

        private const string SORTING_LAYER = "UI_World";
        private const int SORT_BORDER = 210;
        private const int SORT_BG = 211;
        private const int SORT_FILL = 212;

        private void Awake()
        {
            _mana = GetComponent<Mana>();
            if (_mana != null)
                CreateBarVisuals();
        }

        private void OnEnable()
        {
            if (_mana != null)
            {
                _mana.OnManaChanged += OnManaChanged;
                OnManaChanged(_mana.CurrentMana, _mana.MaxMana);
            }
        }

        private void OnDisable()
        {
            if (_mana != null)
                _mana.OnManaChanged -= OnManaChanged;
        }

        private void CreateBarVisuals()
        {
            _barRoot = new GameObject("ManaBar").transform;
            _barRoot.SetParent(transform);

            // Stack above health bar + dash bar:
            // spriteTop + healthMargin + healthHeight + dashHeight + gap + spacing
            float spriteTopY = WorldHealthBar.GetSpriteTopY(gameObject);
            float healthBarMargin = 0.12f;
            float healthBarH = 0.1f;
            float dashBarH = 0.07f;
            float dashGap = 0.06f;
            float barY = spriteTopY + healthBarMargin + healthBarH + dashGap + dashBarH + stackSpacing;

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

            float borderPad = 0.04f;

            _borderRenderer = CreateBarPart("Border",
                new Vector3(barWidth + borderPad, barHeight + borderPad, 1f),
                Vector3.zero, borderColor, SORT_BORDER);

            _bgRenderer = CreateBarPart("BG",
                new Vector3(barWidth, barHeight, 1f),
                Vector3.zero, bgColor, SORT_BG);

            _fillRenderer = CreateBarPart("Fill",
                new Vector3(barWidth, barHeight, 1f),
                Vector3.zero, fillColor, SORT_FILL);
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
            if (_fillRenderer == null || _barRoot == null) return;

            // Smooth fill animation
            float currentFill = _fillRenderer.transform.localScale.x;
            float newFill = Mathf.Lerp(currentFill, _targetFill * barWidth, Time.deltaTime * 10f);
            _fillRenderer.transform.localScale = new Vector3(newFill, barHeight, 1f);

            // Left-align the fill
            float fillOffset = (newFill - barWidth) * 0.5f;
            _fillRenderer.transform.localPosition = new Vector3(fillOffset, 0f, 0f);

            // Flash when empty (Python parity: sinusoidal alpha at 10 Hz)
            if (flashWhenEmpty && _mana != null && _mana.CurrentMana <= 0)
            {
                float alpha = 0.47f + 0.31f * (0.5f + 0.5f * Mathf.Sin(Time.time * flashFrequency));
                _fillRenderer.color = new Color(flashColor.r, flashColor.g, flashColor.b, alpha);
                _fillRenderer.transform.localScale = new Vector3(barWidth, barHeight, 1f);
                _fillRenderer.transform.localPosition = Vector3.zero;
            }
            else
            {
                _fillRenderer.color = fillColor;
            }

            _barRoot.rotation = Quaternion.identity;
        }

        private void OnManaChanged(int current, int max)
        {
            _targetFill = max > 0 ? (float)current / max : 0f;
        }
    }
}
