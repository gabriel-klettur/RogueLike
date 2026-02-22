using UnityEngine;
using TMPro;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// A single floating damage number that rises and fades out.
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

        public void Initialize(int amount, Color color)
        {
            _tmp = GetComponent<TextMeshPro>();
            if (_tmp == null)
                _tmp = gameObject.AddComponent<TextMeshPro>();

            _tmp.text = amount.ToString();
            _tmp.fontSize = 4f;
            _tmp.alignment = TextAlignmentOptions.Center;
            _tmp.sortingOrder = 100;
            _baseColor = color;
            _tmp.color = _baseColor;

            // Randomize horizontal spread for visual variety
            float spreadX = Random.Range(-spreadRange, spreadRange);
            _velocity = new Vector3(spreadX, riseSpeed, 0f);

            _elapsed = 0f;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;

            // Rise and slow down
            float t = _elapsed / lifetime;
            transform.position += _velocity * Time.deltaTime;
            _velocity.y = Mathf.Lerp(riseSpeed, 0f, t);

            // Scale pop effect: start big, settle to normal
            float scale = t < 0.15f ? Mathf.Lerp(1.4f, 1f, t / 0.15f) : 1f;
            transform.localScale = Vector3.one * scale;

            // Fade out in the last 40% of lifetime
            if (t > 0.6f)
            {
                float fadeT = (t - 0.6f) / 0.4f;
                Color c = _baseColor;
                c.a = Mathf.Lerp(1f, 0f, fadeT);
                if (_tmp != null)
                    _tmp.color = c;
            }

            if (_elapsed >= lifetime)
                Destroy(gameObject);
        }
    }
}
