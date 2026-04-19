using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.TileEditor
{
    /// <summary>
    /// Tests for <see cref="MenuBarChrome"/>: single-instance behaviour,
    /// ApplyTheme repaint (with the menu-bar specific bottom-only outline),
    /// and null-safety.
    /// </summary>
    public class MenuBarChromeTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            TileEditorTheme.ResetToDefaults();
            ClearInstance();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            ClearInstance();
            TileEditorTheme.ResetToDefaults();
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static void ClearInstance()
        {
            var fld = typeof(MenuBarChrome).GetField("_instance",
                BindingFlags.NonPublic | BindingFlags.Static);
            fld?.SetValue(null, null);
        }

        private static MenuBarChrome CurrentInstance()
        {
            var fld = typeof(MenuBarChrome).GetField("_instance",
                BindingFlags.NonPublic | BindingFlags.Static);
            return fld?.GetValue(null) as MenuBarChrome;
        }

        private MenuBarChrome BuildBar(string name = "MenuBar")
        {
            var canvasGo = new GameObject(name + "_Canvas");
            canvasGo.AddComponent<Canvas>();
            _spawned.Add(canvasGo);

            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);
            var bg = go.AddComponent<Image>();
            var ol = go.AddComponent<Outline>();

            var chrome = go.AddComponent<MenuBarChrome>();
            chrome.BgImage       = bg;
            chrome.BorderOutline = ol;
            return chrome;
        }

        // ── Single-instance ─────────────────────────────────────────────────

        [Test]
        public void OnEnable_SetsSingletonInstance()
        {
            var bar = BuildBar();
            Assert.AreSame(bar, CurrentInstance());
        }

        [Test]
        public void OnDisable_ClearsSingletonInstance()
        {
            var bar = BuildBar();
            bar.gameObject.SetActive(false);
            Assert.IsNull(CurrentInstance());
        }

        [Test]
        public void NewInstance_ReplacesPrevious()
        {
            var first  = BuildBar("First");
            var second = BuildBar("Second");

            Assert.AreSame(second, CurrentInstance(),
                "Most recently enabled MenuBarChrome should win");
            Assert.IsNotNull(first, "First instance still exists, just not the registered singleton");
        }

        // ── ApplyTheme ──────────────────────────────────────────────────────

        [Test]
        public void ApplyTheme_PaintsBgFromMenuBarBg()
        {
            var custom = new Color(0.10f, 0.10f, 0.10f, 0.95f);
            TileEditorTheme.MenuBarBg = custom;

            var bar = BuildBar();
            bar.ApplyTheme();

            Assert.AreEqual(custom, bar.BgImage.color);
        }

        [Test]
        public void ApplyTheme_OutlineColor_UsesBorder()
        {
            var border = new Color(0.5f, 0.0f, 0.5f, 0.6f);
            TileEditorTheme.Border = border;

            var bar = BuildBar();
            bar.ApplyTheme();

            Assert.AreEqual(border, bar.BorderOutline.effectColor);
        }

        [Test]
        public void ApplyTheme_OutlineDistance_UsesNegativeOutlinePxOnY()
        {
            // Menu bar border is on the BOTTOM only — effectDistance = (0, -OutlinePx).
            TileEditorTheme.OutlinePx = 2f;
            var bar = BuildBar();
            bar.ApplyTheme();

            Assert.AreEqual(new Vector2(0f, -2f), bar.BorderOutline.effectDistance);
        }

        [Test]
        public void ApplyTheme_NullRefs_DoesNotThrow()
        {
            var go = new GameObject("Bare", typeof(RectTransform));
            _spawned.Add(go);
            var chrome = go.AddComponent<MenuBarChrome>();
            // BgImage/BorderOutline left null deliberately.
            Assert.DoesNotThrow(() => chrome.ApplyTheme());
        }

        // ── ApplyThemeToAll broadcast ───────────────────────────────────────

        [Test]
        public void ApplyThemeToAll_RepaintsTheCurrentInstance()
        {
            var bar = BuildBar();
            var custom = new Color(0.7f, 0.7f, 0.2f, 1f);
            TileEditorTheme.MenuBarBg = custom;

            MenuBarChrome.ApplyThemeToAll();

            Assert.AreEqual(custom, bar.BgImage.color);
        }

        [Test]
        public void ApplyThemeToAll_NoInstance_DoesNotThrow()
        {
            ClearInstance();
            Assert.DoesNotThrow(() => MenuBarChrome.ApplyThemeToAll());
        }

        // ── End-to-end: theme + chrome integration ──────────────────────────

        [Test]
        public void TileEditorTheme_ApplyToAll_AlsoUpdatesMenuBar()
        {
            var bar = BuildBar();
            var custom = new Color(0.0f, 0.5f, 1f, 0.85f);

            TileEditorTheme.MenuBarBg = custom;
            TileEditorTheme.ApplyToAll();

            Assert.AreEqual(custom, bar.BgImage.color);
        }
    }
}
