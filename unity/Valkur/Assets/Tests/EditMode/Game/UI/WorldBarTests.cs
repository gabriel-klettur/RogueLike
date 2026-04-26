using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;

namespace Valkur.Tests.EditMode.Game.UI
{
    internal static class WorldBarTestHelper
    {
        public static void InvokeAwake(Component c)
        {
            if (c == null) return;
            var m = c.GetType().GetMethod("Awake",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
            m?.Invoke(c, null);
        }
    }

    public class WorldManaBarTests
    {
        private GameObject _go;

        [SetUp] public void SetUp() { LogAssert.ignoreFailingMessages = true; }
        [TearDown] public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void CreatesManaBarChild_WhenManaComponentPresent()
        {
            _go = new GameObject("Player");
            _go.AddComponent<Mana>().Initialize(100);
            var bar = _go.AddComponent<WorldManaBar>();
            WorldBarTestHelper.InvokeAwake(bar);

            var barRoot = _go.transform.Find("ManaBar");
            Assert.IsNotNull(barRoot, "ManaBar child should be created");
            Assert.AreEqual(3, barRoot.childCount, "Should have Border, BG, Fill children");
        }

        [Test]
        public void DoesNotCreateBar_WhenNoManaComponent()
        {
            _go = new GameObject("NoMana");
            var bar = _go.AddComponent<WorldManaBar>();
            WorldBarTestHelper.InvokeAwake(bar);
            Assert.IsNull(_go.transform.Find("ManaBar"), "ManaBar should not be created without Mana component");
        }

        [Test]
        public void FillRenderers_UseCorrectSortingLayer()
        {
            _go = new GameObject("Player");
            _go.AddComponent<Mana>().Initialize(50);
            var bar = _go.AddComponent<WorldManaBar>();
            WorldBarTestHelper.InvokeAwake(bar);

            foreach (var sr in _go.GetComponentsInChildren<SpriteRenderer>())
                Assert.AreEqual("UI_World", sr.sortingLayerName);
        }
    }

    public class WorldDashBarTests
    {
        private GameObject _go;

        [SetUp] public void SetUp() { LogAssert.ignoreFailingMessages = true; }
        [TearDown] public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void CreatesDashBarChild_WhenDashAbilityPresent()
        {
            _go = new GameObject("Player");
            _go.AddComponent<Rigidbody2D>();
            WorldBarTestHelper.InvokeAwake(_go.AddComponent<DashAbility>());
            WorldBarTestHelper.InvokeAwake(_go.AddComponent<WorldDashBar>());

            Assert.IsNotNull(_go.transform.Find("DashBar"), "DashBar child should be created");
        }

        [Test]
        public void DoesNotCreateBar_WhenNoDashAbility()
        {
            _go = new GameObject("NoDash");
            WorldBarTestHelper.InvokeAwake(_go.AddComponent<WorldDashBar>());
            Assert.IsNull(_go.transform.Find("DashBar"), "DashBar should not be created without DashAbility");
        }

        [Test]
        public void SegmentRenderers_UseCorrectSortingLayer()
        {
            _go = new GameObject("Player");
            _go.AddComponent<Rigidbody2D>();
            WorldBarTestHelper.InvokeAwake(_go.AddComponent<DashAbility>());
            WorldBarTestHelper.InvokeAwake(_go.AddComponent<WorldDashBar>());

            foreach (var sr in _go.GetComponentsInChildren<SpriteRenderer>())
                Assert.AreEqual("UI_World", sr.sortingLayerName);
        }

        [Test]
        public void HasOneSegment_ForSingleChargeDash()
        {
            _go = new GameObject("Player");
            _go.AddComponent<Rigidbody2D>();
            WorldBarTestHelper.InvokeAwake(_go.AddComponent<DashAbility>());
            WorldBarTestHelper.InvokeAwake(_go.AddComponent<WorldDashBar>());

            var barRoot = _go.transform.Find("DashBar");
            Assert.IsNotNull(barRoot, "DashBar root should exist");
            Assert.AreEqual(3, barRoot.childCount, "Single segment should have 3 renderers");
        }
    }
}