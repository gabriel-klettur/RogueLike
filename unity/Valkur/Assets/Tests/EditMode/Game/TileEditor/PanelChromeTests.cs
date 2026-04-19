using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.TileEditor
{
    /// <summary>
    /// Tests for <see cref="PanelChrome"/>: registry lifecycle, ApplyTheme repaint,
    /// null-safety for narrow panels (no title), and ApplyThemeToAll broadcast.
    /// </summary>
    public class PanelChromeTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            TileEditorTheme.ResetToDefaults();
            ClearChromeRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            ClearChromeRegistry();
            TileEditorTheme.ResetToDefaults();
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        /// <summary>
        /// Reflects into PanelChrome's private static `_all` registry and clears it,
        /// so cross-test panels don't leak.
        /// </summary>
        private static void ClearChromeRegistry()
        {
            var fld = typeof(PanelChrome).GetField("_all",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (fld?.GetValue(null) is System.Collections.IList list) list.Clear();
        }

        private static int RegistryCount()
        {
            var fld = typeof(PanelChrome).GetField("_all",
                BindingFlags.NonPublic | BindingFlags.Static);
            return fld?.GetValue(null) is System.Collections.IList list ? list.Count : -1;
        }

        /// <summary>
        /// Builds a fresh GameObject with all chrome refs (Image bg, Outline, header
        /// Image, separator Image, title TMP) and a PanelChrome wired to them.
        /// </summary>
        private PanelChrome BuildPanel(string name, bool withTitle = true)
        {
            // Need a Canvas root so TMP/Image work in EditMode without warnings.
            var canvasGo = new GameObject(name + "_Canvas");
            canvasGo.AddComponent<Canvas>();
            _spawned.Add(canvasGo);

            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);

            var bg = go.AddComponent<Image>();
            var ol = go.AddComponent<Outline>();

            var hdrGo = new GameObject("Hdr", typeof(RectTransform));
            hdrGo.transform.SetParent(go.transform, false);
            var hdrImg = hdrGo.AddComponent<Image>();

            var sepGo = new GameObject("Sep", typeof(RectTransform));
            sepGo.transform.SetParent(go.transform, false);
            var sepImg = sepGo.AddComponent<Image>();

            TextMeshProUGUI titleTmp = null;
            if (withTitle)
            {
                var titleGo = new GameObject("Title", typeof(RectTransform));
                titleGo.transform.SetParent(hdrGo.transform, false);
                titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
            }

            var chrome = go.AddComponent<PanelChrome>();
            chrome.PanelBgImage    = bg;
            chrome.PanelOutline    = ol;
            chrome.HeaderBgImage   = hdrImg;
            chrome.HeaderSeparator = sepImg;
            chrome.HeaderTitle     = titleTmp;

            // EditMode does not reliably fire MonoBehaviour Awake/OnEnable on a freshly
            // AddComponent'd script. Invoke them directly via reflection so the chrome
            // self-registers in PanelChrome._all and applies the current theme.
            InvokeLifecycle(chrome, "OnEnable");

            return chrome;
        }

        private static void InvokeLifecycle(MonoBehaviour mb, string methodName)
        {
            var m = mb.GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            m?.Invoke(mb, null);
        }

        // ── Registry lifecycle ──────────────────────────────────────────────

        [Test]
        public void OnEnable_AddsToRegistry()
        {
            BuildPanel("PanelA");
            Assert.AreEqual(1, RegistryCount(), "Active panel should be registered");
        }

        [Test]
        public void OnDisable_RemovesFromRegistry()
        {
            var chrome = BuildPanel("PanelA");
            Assert.AreEqual(1, RegistryCount());

            InvokeLifecycle(chrome, "OnDisable");
            Assert.AreEqual(0, RegistryCount(), "Disabling should remove from registry");

            InvokeLifecycle(chrome, "OnEnable");
            Assert.AreEqual(1, RegistryCount(), "Re-enabling should re-register");
        }

        [Test]
        public void OnDestroy_RemovesFromRegistry()
        {
            var chrome = BuildPanel("PanelA");
            // EditMode does not always fire OnDestroy on DestroyImmediate; invoke directly.
            InvokeLifecycle(chrome, "OnDisable");
            InvokeLifecycle(chrome, "OnDestroy");
            Object.DestroyImmediate(chrome.gameObject);
            // gameObject was removed from _spawned scope when Destroyed; remove from list to avoid double-destroy.
            _spawned.RemoveAll(g => g == null);
            Assert.AreEqual(0, RegistryCount(), "Destroying should remove from registry");
        }

        [Test]
        public void MultiplePanels_AllRegistered()
        {
            BuildPanel("A"); BuildPanel("B"); BuildPanel("C");
            Assert.AreEqual(3, RegistryCount());
        }

        // ── ApplyTheme — repaint ────────────────────────────────────────────

        [Test]
        public void ApplyTheme_PaintsAllRefsFromCurrentTheme()
        {
            var custom = new Color(0.10f, 0.20f, 0.30f, 0.4f);
            TileEditorTheme.PanelBg     = custom;
            TileEditorTheme.HeaderBg    = new Color(0.50f, 0.10f, 0.10f, 0.7f);
            TileEditorTheme.Separator   = new Color(0.20f, 0.80f, 0.20f, 0.5f);
            TileEditorTheme.HeaderTitle = new Color(1f, 1f, 1f, 1f);
            TileEditorTheme.Border      = new Color(0.90f, 0.10f, 0.40f, 0.6f);
            TileEditorTheme.OutlinePx   = 2.5f;

            var chrome = BuildPanel("PanelA");
            chrome.ApplyTheme();

            Assert.AreEqual(TileEditorTheme.PanelBg,     chrome.PanelBgImage.color);
            Assert.AreEqual(TileEditorTheme.HeaderBg,    chrome.HeaderBgImage.color);
            Assert.AreEqual(TileEditorTheme.Separator,   chrome.HeaderSeparator.color);
            Assert.AreEqual(TileEditorTheme.HeaderTitle, chrome.HeaderTitle.color);
            Assert.AreEqual(TileEditorTheme.Border,      chrome.PanelOutline.effectColor);
            Assert.AreEqual(new Vector2(2.5f, 2.5f),     chrome.PanelOutline.effectDistance);
        }

        [Test]
        public void ApplyTheme_NoTitle_DoesNotThrow()
        {
            // Narrow panels (e.g. TOOLS dropdown 60px) have no title TMP.
            var chrome = BuildPanel("Narrow", withTitle: false);
            Assert.IsNull(chrome.HeaderTitle, "Setup should yield a null title for narrow panels");
            Assert.DoesNotThrow(() => chrome.ApplyTheme());
        }

        [Test]
        public void ApplyTheme_AllRefsNull_DoesNotThrow()
        {
            // Manually-instantiated PanelChrome with no wiring (defensive null-checks).
            var go = new GameObject("Bare", typeof(RectTransform));
            _spawned.Add(go);
            var chrome = go.AddComponent<PanelChrome>();
            Assert.DoesNotThrow(() => chrome.ApplyTheme());
        }

        // ── ApplyThemeToAll — global broadcast ──────────────────────────────

        [Test]
        public void ApplyThemeToAll_RepaintsEveryRegisteredPanel()
        {
            var a = BuildPanel("A");
            var b = BuildPanel("B");
            var c = BuildPanel("C");

            var newColor = new Color(0.2f, 0.6f, 0.9f, 0.7f);
            TileEditorTheme.PanelBg = newColor;

            PanelChrome.ApplyThemeToAll();

            Assert.AreEqual(newColor, a.PanelBgImage.color);
            Assert.AreEqual(newColor, b.PanelBgImage.color);
            Assert.AreEqual(newColor, c.PanelBgImage.color);
        }

        [Test]
        public void ApplyThemeToAll_AfterDisable_OnlyAffectsActivePanels()
        {
            var a = BuildPanel("A");
            var b = BuildPanel("B");

            // Capture b's color BEFORE disabling - re-enabling would repaint to current theme.
            InvokeLifecycle(b, "OnDisable");
            var bColorWhenDisabled = b.PanelBgImage.color;

            var newColor = new Color(0.9f, 0.1f, 0.1f, 1f);
            TileEditorTheme.PanelBg = newColor;
            PanelChrome.ApplyThemeToAll();

            Assert.AreEqual(newColor, a.PanelBgImage.color, "Active panel should get the new color");
            Assert.AreEqual(bColorWhenDisabled, b.PanelBgImage.color,
                "Inactive panel must NOT be repainted by the broadcast");
        }

        [Test]
        public void ReEnabling_PullsCurrentTheme()
        {
            var chrome = BuildPanel("A");
            InvokeLifecycle(chrome, "OnDisable");

            // Mutate while disabled
            var newColor = new Color(0.5f, 0.5f, 0.0f, 0.5f);
            TileEditorTheme.PanelBg = newColor;

            InvokeLifecycle(chrome, "OnEnable");   // OnEnable → ApplyTheme

            Assert.AreEqual(newColor, chrome.PanelBgImage.color,
                "OnEnable should pull the latest theme value");
        }

        // ── End-to-end: theme mutation + ApplyToAll ─────────────────────────

        [Test]
        public void TileEditorTheme_ApplyToAll_PaintsAllChromes()
        {
            var a = BuildPanel("A");
            var b = BuildPanel("B");

            var c1 = new Color(0.123f, 0.456f, 0.789f, 0.5f);
            var c2 = new Color(0.987f, 0.654f, 0.321f, 0.8f);

            TileEditorTheme.PanelBg  = c1;
            TileEditorTheme.HeaderBg = c2;
            TileEditorTheme.ApplyToAll();

            Assert.AreEqual(c1, a.PanelBgImage.color);
            Assert.AreEqual(c2, a.HeaderBgImage.color);
            Assert.AreEqual(c1, b.PanelBgImage.color);
            Assert.AreEqual(c2, b.HeaderBgImage.color);
        }
    }
}
