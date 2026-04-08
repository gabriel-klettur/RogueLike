using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;

namespace Valkur.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for ObjectPool. Validates get/return lifecycle,
    /// capacity limits, and pre-warming behavior.
    /// </summary>
    public class ObjectPoolPlayTests
    {
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject("PoolPrefab");
            _prefab.SetActive(false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_prefab != null)
                Object.DestroyImmediate(_prefab);
        }

        [UnityTest]
        public IEnumerator Get_ReturnsActiveObject()
        {
            var pool = new ObjectPool(_prefab, 2);
            yield return null;

            var obj = pool.Get(Vector3.zero, Quaternion.identity);
            Assert.IsNotNull(obj);
            Assert.IsTrue(obj.activeSelf);
            Assert.AreEqual(1, pool.ActiveCount);

            Object.Destroy(obj);
        }

        [UnityTest]
        public IEnumerator Return_DeactivatesAndRecycles()
        {
            var pool = new ObjectPool(_prefab, 2);
            yield return null;

            var obj = pool.Get(Vector3.zero, Quaternion.identity);
            Assert.AreEqual(1, pool.ActiveCount);

            pool.Return(obj);
            Assert.AreEqual(0, pool.ActiveCount);
            Assert.IsFalse(obj.activeSelf);

            // Getting again should return the same recycled object
            var obj2 = pool.Get(new Vector3(1, 2, 3), Quaternion.identity);
            Assert.IsNotNull(obj2);
            Assert.AreEqual(1, pool.ActiveCount);

            Object.Destroy(obj2);
        }

        [UnityTest]
        public IEnumerator PreWarm_CreatesCorrectCount()
        {
            var pool = new ObjectPool(_prefab, 5);
            yield return null;

            Assert.AreEqual(5, pool.AvailableCount);
            Assert.AreEqual(0, pool.ActiveCount);
        }

        [UnityTest]
        public IEnumerator MaxSize_ReturnsNullWhenExhausted()
        {
            var pool = new ObjectPool(_prefab, 2, maxSize: 2);
            yield return null;

            var obj1 = pool.Get(Vector3.zero, Quaternion.identity);
            var obj2 = pool.Get(Vector3.one, Quaternion.identity);
            Assert.IsNotNull(obj1);
            Assert.IsNotNull(obj2);

            // Pool is at max capacity
            var obj3 = pool.Get(Vector3.up, Quaternion.identity);
            Assert.IsNull(obj3, "Should return null when pool is exhausted");

            Object.Destroy(obj1);
            Object.Destroy(obj2);
        }

        [UnityTest]
        public IEnumerator ReturnAll_ReturnsEverything()
        {
            var pool = new ObjectPool(_prefab, 4);
            yield return null;

            var obj1 = pool.Get(Vector3.zero, Quaternion.identity);
            var obj2 = pool.Get(Vector3.one, Quaternion.identity);
            var obj3 = pool.Get(Vector3.right, Quaternion.identity);

            Assert.AreEqual(3, pool.ActiveCount);

            pool.ReturnAll();
            Assert.AreEqual(0, pool.ActiveCount);

            Object.Destroy(obj1);
            Object.Destroy(obj2);
            Object.Destroy(obj3);
        }

        [UnityTest]
        public IEnumerator Get_SetsPositionAndRotation()
        {
            var pool = new ObjectPool(_prefab, 1);
            yield return null;

            var pos = new Vector3(5f, 10f, 0f);
            var rot = Quaternion.Euler(0, 0, 45f);
            var obj = pool.Get(pos, rot);

            Assert.AreEqual(pos.x, obj.transform.position.x, 0.01f);
            Assert.AreEqual(pos.y, obj.transform.position.y, 0.01f);
            Assert.AreEqual(45f, obj.transform.eulerAngles.z, 0.5f);

            Object.Destroy(obj);
        }
    }
}
