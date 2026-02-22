using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// World-space health bar rendered above an entity using SpriteRenderers.
    /// Mirrors Python's HealthBarSystem: bar above sprite, segmented every 20 HP.
    /// Hides when entity is at full HP or dead.
    /// </summary>
    public class WorldHealthBar : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private Vector3 offset = new Vector3(0f, 0.7f, 0f);
        [SerializeField] private float barWidth = 0.8f;
        [SerializeField] private float barHeight = 0.08f;
        [SerializeField] private Color fillColor = new Color(0.1f, 0.9f, 0.1f, 1f);
        [SerializeField] private Color lowColor = new Color(0.9f, 0.2f, 0.1f, 1f);
        [SerializeField] private Color bgColor = new Color(0.2f, 0.2f, 0.2f, 0.85f);
        [SerializeField] private float lowThreshold = 0.3f;
        [SerializeField] private bool hideAtFullHp = true;

        private Health _health;
        private Transform _barRoot;
        private SpriteRenderer _bgRenderer;
        private SpriteRenderer _fillRenderer;
        private float _targetFill = 1f;

        private void Awake()
        {
            _health = GetComponent<Health>();
            CreateBarVisuals();
        }

        private void OnEnable()
        {
            if (_health != null)
                _health.OnHpChanged += OnHpChanged;
        }

        private void OnDisable()
        {
            if (_health != null)
                _health.OnHpChanged -= OnHpChanged;
        }

        private void CreateBarVisuals()
        {
            // Create a root object that follows the entity
            _barRoot = new GameObject("HealthBar").transform;
            _barRoot.SetParent(transform);
            _barRoot.localPosition = offset;
            _barRoot.localScale = Vector3.one;

            // Background
            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(_barRoot);
            bgGo.transform.localPosition = Vector3.zero;
            bgGo.transform.localScale = new Vector3(barWidth, barHeight, 1f);
            _bgRenderer = bgGo.AddComponent<SpriteRenderer>();
            _bgRenderer.sprite = CreatePixelSprite();
            _bgRenderer.color = bgColor;
            _bgRenderer.sortingOrder = 90;

            // Fill
            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(_barRoot);
            fillGo.transform.localPosition = Vector3.zero;
            fillGo.transform.localScale = new Vector3(barWidth, barHeight, 1f);
            _fillRenderer = fillGo.AddComponent<SpriteRenderer>();
            _fillRenderer.sprite = CreatePixelSprite();
            _fillRenderer.color = fillColor;
            _fillRenderer.sortingOrder = 91;

            UpdateVisibility();
        }

        private void Update()
        {
            if (_fillRenderer == null || _barRoot == null) return;

            // Smooth fill animation
            float currentFill = _fillRenderer.transform.localScale.x;
            float newFill = Mathf.Lerp(currentFill, _targetFill * barWidth, Time.deltaTime * 10f);
            _fillRenderer.transform.localScale = new Vector3(newFill, barHeight, 1f);

            // Offset fill to left-align
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

        private static Texture2D _sharedPixelTex;
        private static Sprite _sharedPixelSprite;

        private static Sprite CreatePixelSprite()
        {
            if (_sharedPixelSprite != null) return _sharedPixelSprite;

            _sharedPixelTex = new Texture2D(1, 1);
            _sharedPixelTex.SetPixel(0, 0, Color.white);
            _sharedPixelTex.Apply();
            _sharedPixelSprite = Sprite.Create(_sharedPixelTex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            return _sharedPixelSprite;
        }
    }
}
