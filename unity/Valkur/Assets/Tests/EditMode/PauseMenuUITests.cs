using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.UI.PauseMenu;

namespace Valkur.Tests.EditMode
{
    public class PauseMenuUITests
    {
        private GameObject _go;
        private PauseMenuUI _menu;

        [SetUp]
        public void SetUp()
        {
            // Destroy any pre-existing singleton
            if (PauseMenuUI.Instance != null)
                Object.DestroyImmediate(PauseMenuUI.Instance.gameObject);

            _go = new GameObject("TestPauseMenuUI");
            _menu = _go.AddComponent<PauseMenuUI>();
            // Awake runs automatically; manually trigger Start
            InvokePrivate("Start");
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        // ── Helper to read private enum _screen ─────────────────────────────

        private string GetScreenState()
        {
            var field = typeof(PauseMenuUI).GetField("_screen",
                BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(_menu)?.ToString() ?? "null";
        }

        private void InvokePrivate(string methodName)
        {
            var method = typeof(PauseMenuUI).GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(_menu, null);
        }

        private void InvokeShowScreen(string screenName)
        {
            var screenField = typeof(PauseMenuUI).GetField("_screen",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var enumType = screenField.FieldType;
            var enumVal = System.Enum.Parse(enumType, screenName);

            var method = typeof(PauseMenuUI).GetMethod("ShowScreen",
                BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(_menu, new[] { enumVal });
        }

        private T GetPrivateField<T>(string fieldName)
        {
            var field = typeof(PauseMenuUI).GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) return default;
            return (T)field.GetValue(_menu);
        }

        // ── Singleton ────────────────────────────────────────────────────────

        [Test]
        public void Instance_IsSetAfterAwake()
        {
            Assert.AreEqual(_menu, PauseMenuUI.Instance);
        }

        [Test]
        public void Instance_DuplicateIsDestroyed()
        {
            var go2 = new GameObject("DupePause");
            go2.AddComponent<PauseMenuUI>();
            Assert.AreEqual(_menu, PauseMenuUI.Instance);
            Object.DestroyImmediate(go2);
        }

        // ── Screen state (initial) ──────────────────────────────────────────

        [Test]
        public void InitialState_IsNone()
        {
            Assert.AreEqual("None", GetScreenState());
        }

        // ── OpenPause / ClosePause ───────────────────────────────────────────

        [Test]
        public void OpenPause_SetsScreenToPause()
        {
            _menu.OpenPause();
            Assert.AreEqual("Pause", GetScreenState());
        }

        [Test]
        public void ClosePause_SetsScreenToNone()
        {
            _menu.OpenPause();
            _menu.ClosePause();
            Assert.AreEqual("None", GetScreenState());
        }

        [Test]
        public void TogglePause_OpensWhenClosed()
        {
            _menu.TogglePause();
            Assert.AreEqual("Pause", GetScreenState());
        }

        [Test]
        public void TogglePause_ClosesWhenOpen()
        {
            _menu.OpenPause();
            _menu.TogglePause();
            Assert.AreEqual("None", GetScreenState());
        }

        // ── GoBack navigation ────────────────────────────────────────────────

        [Test]
        public void GoBack_FromOptions_GoesToPause()
        {
            _menu.OpenPause();
            InvokeShowScreen("Options");
            Assert.AreEqual("Options", GetScreenState());
            InvokePrivate("GoBack");
            Assert.AreEqual("Pause", GetScreenState());
        }

        [Test]
        public void GoBack_FromSounds_GoesToOptions()
        {
            _menu.OpenPause();
            InvokeShowScreen("Sounds");
            Assert.AreEqual("Sounds", GetScreenState());
            InvokePrivate("GoBack");
            Assert.AreEqual("Options", GetScreenState());
        }

        [Test]
        public void GoBack_FromInputs_GoesToOptions()
        {
            _menu.OpenPause();
            InvokeShowScreen("Inputs");
            InvokePrivate("GoBack");
            Assert.AreEqual("Options", GetScreenState());
        }

        [Test]
        public void GoBack_FromLoadGame_GoesToPause()
        {
            _menu.OpenPause();
            InvokeShowScreen("LoadGame");
            Assert.AreEqual("LoadGame", GetScreenState());
            InvokePrivate("GoBack");
            Assert.AreEqual("Pause", GetScreenState());
        }

        // ── Overlay visibility ───────────────────────────────────────────────

        [Test]
        public void OverlayRoot_IsHiddenWhenNone()
        {
            var overlay = GetPrivateField<GameObject>("_overlayRoot");
            if (overlay != null)
                Assert.IsFalse(overlay.activeSelf);
        }

        [Test]
        public void OverlayRoot_IsVisibleWhenPaused()
        {
            _menu.OpenPause();
            var overlay = GetPrivateField<GameObject>("_overlayRoot");
            if (overlay != null)
                Assert.IsTrue(overlay.activeSelf);
        }
    }
}
