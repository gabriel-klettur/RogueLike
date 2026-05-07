using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// Generic GameObject pool to reduce allocation and GC pressure.
    /// Maps to Python's particle_pool and spawn budget pattern.
    /// 
    /// Usage:
    ///   var pool = new ObjectPool(prefab, initialSize, parent);
    ///   var obj = pool.Get(position, rotation);
    ///   pool.Return(obj);
    /// </summary>
    public class ObjectPool
    {
        private readonly GameObject _prefab;
        private readonly Transform _parent;
        private readonly Stack<GameObject> _available;
        private readonly HashSet<GameObject> _active;
        private readonly int _maxSize;

        public int ActiveCount => _active.Count;
        public int AvailableCount => _available.Count;

        /// <param name="prefab">Template to instantiate.</param>
        /// <param name="initialSize">Pre-warm count.</param>
        /// <param name="parent">Optional parent transform for organization.</param>
        /// <param name="maxSize">Hard cap on total pooled objects. 0 = unlimited.</param>
        public ObjectPool(GameObject prefab, int initialSize = 10, Transform parent = null, int maxSize = 0)
        {
            _prefab = prefab ?? throw new ArgumentNullException(nameof(prefab));
            _parent = parent;
            _maxSize = maxSize;
            _available = new Stack<GameObject>(initialSize);
            _active = new HashSet<GameObject>();

            for (int i = 0; i < initialSize; i++)
            {
                var obj = CreateInstance();
                obj.SetActive(false);
                _available.Push(obj);
            }
        }

        /// <summary>
        /// Get an object from the pool. Activates and positions it.
        /// </summary>
        public GameObject Get(Vector3 position, Quaternion rotation)
        {
            GameObject obj;

            if (_available.Count > 0)
            {
                obj = _available.Pop();
                if (obj == null)
                {
                    obj = CreateInstance();
                    if (obj == null) return null;
                }
            }
            else
            {
                if (_maxSize > 0 && _active.Count >= _maxSize)
                {
                    string prefabName = _prefab != null ? _prefab.name : "<destroyed>";
                    Debug.LogWarning($"[ObjectPool] Max size ({_maxSize}) reached for {prefabName}. Reusing oldest.");
                    return null;
                }
                obj = CreateInstance();
                if (obj == null) return null;
            }

            obj.transform.SetPositionAndRotation(position, rotation);
            obj.SetActive(true);
            _active.Add(obj);
            return obj;
        }

        /// <summary>
        /// Return an object to the pool. Deactivates it.
        /// </summary>
        public void Return(GameObject obj)
        {
            if (obj == null) return;

            obj.SetActive(false);
            _active.Remove(obj);
            _available.Push(obj);
        }

        /// <summary>
        /// Return all active objects to the pool.
        /// </summary>
        public void ReturnAll()
        {
            foreach (var obj in _active)
            {
                if (obj != null)
                {
                    obj.SetActive(false);
                    _available.Push(obj);
                }
            }
            _active.Clear();
        }

        /// <summary>
        /// Destroy all pooled objects (active and available).
        /// </summary>
        public void Dispose()
        {
            foreach (var obj in _active)
            {
                if (obj != null) SafeDestroy.Of(obj);
            }
            _active.Clear();

            while (_available.Count > 0)
            {
                var obj = _available.Pop();
                if (obj != null) SafeDestroy.Of(obj);
            }
        }

        private GameObject CreateInstance()
        {
            if (_prefab == null)
            {
                Debug.LogError("[ObjectPool] Prefab has been destroyed or is null — cannot create a new pool instance. " +
                               "Ensure the pool owner resets the pool when the scene is reloaded.");
                return null;
            }
            var obj = UnityEngine.Object.Instantiate(_prefab, _parent);
            obj.name = $"{_prefab.name}_pooled";
            return obj;
        }
    }
}
