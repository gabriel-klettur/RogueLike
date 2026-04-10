using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;

namespace Valkur.Tests.EditMode
{
    public class WorldManaBarTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void CreatesManaBarChild_WhenManaComponentPresent()
        {
            _go = new GameObject("Player");
            _go.AddComponent<Mana>().Initialize(100);
            _go.AddComponent<WorldManaBar>();

            var barRoot = _go.transform.Find("ManaBar");
            Assert.IsNotNull(barRoot, "ManaBar child should be created");
            Assert.AreEqual(3, barRoot.childCount, "Should have Border, BG, Fill children");
        }

        [Test]
        public void DoesNotCreateBar_WhenNoManaComponent()
        {
            _go = new GameObject("NoMana");
            _go.AddComponent<WorldManaBar>();

            var barRoot = _go.transform.Find("ManaBar");
            Assert.IsNull(barRoot, "ManaBar should not be created without Mana component");
        }

        [Test]
        public void FillRenderers_UseCorrectSortingLayer()
        {
            _go = new GameObject("Player");
            _go.AddComponent<Mana>().Initialize(50);
            _go.AddComponent<WorldManaBar>();

            var renderers = _go.GetComponentsInChildren<SpriteRenderer>();
            foreach (var sr in renderers)
            {
                Assert.AreEqual("UI_World", sr.sortingLayerName);
            }
        }
    }

    public class WorldDashBarTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void CreatesDashBarChild_WhenDashAbilityPresent()
        {
            _go = new GameObject("Player");
            _go.AddComponent<Rigidbody2D>();
            _go.AddComponent<DashAbility>();
            _go.AddComponent<WorldDashBar>();

            var barRoot = _go.transform.Find("DashBar");
            Assert.IsNotNull(barRoot, "DashBar child should be created");
        }

        [Test]
        public void DoesNotCreateBar_WhenNoDashAbility()
        {
            _go = new GameObject("NoDash");
            _go.AddComponent<WorldDashBar>();

            var barRoot = _go.transform.Find("DashBar");
            Assert.IsNull(barRoot, "DashBar should not be created without DashAbility");
        }

        [Test]
        public void SegmentRenderers_UseCorrectSortingLayer()
        {
            _go = new GameObject("Player");
            _go.AddComponent<Rigidbody2D>();
            _go.AddComponent<DashAbility>();
            _go.AddComponent<WorldDashBar>();

            var renderers = _go.GetComponentsInChildren<SpriteRenderer>();
            foreach (var sr in renderers)
            {
                Assert.AreEqual("UI_World", sr.sortingLayerName);
            }
        }

        [Test]
        public void HasOneSegment_ForSingleChargeDash()
        {
            _go = new GameObject("Player");
            _go.AddComponent<Rigidbody2D>();
            _go.AddComponent<DashAbility>();
            _go.AddComponent<WorldDashBar>();

            var barRoot = _go.transform.Find("DashBar");
            // 1 segment × 3 renderers (border, bg, fill)
            Assert.AreEqual(3, barRoot.childCount, "Single segment should have 3 renderers");
        }
    }
}
