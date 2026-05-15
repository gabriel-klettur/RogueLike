using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Game.Editors.TileEditor.Tags
{
    /// <summary>
    /// M1.10 — verify the picker handler <c>TileEditorManager.OnCollisionTagChanged</c>
    /// treats each digit button as an independent toggle, with the "*" button
    /// acting as an all/clear shortcut. Drives the handler via reflection so
    /// we don't need to build the full UI canvas.
    ///
    /// Behaviour pinned:
    ///   • Click "0" with empty state         → state = "0"
    ///   • Click "5" with state = "0"         → state = "0,5"
    ///   • Click "0" with state = "0,5"       → state = "5"   (toggle off)
    ///   • Click "*" with state = "5"         → state = "*"   (set all)
    ///   • Click "*" with state = "*"         → state = ""    (clear all)
    ///   • Click "9" / "garbage"              → state = "*"   (defensive fallback)
    /// </summary>
    [TestFixture]
    public class CollidersPanelMultiTagPickerTests
    {
        private GameObject _host;
        private TileEditorManager _manager;
        private TileEditorState _state;

        [SetUp]
        public void SetUp()
        {
            if (TileEditorManager.HasInstance)
                Object.DestroyImmediate(TileEditorManager.Instance.gameObject);

            _host = new GameObject("TileEditorManager_PickerTestHost");
            _manager = _host.AddComponent<TileEditorManager>();
            // Force Awake so OnSingletonAwake initialises _state.
            typeof(TileEditorManager).GetMethod(
                "Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(_manager, null);

            _state = _manager.State;
            Assert.IsNotNull(_state, "Manager state must be available after Awake.");
        }

        [TearDown]
        public void TearDown()
        {
            if (Valkur.Gameplay.World.Layering.LayerJumpTriggerSystem.HasInstance)
                Object.DestroyImmediate(Valkur.Gameplay.World.Layering.LayerJumpTriggerSystem.Instance.gameObject);
            if (Valkur.Gameplay.World.Layering.WorldCollisionBaker.HasInstance)
                Object.DestroyImmediate(Valkur.Gameplay.World.Layering.WorldCollisionBaker.Instance.gameObject);
            if (Valkur.Gameplay.World.Layering.LayerAutoDropSystem.HasInstance)
                Object.DestroyImmediate(Valkur.Gameplay.World.Layering.LayerAutoDropSystem.Instance.gameObject);
            if (_host != null) Object.DestroyImmediate(_host);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void Click(string tag)
        {
            var method = typeof(TileEditorManager).GetMethod(
                "OnCollisionTagChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "Internal OnCollisionTagChanged must exist.");
            method.Invoke(_manager, new object[] { tag });
        }

        // ── Toggle semantics ─────────────────────────────────────────────────

        [Test]
        public void ClickSingleDigit_FromEmpty_SetsThatDigit()
        {
            _state.ActiveCollisionTag = string.Empty;
            Click("0");
            Assert.AreEqual("0", _state.ActiveCollisionTag);
        }

        [Test]
        public void ClickSecondDigit_AddsItToCsv()
        {
            _state.ActiveCollisionTag = "0";
            Click("5");
            Assert.AreEqual("0,5", _state.ActiveCollisionTag,
                "Second click adds the bit; canonical CSV stays sorted.");
        }

        [Test]
        public void ClickActiveDigit_TogglesItOff()
        {
            _state.ActiveCollisionTag = "0,5";
            Click("0");
            Assert.AreEqual("5", _state.ActiveCollisionTag,
                "Re-clicking an active digit must clear its bit.");
        }

        [Test]
        public void ClickLastRemainingDigit_LeavesEmptyState()
        {
            _state.ActiveCollisionTag = "5";
            Click("5");
            Assert.AreEqual(string.Empty, _state.ActiveCollisionTag,
                "Toggling off the last bit must leave the state empty " +
                "(picker's 'no layers selected' sentinel).");
        }

        // ── Wildcard shortcut ────────────────────────────────────────────────

        [Test]
        public void ClickWildcard_FromPartial_SetsFullMask()
        {
            _state.ActiveCollisionTag = "0,5";
            Click("*");
            Assert.AreEqual("*", _state.ActiveCollisionTag,
                "'*' acts as an all-shortcut when the current mask is not full.");
        }

        [Test]
        public void ClickWildcard_WhenAlreadyFull_ClearsTheMask()
        {
            _state.ActiveCollisionTag = "*";
            Click("*");
            Assert.AreEqual(string.Empty, _state.ActiveCollisionTag,
                "Second '*' click on a full mask clears the state — the all/clear toggle.");
        }

        [Test]
        public void ClickWildcard_FromEmpty_SetsFullMask()
        {
            _state.ActiveCollisionTag = string.Empty;
            Click("*");
            Assert.AreEqual("*", _state.ActiveCollisionTag);
        }

        // ── Defensive fallback ───────────────────────────────────────────────

        [Test]
        public void ClickGarbage_FallsBackToWildcard()
        {
            _state.ActiveCollisionTag = "3";
            Click("garbage");
            Assert.AreEqual("*", _state.ActiveCollisionTag,
                "Unknown tag strings must clamp to wildcard rather than corrupt state.");
        }

        [Test]
        public void ClickOutOfRangeDigit_FallsBackToWildcard()
        {
            _state.ActiveCollisionTag = "3";
            Click("9");
            Assert.AreEqual("*", _state.ActiveCollisionTag,
                "Digit '9' is outside the 0..8 enum; treat as garbage.");
        }

        // ── Authoring flow ───────────────────────────────────────────────────

        [Test]
        public void BuildMultiTag_ByClickingThreeDigits_ProducesCsv()
        {
            _state.ActiveCollisionTag = string.Empty;
            Click("4");
            Click("0");
            Click("2");
            Assert.AreEqual("0,2,4", _state.ActiveCollisionTag,
                "Authoring 3 layers ends in canonical sorted CSV — order of clicks doesn't matter.");
        }
    }
}
