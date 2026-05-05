using UnityEngine;
using TMPro;
using Valkur.Core;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Attaches to any entity with Health. Spawns floating damage numbers on damage events.
    /// Uses a shared ObjectPool to avoid per-hit allocations.
    /// </summary>
    public class FloatingDamageSpawner : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private Color damageColor = new Color(1f, 0.3f, 0.3f, 1f);
        [SerializeField] private Color healColor = new Color(0.3f, 1f, 0.3f, 1f);
        [SerializeField] private Color xpColor   = new Color(0.4f, 0.95f, 1f, 1f);
        [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 0.8f, 0f);

        /// <summary>Counts numbers spawned this lifetime — test seam.</summary>
        public int SpawnedCount { get; private set; }
        /// <summary>Last text passed to a floating number — test seam.</summary>
        public string LastSpawnedText { get; private set; }

        private Health _health;

        private static ObjectPool _pool;
        private static GameObject _prefab;
        private const int POOL_INITIAL = 8;
        private const int POOL_MAX = 32;

        private void Awake()
        {
            _health = GetComponent<Health>();
            EnsurePool();
        }

        private void OnEnable()
        {
            if (_health != null)
                _health.OnDamaged += OnDamaged;
        }

        private void OnDisable()
        {
            if (_health != null)
                _health.OnDamaged -= OnDamaged;
        }

        private void OnDamaged(int amount)
        {
            SpawnNumber(amount, damageColor);
        }

        public void ShowHeal(int amount)
        {
            SpawnNumber(amount, healColor);
        }

        /// <summary>
        /// Floating "+N XP" feedback above the entity. Used by
        /// <see cref="XpFeedbackSystem"/> when XP is gained.
        /// </summary>
        public void ShowXp(int amount)
        {
            if (amount == 0) return;
            SpawnText($"+{amount} XP", xpColor);
        }

        private void SpawnNumber(int amount, Color color)
        {
            SpawnText(amount.ToString(), color, amount);
        }

        private void SpawnText(string text, Color color, int? numericFallback = null)
        {
            EnsurePool();
            var go = _pool.Get(transform.position + spawnOffset, Quaternion.identity);
            if (go == null) return;

            var dmgNum = go.GetComponent<FloatingDamageNumber>();
            if (dmgNum == null) return;

            dmgNum.OnFinished -= ReturnToPool;
            dmgNum.OnFinished += ReturnToPool;

            if (numericFallback.HasValue)
                dmgNum.Initialize(numericFallback.Value, color);
            else
                dmgNum.Initialize(text, color);

            SpawnedCount++;
            LastSpawnedText = text;
        }

        private static void ReturnToPool(FloatingDamageNumber num)
        {
            if (num == null) return;
            num.OnFinished -= ReturnToPool;
            if (_pool != null)
                _pool.Return(num.gameObject);
            else
                num.gameObject.SetActive(false);
        }

        private static void EnsurePool()
        {
            // _prefab is a runtime-created GameObject. If the scene was reloaded it will
            // have been destroyed (Unity null-check catches this). Reset both statics so
            // a fresh pool and prefab are created for the new scene.
            if (_prefab == null)
                _pool = null;

            if (_pool != null) return;

            _prefab = new GameObject("DmgNumPrefab");
            _prefab.AddComponent<TextMeshPro>();
            _prefab.AddComponent<FloatingDamageNumber>();
            _prefab.SetActive(false);

            _pool = new ObjectPool(_prefab, POOL_INITIAL, null, POOL_MAX);
        }
    }
}
