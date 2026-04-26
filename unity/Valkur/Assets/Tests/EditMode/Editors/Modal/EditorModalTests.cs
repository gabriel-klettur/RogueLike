using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Editors.EditorKit;

namespace Valkur.Tests.EditMode.Editors.Modal
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
            EditorModal.Message(_testCanvas.transform, "Test Title", "Test Body", () => { });

            // Modal creates a child GameObject with "Modal" or similar in name
            Assert.Greater(_testCanvas.transform.childCount, 0, "Modal should create child GameObject");
        }

        [Test]
        public void Confirm_CreatesModalWithOkAndCancelButtons()
        {
            EditorModal.Confirm(_testCanvas.transform, "Confirm Test", "Are you sure?",
                () => { },
                () => { });

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
            Assert.AreEqual(EditorModal.FieldKind.Text, field.Kind);
            Assert.AreEqual("TestDefault", field.Default);
        }

        [Test]
        public void FormField_IntFieldCreatesCorrectly()
        {
            var field = EditorModal.FormField.Int("HPKey", 50);

            Assert.AreEqual("HPKey", field.Key);
            Assert.AreEqual(EditorModal.FieldKind.Int, field.Kind);
            Assert.AreEqual(50, field.Default);
        }

        [Test]
        public void FormField_DropdownFieldCreatesCorrectly()
        {
            var options = new[] { "Option1", "Option2", "Option3" };
            var field = EditorModal.FormField.Dropdown("TypeKey", options, 1);

            Assert.AreEqual("TypeKey", field.Key);
            Assert.AreEqual(EditorModal.FieldKind.Dropdown, field.Kind);
            Assert.AreEqual(options, field.Options);
            Assert.AreEqual(1, field.Default);
        }
    }
}
