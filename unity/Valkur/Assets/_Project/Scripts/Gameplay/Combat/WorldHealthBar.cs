using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// World-space health bar rendered above an entity using SpriteRenderers.
    /// Mirrors Python's HealthBarSystem: bar above sprite with smooth fill animation.
    /// Hides when entity is at full HP or dead.
    /// Colors are configurable per-entity via SetBarColors().
    /// </summary>
    public class WorldHealthBar : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField, Tooltip("Extra margin above sprite top edge.")]
        private float marginAboveSprite = 0.12f;
        [SerializeField] private float barWidth = 0.8f;
        [SerializeField] private float barHeight = 0.1f;
        [SerializeField] private float lowThreshold = 0.3f;
        [SerializeField] private bool hideAtFullHp = true;

        [Header("Colors")]
        [SerializeField] private Color fillColor = new Color(0.2f, 0.9f, 0.2f, 1f);
        [SerializeField] private Color lowColor = new Color(0.95f, 0.2f, 0.15f, 1f);
        [SerializeField] private Color bgColor = new Color(0.12f, 0.12f, 0.12f, 0.95f);
        [SerializeField] private Color borderColor = new Color(0f, 0f, 0f, 0.9f);

        private Health _health;
        private Transform _barRoot;
        private SpriteRenderer _borderRenderer;
        private SpriteRenderer _bgRenderer;
        private SpriteRenderer _fillRenderer;
        private float _targetFill = 1f;

        private const string SORTING_LAYER = "UI_World";
        private const int SORT_BORDER = 200;
        private const int SORT_BG = 201;
        private const int SORT_FILL = 202;

        private void Awake()
        {
            _health = GetComponent<Health>();
            CreateBarVisuals();
        }

        /// <summary>
        /// Compute the local-space Y of the sprite's top edge (above pivot).
        /// Works regardless of entity scale.
        /// </summary>
        public static float GetSpriteTopY(GameObject entity)
        {
            var sr = entity.GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                // bounds.max.y is the world-space top of the sprite.
                // Subtract entity position to get local-space distance from pivot (feet) to top.
                float worldTopY = sr.bounds.max.y - entity.transform.position.y;
                float scaleY = entity.transform.localScale.y;
                return scaleY > 0f ? worldTopY / scaleY : worldTopY;
            }
            return 1.4f; // fallback for ~22px tall sprite at PPU 16
        }

        private void OnEnable()
        {
            if (_health != null)
            {
                _health.OnHpChanged += OnHpChanged;
                OnHpChanged(_health.CurrentHp, _health.MaxHp);
            }
        }

        private void OnDisable()
        {
            if (_health != null)
                _health.OnHpChanged -= OnHpChanged;
        }

        /// <summary>
        /// Configure bar colors externally (e.g., green for player, red for NPCs).
        /// </summary>
        public void SetBarColors(Color fill, Color low)
        {
            fillColor = fill;
            lowColor = low;
        }

        /// <summary>
        /// Python always shows the health bar for the player (no hide-at-full).
        /// Monsters keep hideAtFullHp=true to reduce clutter.
        /// </summary>
        public void SetHideAtFullHp(bool hide)
        {
            hideAtFullHp = hide;
            UpdateVisibility();
        }

        private void CreateBarVisuals()
        {
            _barRoot = new GameObject("HealthBar").transform;
            _barRoot.SetParent(transform);

            // Position above sprite top edge (like Python's screen_y - margin)
            float spriteTopY = GetSpriteTopY(gameObject);
            float barY = spriteTopY + marginAboveSprite;

            // Compensate for entity visual scaling so bar stays constant size
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

            // Border (slightly larger than bg)
            _borderRenderer = CreateBarPart("Border",
                new Vector3(barWidth + borderPad, barHeight + borderPad, 1f),
                Vector3.zero, borderColor, SORT_BORDER);

            // Background
            _bgRenderer = CreateBarPart("BG",
                new Vector3(barWidth, barHeight, 1f),
                Vector3.zero, bgColor, SORT_BG);

            // Fill
            _fillRenderer = CreateBarPart("Fill",
                new Vector3(barWidth, barHeight, 1f),
                Vector3.zero, fillColor, SORT_FILL);

            UpdateVisibility();
        }

        private SpriteRenderer CreateBarPart(string name, Vector3 scale, Vector3 localPos,
            Color color, int sortOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_barRoot);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetSharedPixelSprite();
            sr.color = color;
            sr.sortingLayerName = SORTING_LAYER;
            sr.sortingOrder = sortOrder;
            sr.material = GetSharedSpriteMaterial();
            return sr;
        }

        private void Update()
        {
            if (_fillRenderer == null || _barRoot == null) return;

            // Smooth fill animation
            float currentFill = _fillRenderer.transform.localScale.x;
            float newFill = Mathf.Lerp(currentFill, _targetFill * barWidth, Time.deltaTime * 10f);
            _fillRenderer.transform.localScale = new Vector3(newFill, barHeight, 1f);

            // Left-align the fill bar
            float fillOffset = (newFill - barWidth) * 0.5f;
            _fillRenderer.transform.localPosition = new Vector3(fillOffset, 0f, 0f);

            // Color based on HP ratio
            float ratio = barWidth > 0 ? newFill / barWidth : 0f;
            _fillRenderer.color = ratio <= lowThreshold ? lowColor : fillColor;

            // Keep bar facing camera (no rotation from parent)
            _barRoot.rotation = Quaternion.identity;
        }

        private void OnHpChanged(int current, int max)
        {
            _targetFill = max > 0 ? (float)current / max : 0f;
            UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            if (_barRoot == null || _health == null) return;

            bool show = true;
            if (_health.IsDead) show = false;
            if (hideAtFullHp && _health.CurrentHp >= _health.MaxHp) show = false;

            _barRoot.gameObject.SetActive(show);
        }

        private static Sprite _sharedPixelSprite;
        private static Material _sharedMaterial;

        public static Sprite GetSharedPixelSprite()
        {
            if (_sharedPixelSprite != null) return _sharedPixelSprite;

            var tex = new Texture2D(4, 4);
            var pixels = new Color[16];
            for (int i = 0; i < 16; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();
            _sharedPixelSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            return _sharedPixelSprite;
        }

        public static Material GetSharedSpriteMaterial()
        {
            if (_sharedMaterial != null) return _sharedMaterial;
            _sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                ?? Shader.Find("Sprites/Default"));
            return _sharedMaterial;
        }
    }
}
