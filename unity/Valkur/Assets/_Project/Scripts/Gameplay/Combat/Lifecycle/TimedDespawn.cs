using UnityEngine;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Destroys the GameObject after a configurable time-to-live (TTL).
    /// Maps to Python's TimedDespawnSystem + TimedDespawn component.
    /// Used for corpses, projectile debris, VFX leftovers, ground drops, etc.
    /// </summary>
    public class TimedDespawn : MonoBehaviour
    {
        [SerializeField, Tooltip("Seconds before this object is destroyed. 0 = never.")]
        private float ttl = 10f;

        private float _spawnTime;

        public float TTL { get => ttl; set => ttl = value; }
        public float Elapsed => Time.time - _spawnTime;
        public float Remaining => Mathf.Max(0f, ttl - Elapsed);

        private void OnEnable()
        {
            _spawnTime = Time.time;
        }

        private void Update()
        {
            if (ttl <= 0f) return;
            if (Time.time - _spawnTime >= ttl)
            {
                Destroy(gameObject);
            }
        }
    }
}
