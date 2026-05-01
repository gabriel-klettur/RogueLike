using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// World-space coin pickup. Auto-collects when the player enters its trigger.
    /// Maps Python's CoinComponent + MapLoadDropsSystem coin spawning.
    ///
    /// Attach to a GameObject with CircleCollider2D (isTrigger=true) + SpriteRenderer.
    /// Call Initialize() to configure at runtime, or set fields in Inspector.
    /// </summary>
    [RequireComponent(typeof(CircleCollider2D))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class CoinPickup : MonoBehaviour
    {
        [Header("Value")]
        [SerializeField, Tooltip("Coin amount awarded when picked up.")]
        private int amount = 1;

        [Header("Magnet")]
        [SerializeField, Tooltip("If >0, pulls toward player within this radius (world units).")]
        private float magnetRadius = 2f;
        [SerializeField, Tooltip("Speed at which the coin slides toward the player.")]
        private float magnetSpeed  = 6f;

        [Header("Visual")]
        [SerializeField] private float bobAmplitude = 0.04f;
        [SerializeField] private float bobFrequency = 3f;
        [SerializeField] private float spawnDelay   = 0.3f;  // seconds before pickup is active

        // ── Internal ──────────────────────────────────────────────────
        private float   _spawnTime;
        private float   _baseY;
        private bool    _collected;
        private Transform _player;

        // ── Public API ────────────────────────────────────────────────

        public void Initialize(int coinAmount, Vector3 position)
        {
            amount = coinAmount;
            transform.position = position;
            _baseY      = position.y;
            _spawnTime  = Time.time;
            gameObject.name = $"Coin_{coinAmount}";
        }

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            _spawnTime = Time.time;
            _baseY     = transform.position.y;

            var col = GetComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius    = 0.35f;
        }

        private void Start()
        {
            var playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo != null) _player = playerGo.transform;
        }

        private void Update()
        {
            if (_collected) return;

            // Bob animation
            float bob = Mathf.Sin((Time.time - _spawnTime) * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
            var pos = transform.position;
            pos.y = _baseY + bob;
            transform.position = pos;

            // Magnet pull toward player
            if (magnetRadius > 0f && _player != null && Time.time > _spawnTime + spawnDelay)
            {
                float dist = Vector2.Distance(transform.position, _player.position);
                if (dist <= magnetRadius)
                {
                    Vector3 dir = (_player.position - transform.position).normalized;
                    float speed = Mathf.Lerp(magnetSpeed * 0.5f, magnetSpeed, 1f - dist / magnetRadius);
                    transform.position += dir * speed * Time.deltaTime;
                }
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_collected) return;
            if (Time.time < _spawnTime + spawnDelay) return;
            if (!other.CompareTag("Player")) return;

            var wallet = other.GetComponent<CurrencyWallet>();
            if (wallet == null)
            {
                Debug.LogWarning($"[CoinPickup] Player has no CurrencyWallet component.");
                return;
            }

            Collect(wallet);
        }

        private void Collect(CurrencyWallet wallet)
        {
            _collected = true;
            wallet.Add(amount);

            // Spawn a small impact flash (re-use existing VFX manager)
            if (VFXManager.HasInstance)
                VFXManager.Instance.SpawnImpact(transform.position, new Color(1f, 0.92f, 0.2f, 1f), 0.25f, 0.6f);

            Destroy(gameObject);
        }
    }
}
