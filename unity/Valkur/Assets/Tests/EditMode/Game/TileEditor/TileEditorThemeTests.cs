using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.TileEditor
{
    /// <summary>
    /// Unit tests for <see cref="TileEditorTheme"/>: defaults, mutation, ResetToDefaults
    /// and the OnChanged event raised by ApplyToAll.
    /// </summary>
    public class TileEditorThemeTests
    {
        [SetUp]
        public void SetUp()
        {
            // Theme is a global static — make sure every test starts from defaults.
            TileEditorTheme.ResetToDefaults();
        }

        [TearDown]
        public void TearDown()
        {
            TileEditorTheme.ResetToDefaults();
        }

        [Test]
        public void Defaults_ArePopulated()
        {
            // Sanity: the default colors are non-default Color (i.e. not Color.clear / Color(0,0,0,0)).
            Assert.AreNotEqual(default(Color), TileEditorTheme.PanelBg,    "PanelBg default should be set");
            Assert.AreNotEqual(default(Color), TileEditorTheme.HeaderBg,   "HeaderBg default should be set");
            Assert.AreNotEqual(default(Color), TileEditorTheme.Border,     "Border default should be set");
            Assert.AreNotEqual(default(Color), TileEditorTheme.Separator,  "Separator default should be set");
            Assert.AreNotEqual(default(Color), TileEditorTheme.HeaderTitle,"HeaderTitle default should be set");
            Assert.AreNotEqual(default(Color), TileEditorTheme.MenuBarBg,  "MenuBarBg default should be set");
            Assert.Greater(TileEditorTheme.OutlinePx, 0f,                  "OutlinePx default should be > 0");
        }

        [Test]
        public void PanelBg_HasSemiTransparentAlpha()
        {
            // The default PERF-PROBE-style panel bg is semi-transparent dark.
            Assert.Less   (TileEditorTheme.PanelBg.a, 1f,  "PanelBg should be semi-transparent");
            Assert.Greater(TileEditorTheme.PanelBg.a, 0f,  "PanelBg should not be fully transparent");
        }

        [Test]
        public void Mutate_PanelBg_PersistsValue()
        {
            var before = TileEditorTheme.PanelBg;
            var custom = new Color(1f, 0.25f, 0.40f, 0.5f);
            TileEditorTheme.PanelBg = custom;

            Assert.AreEqual(custom, TileEditorTheme.PanelBg);
            Assert.AreNotEqual(before, TileEditorTheme.PanelBg);
        }

        [Test]
        public void Mutate_OutlinePx_PersistsValue()
        {
            TileEditorTheme.OutlinePx = 3.5f;
            Assert.AreEqual(3.5f, TileEditorTheme.OutlinePx, 0.0001f);
        }

        [Test]
        public void ResetToDefaults_RestoresEveryField()
        {
            // capture
            var defPanel  = TileEditorTheme.PanelBg;
            var defHdr    = TileEditorTheme.HeaderBg;
            var defBorder = TileEditorTheme.Border;
            var defSep    = TileEditorTheme.Separator;
            var defTitle  = TileEditorTheme.HeaderTitle;
            var defMenu   = TileEditorTheme.MenuBarBg;
            var defOl     = TileEditorTheme.OutlinePx;

            // mutate everything
            TileEditorTheme.PanelBg     = new Color(1f, 0f, 0f, 0.3f);
            TileEditorTheme.HeaderBg    = new Color(0f, 1f, 0f, 0.3f);
            TileEditorTheme.Border      = new Color(0f, 0f, 1f, 0.3f);
            TileEditorTheme.Separator   = new Color(1f, 1f, 0f, 0.3f);
            TileEditorTheme.HeaderTitle = new Color(1f, 0f, 1f, 1f);
            TileEditorTheme.MenuBarBg   = new Color(0f, 1f, 1f, 0.5f);
            TileEditorTheme.OutlinePx   = 4f;

            TileEditorTheme.ResetToDefaults();

            Assert.AreEqual(defPanel,  TileEditorTheme.PanelBg);
            Assert.AreEqual(defHdr,    TileEditorTheme.HeaderBg);
            Assert.AreEqual(defBorder, TileEditorTheme.Border);
            Assert.AreEqual(defSep,    TileEditorTheme.Separator);
            Assert.AreEqual(defTitle,  TileEditorTheme.HeaderTitle);
            Assert.AreEqual(defMenu,   TileEditorTheme.MenuBarBg);
            Assert.AreEqual(defOl,     TileEditorTheme.OutlinePx, 0.0001f);
        }

        [Test]
        public void ApplyToAll_RaisesOnChangedEvent()
        {
            int count = 0;
            System.Action h = () => count++;
            TileEditorTheme.OnChanged += h;
            try
            {
                TileEditorTheme.ApplyToAll();
                Assert.AreEqual(1, count, "OnChanged should be raised exactly once per ApplyToAll");

                TileEditorTheme.ApplyToAll();
                Assert.AreEqual(2, count, "OnChanged should be raised again on subsequent ApplyToAll");
            }
            finally
            {
                TileEditorTheme.OnChanged -= h;
            }
        }

        [Test]
        public void ResetToDefaults_RaisesOnChangedEvent()
        {
            int count = 0;
            System.Action h = () => count++;
            TileEditorTheme.OnChanged += h;
            try
            {
                TileEditorTheme.ResetToDefaults();
                Assert.GreaterOrEqual(count, 1, "ResetToDefaults should trigger OnChanged via ApplyToAll");
            }
            finally
            {
                TileEditorTheme.OnChanged -= h;
            }
        }

        [Test]
        public void ApplyToAll_DoesNotThrow_WhenNoListeners()
        {
            // OnChanged has no subscribers right now (TearDown removed any from previous tests).
            // The null-conditional invocation must not throw.
            Assert.DoesNotThrow(() => TileEditorTheme.ApplyToAll());
        }
    }
}
