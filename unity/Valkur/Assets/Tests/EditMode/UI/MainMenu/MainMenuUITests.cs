using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.UI.MainMenu;

namespace Valkur.Tests.EditMode.UI.MainMenu
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
        public void OptionsGoBack_FromAudio_GoesToOptions()
        {
            InvokeShowMenuScreen("Audio");
            Assert.AreEqual("Audio", GetMenuScreen());
            InvokePrivate("OptionsGoBack");
            Assert.AreEqual("Options", GetMenuScreen());
        }

        [Test]
        public void OptionsGoBack_FromControls_GoesToOptions()
        {
            InvokeShowMenuScreen("Controls");
            Assert.AreEqual("Controls", GetMenuScreen());
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

        /// <summary>
        /// These three used to assert the ENGLISH labels — <c>Contains("New Game")</c> — so
        /// translating the menu put them red for doing the right thing, and the literal was also
        /// what <c>ExecuteOption</c> switched on. The rows carry a <c>MainMenuItem</c> now, and
        /// that is what is worth pinning: the meaning, not the spelling.
        /// </summary>
        [Test]
        public void MenuItems_AlwaysContain_NewGame_Options_And_Exit()
        {
            var items = MenuItems();
            Assert.IsNotNull(items);
            CollectionAssert.Contains(items, "NewGame");
            CollectionAssert.Contains(items, "Options");
            CollectionAssert.Contains(items, "Exit");
        }

        [Test]
        public void EveryMenuItem_HasALabel()
        {
            var items = MenuItems();
            var labels = GetPrivateField<string[]>("_menuOptions");
            Assert.IsNotNull(labels);
            Assert.AreEqual(items.Count, labels.Length,
                "every row must carry both a meaning and a label");
            foreach (var label in labels)
                Assert.IsFalse(string.IsNullOrWhiteSpace(label), "a row was built with no label");
        }

        /// <summary>The meanings of the rows, as strings, without leaking the private enum.</summary>
        private System.Collections.Generic.List<string> MenuItems()
        {
            var field = typeof(MainMenuUI).GetField("_menuItems",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "MainMenuUI._menuItems must exist");
            var list = (System.Collections.IEnumerable)field.GetValue(_menu);
            var names = new System.Collections.Generic.List<string>();
            foreach (var item in list) names.Add(item.ToString());
            return names;
        }

        [Test]
        public void ClassSelector_NotShowingByDefault()
        {
            bool showing = GetPrivateField<bool>("_showingClassSelector");
            Assert.IsFalse(showing);
        }
    }
}
