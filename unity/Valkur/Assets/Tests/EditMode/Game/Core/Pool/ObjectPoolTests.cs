using NUnit.Framework;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Tests.EditMode.Game.Core.Pool
{
    /// <summary>
    /// EditMode tests for <see cref="ObjectPool"/>: get/return lifecycle,
    /// capacity limits, pre-warming, and per-spawn position/rotation setup.
    ///
    /// Migrated from <c>PlayMode/Core/ObjectPoolPlayTests.cs</c>: the pool
    /// uses <see cref="Object.Instantiate"/> synchronously and the
    /// <c>yield return null</c> after the constructor was gratuitous —
    /// pool internals are wired in the constructor, not in Awake.
    /// </summary>
    [TestFixture]
    public class ObjectPoolTests
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

        [Test]
        public void Get_ReturnsActiveObject()
        {
            var pool = new ObjectPool(_prefab, 2);

            var obj = pool.Get(Vector3.zero, Quaternion.identity);
            try
            {
                Assert.IsNotNull(obj);
                Assert.IsTrue(obj.activeSelf);
                Assert.AreEqual(1, pool.ActiveCount);
            }
            finally
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
        }

        [Test]
        public void Return_DeactivatesAndRecycles()
        {
            var pool = new ObjectPool(_prefab, 2);

            var obj = pool.Get(Vector3.zero, Quaternion.identity);
            Assert.AreEqual(1, pool.ActiveCount);

            pool.Return(obj);
            Assert.AreEqual(0, pool.ActiveCount);
            Assert.IsFalse(obj.activeSelf);

            // Re-Get must reuse the recycled instance.
            var obj2 = pool.Get(new Vector3(1, 2, 3), Quaternion.identity);
            try
            {
                Assert.IsNotNull(obj2);
                Assert.AreEqual(1, pool.ActiveCount);
            }
            finally
            {
                if (obj2 != null) Object.DestroyImmediate(obj2);
            }
        }

        [Test]
        public void PreWarm_CreatesCorrectCount()
        {
            var pool = new ObjectPool(_prefab, 5);

            Assert.AreEqual(5, pool.AvailableCount);
            Assert.AreEqual(0, pool.ActiveCount);
        }

        [Test]
        public void MaxSize_ReturnsNullWhenExhausted()
        {
            var pool = new ObjectPool(_prefab, 2, maxSize: 2);

            var obj1 = pool.Get(Vector3.zero, Quaternion.identity);
            var obj2 = pool.Get(Vector3.one, Quaternion.identity);
            try
            {
                Assert.IsNotNull(obj1);
                Assert.IsNotNull(obj2);

                var obj3 = pool.Get(Vector3.up, Quaternion.identity);
                Assert.IsNull(obj3, "Pool must return null once maxSize is reached.");
            }
            finally
            {
                if (obj1 != null) Object.DestroyImmediate(obj1);
                if (obj2 != null) Object.DestroyImmediate(obj2);
            }
        }

        [Test]
        public void ReturnAll_ReturnsEverything()
        {
            var pool = new ObjectPool(_prefab, 4);

            var obj1 = pool.Get(Vector3.zero, Quaternion.identity);
            var obj2 = pool.Get(Vector3.one, Quaternion.identity);
            var obj3 = pool.Get(Vector3.right, Quaternion.identity);
            try
            {
                Assert.AreEqual(3, pool.ActiveCount);

                pool.ReturnAll();
                Assert.AreEqual(0, pool.ActiveCount);
            }
            finally
            {
                if (obj1 != null) Object.DestroyImmediate(obj1);
                if (obj2 != null) Object.DestroyImmediate(obj2);
                if (obj3 != null) Object.DestroyImmediate(obj3);
            }
        }

        [Test]
        public void Get_SetsPositionAndRotation()
        {
            var pool = new ObjectPool(_prefab, 1);

            var pos = new Vector3(5f, 10f, 0f);
            var rot = Quaternion.Euler(0, 0, 45f);
            var obj = pool.Get(pos, rot);
            try
            {
                Assert.AreEqual(pos.x, obj.transform.position.x, 0.01f);
                Assert.AreEqual(pos.y, obj.transform.position.y, 0.01f);
                Assert.AreEqual(45f, obj.transform.eulerAngles.z, 0.5f);
            }
            finally
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
        }
    }
}
