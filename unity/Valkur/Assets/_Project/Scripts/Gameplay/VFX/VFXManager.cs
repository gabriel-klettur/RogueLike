using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Central manager for visual effects (impact, slash arc, area indicator, etc.).
    /// Maps to Python's ParticleSystem + ParticleRenderSystem + particle presets.
    /// 
    /// Uses ObjectPool for each VFX type to avoid runtime allocation.
    /// Singleton — created by GameplaySceneSetup or accessed via Instance.
    /// </summary>
    public class VFXManager : SingletonMonoBehaviour<VFXManager>, IVFXService
    {
        [Header("Pool Settings")]
        [SerializeField] private int poolSizePerType = 20;

        private readonly Dictionary<string, ObjectPool> _pools = new Dictionary<string, ObjectPool>();
        private Transform _poolParent;
        private ParticlePresetCatalog _particleCatalog;

        protected override void OnSingletonAwake()
        {
            _poolParent = new GameObject("VFXPools").transform;
            _poolParent.SetParent(transform);
            ServiceLocator.Register<IVFXService>(this);
        }

        /// <summary>
        /// Register a prefab for pooling under a given key.
        /// Call during initialization for each VFX type.
        /// </summary>
        public void RegisterPrefab(string key, GameObject prefab, int warmCount = 0)
        {
            if (_pools.ContainsKey(key)) return;

            var parent = new GameObject($"Pool_{key}").transform;
            parent.SetParent(_poolParent);

            int size = warmCount > 0 ? warmCount : poolSizePerType;
            _pools[key] = new ObjectPool(prefab, size, parent);
        }

        /// <summary>
        /// Provide the particle preset catalog for SpawnParticlePreset.
        /// Called by GameplaySceneSetup after creating the manager.
        /// </summary>
        public void SetParticleCatalog(ParticlePresetCatalog catalog) => _particleCatalog = catalog;

        /// <summary>
        /// Spawn a one-shot particle effect from a preset at world position.
        /// duration &lt; 0  → auto-destroy after preset lifespan + 1 s.
        /// Maps to Python's gameplay emitter systems (dash, fireball trail, healing aura, etc.)
        /// called from combat/spell MonoBehaviours.
        /// </summary>
        public void SpawnParticlePreset(string presetId, Vector3 position, float duration = -1f, float scale = 1f)
        {
            if (_particleCatalog == null)
            {
                Debug.LogWarning("[VFXManager] No particle catalog set — call SetParticleCatalog() first.");
                return;
            }

            var preset = _particleCatalog.GetById(presetId);
            if (preset == null)
            {
                Debug.LogWarning($"[VFXManager] Particle preset '{presetId}' not found in catalog.");
                return;
            }

            var go = new GameObject($"ParticleEffect_{presetId}");
            go.transform.position = position;
            go.transform.SetParent(_poolParent, true);

            var emitter = go.AddComponent<ParticleEmitter>();
            emitter.ApplyPreset(preset, scale);

            float destroyAfter = duration > 0f
                ? duration
                : (preset.vfx.lifespan > 0f ? preset.vfx.lifespan + 1f : 5f);
            Destroy(go, destroyAfter);
        }

        /// <summary>
        /// Spawn a VFX instance from the pool at the given position.
        /// Returns the GameObject for further configuration, or null if pool exhausted.
        /// </summary>
        public GameObject Spawn(string key, Vector3 position, Quaternion rotation)
        {
            if (!_pools.TryGetValue(key, out var pool))
            {
                Debug.LogWarning($"[VFXManager] No pool registered for key '{key}'.");
                return null;
            }

            return pool.Get(position, rotation);
        }

        /// <summary>
        /// Return a VFX instance to its pool.
        /// </summary>
        public void Despawn(string key, GameObject obj)
        {
            if (_pools.TryGetValue(key, out var pool))
                pool.Return(obj);
            else
                Destroy(obj);
        }

        /// <summary>
        /// Spawn an impact effect at the given position with auto-despawn.
        /// </summary>
        public void SpawnImpact(Vector3 position, Color color, float duration = 0.3f, float scale = 1f)
        {
            var go = SpawnOrCreateSimpleVFX("impact", position);
            if (go == null) return;

            var vfx = go.GetComponent<SimpleVFX>();
            if (vfx == null) vfx = go.AddComponent<SimpleVFX>();
            vfx.Play(color, duration, scale, "impact", this);
        }

        /// <summary>
        /// Spawn a slash arc effect at the given position facing a direction.
        /// </summary>
        public void SpawnSlashArc(Vector3 position, Vector2 direction, Color color, float arc = 90f, float radius = 1.5f, float duration = 0.2f)
        {
            var go = SpawnOrCreateSimpleVFX("slash", position);
            if (go == null) return;

            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            go.transform.rotation = Quaternion.Euler(0, 0, angle);

            var vfx = go.GetComponent<SimpleVFX>();
            if (vfx == null) vfx = go.AddComponent<SimpleVFX>();
            vfx.Play(color, duration, radius, "slash", this);
        }

        /// <summary>
        /// Spawn an area indicator effect at the given position.
        /// </summary>
        public void SpawnAreaIndicator(Vector3 position, Color color, float radius = 2f, float duration = 0.5f)
        {
            var go = SpawnOrCreateSimpleVFX("area", position);
            if (go == null) return;

            var vfx = go.GetComponent<SimpleVFX>();
            if (vfx == null) vfx = go.AddComponent<SimpleVFX>();
            vfx.Play(color, duration, radius, "area", this);
        }

        private GameObject SpawnOrCreateSimpleVFX(string key, Vector3 position)
        {
            if (_pools.ContainsKey(key))
            {
                return Spawn(key, position, Quaternion.identity);
            }

            // Auto-create pool with a simple sprite-based prefab
            var prefab = CreateSimpleVFXPrefab(key);
            RegisterPrefab(key, prefab, poolSizePerType);
            Destroy(prefab);

            return Spawn(key, position, Quaternion.identity);
        }

        private static GameObject CreateSimpleVFXPrefab(string key)
        {
            var go = new GameObject($"VFX_{key}");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            sr.sortingOrder = SortingConfig.Z_SKY;

            // Create a simple circle texture
            int size = 32;
            var tex = new Texture2D(size, size);
            tex.filterMode = FilterMode.Point;
            var pixels = new Color[size * size];
            float center = size / 2f;
            float radiusSq = center * center;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center + 0.5f;
                    float dy = y - center + 0.5f;
                    float distSq = dx * dx + dy * dy;
                    pixels[y * size + x] = distSq <= radiusSq
                        ? Color.white
                        : Color.clear;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();

            sr.sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);

            go.AddComponent<SimpleVFX>();
            return go;
        }

        protected override void OnDestroy()
        {
            ServiceLocator.Unregister<IVFXService>();
            foreach (var pool in _pools.Values)
                pool.Dispose();
            _pools.Clear();

            base.OnDestroy();
        }
    }
}
