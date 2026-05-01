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
        [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 0.8f, 0f);

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

        private void SpawnNumber(int amount, Color color)
        {
            EnsurePool();
            var go = _pool.Get(transform.position + spawnOffset, Quaternion.identity);
            if (go == null) return;

            var dmgNum = go.GetComponent<FloatingDamageNumber>();
            if (dmgNum == null) return;

            dmgNum.OnFinished -= ReturnToPool;
            dmgNum.OnFinished += ReturnToPool;
            dmgNum.Initialize(amount, color);
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
