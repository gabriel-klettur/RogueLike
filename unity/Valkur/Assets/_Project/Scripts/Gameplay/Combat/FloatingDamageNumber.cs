using System;
using UnityEngine;
using TMPro;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// A single floating damage number that rises and fades out.
    /// Supports object pooling: calls OnFinished instead of Destroy when lifetime expires.
    /// Spawned by FloatingDamageSpawner on damage events.
    /// </summary>
    public class FloatingDamageNumber : MonoBehaviour
    {
        [Header("Animation")]
        [SerializeField] private float riseSpeed = 1.5f;
        [SerializeField] private float lifetime = 0.8f;
        [SerializeField] private float spreadRange = 0.3f;

        private TextMeshPro _tmp;
        private float _elapsed;
        private Color _baseColor;
        private Vector3 _velocity;
        private bool _active;

        /// <summary>
        /// Called when the number finishes its animation. Used by the spawner to return to pool.
        /// </summary>
        public event Action<FloatingDamageNumber> OnFinished;

        private void Awake()
        {
            _tmp = GetComponent<TextMeshPro>();
            if (_tmp == null)
                _tmp = gameObject.AddComponent<TextMeshPro>();
        }

        public void Initialize(int amount, Color color)
        {
            if (_tmp == null)
            {
                _tmp = GetComponent<TextMeshPro>();
                if (_tmp == null)
                    _tmp = gameObject.AddComponent<TextMeshPro>();
            }

            _tmp.text = amount.ToString();
            _tmp.fontSize = 4f;
            _tmp.alignment = TextAlignmentOptions.Center;
            _tmp.sortingOrder = 100;
            _baseColor = color;
            _tmp.color = _baseColor;

            float spreadX = UnityEngine.Random.Range(-spreadRange, spreadRange);
            _velocity = new Vector3(spreadX, riseSpeed, 0f);

            _elapsed = 0f;
            _active = true;
            transform.localScale = Vector3.one;
        }

        private void Update()
        {
            if (!_active) return;

            _elapsed += Time.deltaTime;

            float t = _elapsed / lifetime;
            transform.position += _velocity * Time.deltaTime;
            _velocity.y = Mathf.Lerp(riseSpeed, 0f, t);

            float scale = t < 0.15f ? Mathf.Lerp(1.4f, 1f, t / 0.15f) : 1f;
            transform.localScale = Vector3.one * scale;

            if (t > 0.6f)
            {
                float fadeT = (t - 0.6f) / 0.4f;
                Color c = _baseColor;
                c.a = Mathf.Lerp(1f, 0f, fadeT);
                if (_tmp != null)
                    _tmp.color = c;
            }

            if (_elapsed >= lifetime)
            {
                _active = false;
                if (OnFinished != null)
                    OnFinished.Invoke(this);
                else
                    Destroy(gameObject);
            }
        }
    }
}
