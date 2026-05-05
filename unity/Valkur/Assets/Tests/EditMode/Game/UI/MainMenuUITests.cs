using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.UI.MainMenu;

namespace Valkur.Tests.EditMode.Game.UI
{
    public class MainMenuUITests
    {
        private GameObject _go;
        private MainMenuUI _menu;

        [SetUp]
        public void SetUp()
        {
            // Destroy any pre-existing instance
            var existing = Object.FindObjectOfType<MainMenuUI>();
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            _go = new GameObject("TestMainMenuUI");
            _menu = _go.AddComponent<MainMenuUI>();
            // Awake runs automatically; manually trigger Start
            InvokePrivate("Start");
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private T GetPrivateField<T>(string fieldName)
        {
            var field = typeof(MainMenuUI).GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) return default;
            return (T)field.GetValue(_menu);
        }

        private void SetPrivateField(string fieldName, object value)
        {
            var field = typeof(MainMenuUI).GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(_menu, value);
        }

        private string GetMenuScreen()
        {
            var field = typeof(MainMenuUI).GetField("_menuScreen",
                BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(_menu)?.ToString() ?? "null";
        }

        private void InvokePrivate(string methodName)
        {
            var method = typeof(MainMenuUI).GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(_menu, null);
        }

        private void InvokeShowMenuScreen(string screenName)
        {
            var field = typeof(MainMenuUI).GetField("_menuScreen",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var enumType = field.FieldType;
            var enumVal = System.Enum.Parse(enumType, screenName);

            var method = typeof(MainMenuUI).GetMethod("ShowMenuScreen",
                BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(_menu, new[] { enumVal });
        }

        // ── Press-to-start ───────────────────────────────────────────────────

        [Test]
        public void PressToStart_IsActiveOnStart()
        {
            bool active = GetPrivateField<bool>("_pressToStartActive");
            Assert.IsTrue(active, "Press-to-start should be active after start");
        }

        [Test]
        public void PressToStart_Overlay_Exists()
        {
            var overlay = GetPrivateField<GameObject>("_pressToStartOverlay");
            Assert.IsNotNull(overlay, "Press-to-start overlay should be built");
        }

        // ── MenuScreen state ─────────────────────────────────────────────────

        [Test]
        public void InitialMenuScreen_IsMain()
        {
            Assert.AreEqual("Main", GetMenuScreen());
        }

        // ── OptionsGoBack navigation ─────────────────────────────────────────

        [Test]
        public void OptionsGoBack_FromOptions_GoesToMain()
        {
            InvokeShowMenuScreen("Options");
            Assert.AreEqual("Options", GetMenuScreen());
            InvokePrivate("OptionsGoBack");
            Assert.AreEqual("Main", GetMenuScreen());
        }

        [Test]
        public void OptionsGoBack_FromSounds_GoesToOptions()
        {
            InvokeShowMenuScreen("Sounds");
            Assert.AreEqual("Sounds", GetMenuScreen());
            InvokePrivate("OptionsGoBack");
            Assert.AreEqual("Options", GetMenuScreen());
        }

        [Test]
        public void OptionsGoBack_FromInputs_GoesToOptions()
        {
            InvokeShowMenuScreen("Inputs");
            Assert.AreEqual("Inputs", GetMenuScreen());
            InvokePrivate("OptionsGoBack");
            Assert.AreEqual("Options", GetMenuScreen());
        }

        [Test]
        public void OptionsGoBack_FromLoadGame_GoesToMain()
        {
            InvokeShowMenuScreen("LoadGame");
            Assert.AreEqual("LoadGame", GetMenuScreen());
            InvokePrivate("OptionsGoBack");
            Assert.AreEqual("Main", GetMenuScreen());
        }

        // ── Menu options (dynamic) ───────────────────────────────────────────

        [Test]
        public void MenuOptions_AlwaysContains_NewGame()
        {
            var options = GetPrivateField<string[]>("_menuOptions");
            Assert.IsNotNull(options);
            Assert.Contains("New Game", options);
        }

        [Test]
        public void MenuOptions_AlwaysContains_Options()
        {
            var options = GetPrivateField<string[]>("_menuOptions");
            Assert.IsNotNull(options);
            Assert.Contains("Options", options);
        }

        [Test]
        public void MenuOptions_AlwaysContains_Exit()
        {
            var options = GetPrivateField<string[]>("_menuOptions");
            Assert.IsNotNull(options);
            Assert.Contains("Exit", options);
        }

        // ── Class selector ───────────────────────────────────────────────────

        [Test]
        public void ClassSelector_NotShowingByDefault()
        {
            bool showing = GetPrivateField<bool>("_showingClassSelector");
            Assert.IsFalse(showing);
        }
    }
}
