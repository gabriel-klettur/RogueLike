using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Attaches to any entity with Health. Spawns floating damage numbers on damage events.
    /// </summary>
    public class FloatingDamageSpawner : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private Color damageColor = new Color(1f, 0.3f, 0.3f, 1f);
        [SerializeField] private Color healColor = new Color(0.3f, 1f, 0.3f, 1f);
        [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 0.8f, 0f);

        private Health _health;

        private void Awake()
        {
            _health = GetComponent<Health>();
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
            var go = new GameObject($"DmgNum_{amount}");
            go.transform.position = transform.position + spawnOffset;

            var dmgNum = go.AddComponent<FloatingDamageNumber>();
            dmgNum.Initialize(amount, color);
        }
    }
}
