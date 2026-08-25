using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.Lighting
{
    /// <summary>
    /// Builds the Ctrl+F3 Lighting editor's whole UI and asserts it survives.
    ///
    /// This fixture exists because the editor was UNOPENABLE and nobody noticed: clicking
    /// "Lighting" in the ESC menu threw a NullReferenceException out of BuildPresetsPanel before a
    /// single preset was drawn, and <c>LightingRuntimeEditor.Activate</c> caught it and logged one
    /// line. The panel simply never appeared.
    ///
    /// The cause is a trap worth remembering: <c>UIFactory.MakeScrollView</c> already puts a
    /// <see cref="VerticalLayoutGroup"/> and a <see cref="ContentSizeFitter"/> on the content it
    /// returns. Both are <c>[DisallowMultipleComponent]</c>, so a second <c>AddComponent</c>
    /// returns <b>null</b> instead of throwing — and the next line dereferences it. The failure
    /// surfaces one line after the mistake, on a line that looks innocent.
    ///
    /// Calling BuildAll directly is the point: here the exception propagates and fails the test,
    /// instead of being swallowed by Activate's try/catch.
    /// </summary>
    [TestFixture]
    public class LightingEditorUIBuilderTests
    {
        private GameObject _canvasGo;
        private LightingEditorUIBuilder.UIRefs _ui;

        [SetUp]
        public void SetUp()
        {
            _canvasGo = new GameObject("TestCanvas");
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var root = new GameObject("Root", typeof(RectTransform));
            root.transform.SetParent(_canvasGo.transform, false);
            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _ui = LightingEditorUIBuilder.BuildAll(
                root.transform,
                onDropdownToggle:      _ => { },
                onModeSelect:          () => { },
                onModeSpawn:           () => { },
                onModeDelete:          () => { },
                onToggleAmbient:       () => { },
                onTogglePointLights:   () => { },
                onScrubTime:           _ => { },
                onPause:               () => { },
                onDayLengthChanged:    _ => { },
                onMinIntensityChanged: _ => { },
                onToggleLightsWindow:  () => { },
                onLightsWindowStart:   _ => { },
                onLightsWindowEnd:     _ => { },
                onJumpDawn:            () => { },
                onJumpNoon:            () => { },
                onJumpDusk:            () => { },
                onJumpMidnight:        () => { },
                onSearchChanged:       _ => { },
                onSave:                () => { },
                onUndo:                () => { },
                onRedo:                () => { },
                onToggleTutorial:      () => { });
        }

        [TearDown]
        public void TearDown()
        {
            if (_canvasGo != null) Object.DestroyImmediate(_canvasGo);
        }

        [Test]
        public void BuildAll_CompletesWithoutThrowing()
        {
            // Reaching [SetUp]'s end at all is the assertion; this makes the intent explicit.
            Assert.Pass("The Lighting editor's UI built end to end.");
        }

        [Test]
        public void ThePresetListExists()
        {
            Assert.IsNotNull(_ui.PresetGrid,
                "No preset list. This is the panel that used to throw before drawing anything, " +
                "which left the whole Lighting editor looking like it simply did not open.");
        }

        [Test]
        public void TheInstancesListExists()
        {
            Assert.IsNotNull(_ui.InstancesListContent,
                "No instances list — the panel that shows the lights actually placed in the world.");
        }

        [Test]
        public void TheSearchBoxExists()
        {
            Assert.IsNotNull(_ui.SearchBox, "The preset search box is gone.");
        }

        [Test]
        public void ScrollContents_CarryExactlyOneLayoutGroupAndOneFitter()
        {
            // The trap, pinned. If someone re-adds a layout component on a MakeScrollView content,
            // AddComponent returns null and the panel breaks again one line later.
            AssertSingleLayout(_ui.PresetGrid,          "preset list");
            AssertSingleLayout(_ui.InstancesListContent, "instances list");
        }

        private static void AssertSingleLayout(RectTransform content, string what)
        {
            Assert.IsNotNull(content, $"The {what} content is null.");

            var groups  = content.GetComponents<LayoutGroup>();
            var fitters = content.GetComponents<ContentSizeFitter>();

            Assert.AreEqual(1, groups.Length,
                $"The {what} content carries {groups.Length} LayoutGroups. LayoutGroup is " +
                "[DisallowMultipleComponent]: anything other than exactly one means an " +
                "AddComponent somewhere returned null and whatever configured it crashed.");
            Assert.AreEqual(1, fitters.Length,
                $"The {what} content carries {fitters.Length} ContentSizeFitters, for the same reason.");
        }

        [Test]
        public void MakeScrollView_AlreadyProvidesTheLayout_WhichIsWhyASecondOneCannotBeAdded()
        {
            // Pins the contract the panels above depend on. If UIFactory ever stops adding these,
            // the get-or-add in the panels keeps working — but this test tells the next reader why
            // the get-or-add is written that way at all.
            var host = new GameObject("ScrollHost", typeof(RectTransform));
            host.transform.SetParent(_canvasGo.transform, false);

            var (_, content) = Valkur.UIKit.UIFactory.MakeScrollView(host.transform, "Probe");

            Assert.IsNotNull(content.GetComponent<VerticalLayoutGroup>(),
                "UIFactory.MakeScrollView is expected to lay out its content already.");
            Assert.IsNotNull(content.GetComponent<ContentSizeFitter>(),
                "…and to size it already.");

            Object.DestroyImmediate(host);
        }
    }
}
