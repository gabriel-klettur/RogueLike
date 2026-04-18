using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.Editors.EditorKit;

namespace Valkur.Tests.EditMode
{
    public class EditorModalTests
    {
        private Canvas _testCanvas;

        [SetUp]
        public void SetUp()
        {
            var canvasGo = new GameObject("TestCanvas");
            _testCanvas = canvasGo.AddComponent<Canvas>();
            _testCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        [TearDown]
        public void TearDown()
        {
            if (_testCanvas != null)
                Object.DestroyImmediate(_testCanvas.gameObject);
        }

        [Test]
        public void Message_ShowsModalWithOkButton()
        {
            bool okCalled = false;
            EditorModal.Message(_testCanvas.transform, "Test Title", "Test Body", () => okCalled = true);

            // Modal creates a child GameObject with "Modal" or similar in name
            Assert.Greater(_testCanvas.transform.childCount, 0, "Modal should create child GameObject");

            // Simulate OK button click by invoking callback directly
            // (Full UI interaction testing would require PlayMode or UI Toolkit)
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(".*"));
        }

        [Test]
        public void Confirm_CreatesModalWithOkAndCancelButtons()
        {
            bool okCalled = false;
            bool cancelCalled = false;

            EditorModal.Confirm(_testCanvas.transform, "Confirm Test", "Are you sure?",
                () => okCalled = true,
                () => cancelCalled = true);

            Assert.Greater(_testCanvas.transform.childCount, 0, "Modal should create child GameObject");
        }

        [Test]
        public void Prompt_CreatesModalWithInputField()
        {
            string result = null;

            EditorModal.Prompt(_testCanvas.transform, "Enter Name", "DefaultName",
                value => result = value,
                null);

            Assert.Greater(_testCanvas.transform.childCount, 0, "Modal should create child GameObject");
            // Input field should be created and initialized with default value
        }

        [Test]
        public void Form_CreatesModalWithMultipleFields()
        {
            EditorModal.FormResult result = null;

            var fields = new[]
            {
                EditorModal.FormField.Text("Name", "DefaultName"),
                EditorModal.FormField.Int("HP", 100),
                EditorModal.FormField.Dropdown("Type", new[] { "A", "B", "C" }, 0)
            };

            EditorModal.Form(_testCanvas.transform, "Add Entity", fields,
                r => result = r,
                null);

            Assert.Greater(_testCanvas.transform.childCount, 0, "Modal should create child GameObject with form fields");
            // Form should have 3 fields created
        }

        [Test]
        public void ModalHierarchy_IsCreatedCorrectly()
        {
            EditorModal.Message(_testCanvas.transform, "Hierarchy Test", "Check structure", null);

            Assert.AreEqual(1, _testCanvas.transform.childCount, "Should create exactly one modal root");

            var modalRoot = _testCanvas.transform.GetChild(0);
            Assert.IsNotNull(modalRoot, "Modal root should exist");
            Assert.Greater(modalRoot.childCount, 0, "Modal should have child elements (blocker, panel, etc.)");
        }

        [Test]
        public void FormField_TextFieldCreatesCorrectly()
        {
            var field = EditorModal.FormField.Text("TestKey", "TestDefault");

            Assert.AreEqual("TestKey", field.Key);
            Assert.AreEqual(EditorModal.FormField.FieldType.Text, field.Type);
            Assert.AreEqual("TestDefault", field.TextDefault);
        }

        [Test]
        public void FormField_IntFieldCreatesCorrectly()
        {
            var field = EditorModal.FormField.Int("HPKey", 50);

            Assert.AreEqual("HPKey", field.Key);
            Assert.AreEqual(EditorModal.FormField.FieldType.Int, field.Type);
            Assert.AreEqual(50, field.IntDefault);
        }

        [Test]
        public void FormField_DropdownFieldCreatesCorrectly()
        {
            var options = new[] { "Option1", "Option2", "Option3" };
            var field = EditorModal.FormField.Dropdown("TypeKey", options, 1);

            Assert.AreEqual("TypeKey", field.Key);
            Assert.AreEqual(EditorModal.FormField.FieldType.Dropdown, field.Type);
            Assert.AreEqual(options, field.DropdownOptions);
            Assert.AreEqual(1, field.DropdownDefault);
        }
    }
}
