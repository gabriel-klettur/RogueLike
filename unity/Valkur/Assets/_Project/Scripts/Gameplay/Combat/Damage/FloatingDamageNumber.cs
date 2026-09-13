using System;
using UnityEngine;
using TMPro;
using Valkur.Core;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// A single floating number that rises and fades out. Pooled: calls <see cref="OnFinished"/>
    /// instead of Destroy when its life ends. Spawned by <see cref="FloatingDamageSpawner"/>.
    ///
    /// Two things separate it from the TMP-at-size-4 it replaced. It carries a SHADOW — a
    /// second label a texel down and right, dark — so a red number over a red building and
    /// a pale one over snow both stay legible; and a critical is a different object: bigger,
    /// gold, and it lands with a punch (2x down to 1x over its first fifth) where a plain hit
    /// only pops.
    ///
    /// Sorting: <c>UI_World</c>, the layer the ambient light leaves alone, at an order
    /// derived from the point it rose from through the same formula the bars use, plus the
    /// bars' whole span — so the number of a blow rises OVER that creature's bar and still
    /// sorts correctly against the bars of creatures behind and in front. A constant order
    /// on that layer is the regression the bars themselves already fixed once.
    /// </summary>
    public class FloatingDamageNumber : MonoBehaviour
    {
        [Header("Animation")]
        [SerializeField] private float riseSpeed = 1.5f;
        [SerializeField] private float lifetime = 0.85f;
        [SerializeField] private float spreadRange = 0.3f;

        /// <summary>Offset of the shadow label, in world units: about one texel down and right.</summary>
        private static readonly Vector3 ShadowOffset = new Vector3(0.05f, -0.05f, 0f);

        /// <summary>Orders above the bar rig's own span, so the number clears every bar piece.</summary>
        private const int OrderAboveBars = 2;

        private TextMeshPro _tmp;
        private TextMeshPro _shadow;
        private float _elapsed;
        private Color _baseColor;
        private Vector3 _velocity;
        private bool _active;
        private bool _critical;
        private float _size;

        /// <summary>Called when the number finishes its animation. The spawner returns it to the pool.</summary>
        public event Action<FloatingDamageNumber> OnFinished;

        /// <summary>Whether the number in flight is a critical. Test seam.</summary>
        public bool IsCritical => _critical;

        /// <summary>The label. Test seam.</summary>
        public TextMeshPro Label { get { EnsureLabels(); return _tmp; } }

        private void Awake() => EnsureLabels();

        private void EnsureLabels()
        {
            if (_tmp == null)
            {
                _tmp = GetComponent<TextMeshPro>();
                if (_tmp == null) _tmp = gameObject.AddComponent<TextMeshPro>();
            }
            if (_shadow == null)
            {
                var existing = transform.Find("Shadow");
                var go = existing != null ? existing.gameObject : new GameObject("Shadow");
                if (existing == null)
                {
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = ShadowOffset;
                }
                _shadow = go.GetComponent<TextMeshPro>();
                if (_shadow == null) _shadow = go.AddComponent<TextMeshPro>();
            }
        }

        public void Initialize(int amount, Color color) => InitializeInternal(amount.ToString(), color, false);

        /// <summary>Custom-string variant (e.g. "+15 XP"). Same animation, only the text differs.</summary>
        public void Initialize(string text, Color color) => InitializeInternal(text, color, false);

        /// <summary>A damage number that knows whether it was a critical.</summary>
        public void Initialize(int amount, Color color, bool critical) => InitializeInternal(amount.ToString(), color, critical);

        private void InitializeInternal(string text, Color color, bool critical)
        {
            EnsureLabels();

            _critical  = critical;
            _size      = DamageNumberPalette.SizeFor(critical);
            _baseColor = color;

            Style(_tmp, text, _size, color);
            Style(_shadow, text, _size, new Color(0f, 0f, 0f, 0.75f));
            _tmp.fontStyle = critical ? FontStyles.Bold : FontStyles.Bold;
            _shadow.fontStyle = _tmp.fontStyle;

            // UI_World, just above the bars of the creature the number rose from.
            int order = SortingConfig.ComputeSortingOrder(SortingConfig.Z_UI, transform.position.y)
                      + WorldBarRig.SORT_SPAN + OrderAboveBars;
            _tmp.sortingLayerID    = SortingLayer.NameToID(SortingConfig.LAYER_UI_WORLD);
            _tmp.sortingOrder      = order;
            _shadow.sortingLayerID = _tmp.sortingLayerID;
            _shadow.sortingOrder   = order - 1;

            float spreadX = UnityEngine.Random.Range(-spreadRange, spreadRange);
            _velocity = new Vector3(spreadX, riseSpeed * (critical ? 0.8f : 1f), 0f);

            _elapsed = 0f;
            _active  = true;
            transform.localScale = Vector3.one * (critical ? 2.0f : 1.4f);
        }

        private static void Style(TextMeshPro t, string text, float size, Color color)
        {
            t.text      = text;
            t.fontSize  = size;
            t.alignment = TextAlignmentOptions.Center;
            t.color     = color;
        }

        private void Update() => Advance(Time.deltaTime);

        /// <summary>Advance the number. Public so a test can run one out.</summary>
        public void Advance(float dt)
        {
            if (!_active) return;

            _elapsed += dt;
            float t = _elapsed / lifetime;

            transform.position += _velocity * dt;
            _velocity.y = Mathf.Lerp(riseSpeed, 0f, t);

            // The punch: a crit lands from 2x, a plain hit pops from 1.4x, both settling by 18 %.
            float from  = _critical ? 2.0f : 1.4f;
            float scale = t < 0.18f ? Mathf.Lerp(from, 1f, t / 0.18f) : 1f;
            transform.localScale = Vector3.one * scale;

            float alpha = t > 0.6f ? Mathf.Lerp(1f, 0f, (t - 0.6f) / 0.4f) : 1f;
            var c = _baseColor; c.a = alpha;
            _tmp.color = c;
            var s = _shadow.color; s.a = 0.75f * alpha;
            _shadow.color = s;

            if (_elapsed >= lifetime)
            {
                _active = false;
                if (OnFinished != null) OnFinished.Invoke(this);
                else Destroy(gameObject);
            }
        }
    }
}
