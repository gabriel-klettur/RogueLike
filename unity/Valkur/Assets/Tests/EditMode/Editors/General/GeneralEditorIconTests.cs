using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.Editors.General;
using Valkur.UI.Frontend;

namespace Valkur.Tests.EditMode.Editors.General
{
    /// <summary>
    /// The launcher's talent-style tiles: every entry wears a real icon, every icon draws
    /// something, no two entries share one, and the particles answer events — never rest.
    ///
    /// <para><b>The icon table is keyed by LABEL</b> (<see cref="GeneralEditorIcons"/>), so an
    /// editor added to the registry without a row there would ship an empty socket and nothing
    /// would throw. The first test walks the LIVE registry for exactly that.</para>
    /// </summary>
    [TestFixture]
    public class GeneralEditorIconTests
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private GameObject _host;
        private GeneralEditorManager _launcher;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _host = new GameObject("GeneralEditorIconHost");
            _launcher = _host.AddComponent<GeneralEditorManager>();
            typeof(GeneralEditorManager).GetMethod("OnSingletonAwake", Any)?.Invoke(_launcher, null);
        }

        [TearDown]
        public void TearDown()
        {
            var canvas = (Canvas)typeof(GeneralEditorManager).GetField("_canvas", Any).GetValue(_launcher);
            if (canvas != null) Object.DestroyImmediate(canvas.gameObject);
            if (_host != null) Object.DestroyImmediate(_host);
            var mgr = GameObject.Find("[GameEditorManager]");
            if (mgr != null) Object.DestroyImmediate(mgr);
            LogAssert.ignoreFailingMessages = false;
        }

        private List<GeneralEditorTile> Tiles()
        {
            var panel = (GameObject)typeof(GeneralEditorManager).GetField("_panelRoot", Any).GetValue(_launcher);
            return panel.GetComponentsInChildren<GeneralEditorTile>(true).ToList();
        }

        [Test]
        public void EveryRegistryEntry_HasItsOwnIcon()
        {
            var entries = GeneralEditorRegistry.BuildEntries();
            var seen = new Dictionary<FrontendGlyph, string>();
            foreach (var e in entries)
            {
                var glyph = GeneralEditorIcons.GlyphFor(e.Label);
                Assert.AreNotEqual(FrontendGlyph.None, glyph,
                    $"'{e.Label}' has no row in GeneralEditorIcons: it would ship an empty socket.");
                Assert.IsFalse(seen.ContainsKey(glyph),
                    $"'{e.Label}' and '{(seen.TryGetValue(glyph, out var other) ? other : "")}' wear the same icon.");
                seen[glyph] = e.Label;
            }
        }

        // ── The three defects of the first Play capture (2026-09-14), pinned ─────────────

        /// <summary>
        /// Dust shipped as the accent pushed toward bronze and Snow as warm grey: additive light
        /// that dim reads as dirt over the panel's near-black channel. Every colour any icon can
        /// throw must clear the floors, whichever style and wherever on its ramp.
        /// </summary>
        [Test]
        public void EveryIconMote_IsBrightAndSaturated_NeverASmudge()
        {
            foreach (FrontendGlyph g in System.Enum.GetValues(typeof(FrontendGlyph)))
            foreach (FrontendMoteStyle style in System.Enum.GetValues(typeof(FrontendMoteStyle)))
            for (int i = 0; i <= 10; i++)
            {
                var c = FrontendIconMotes.MoteColour(style, FrontendIconTheme.AccentOf(g), i / 10f);
                Color.RGBToHSV(c, out _, out float s, out float v);
                Assert.GreaterOrEqual(v, FrontendIconMotes.MinValue - 0.001f, $"{g}/{style} at {i / 10f}: value {v:F2} is a smudge on additive");
                Assert.GreaterOrEqual(s, FrontendIconMotes.MinSaturation - 0.001f, $"{g}/{style} at {i / 10f}: saturation {s:F2} reads as a white dot");
            }
        }

        [Test]
        public void TheMoteLayer_IsAdditive()
        {
            var mat = _launcher.Motes.material;
            Assert.IsNotNull(mat, "the launcher's motes have no material");
            Assert.AreEqual((int)UnityEngine.Rendering.BlendMode.One, mat.GetInt("_DstBlend"),
                "motes drawn alpha-blended paint dark discs instead of adding light");
        }

        /// <summary>
        /// The workspace hands back the size the panel HAD. A document saved at the old 280 px put
        /// the fifth column and half of "HERRAMIENTAS" outside the frame, because only the height
        /// was re-derived on restore.
        /// </summary>
        [Test]
        public void RestoringAnOlderSize_NeverShrinksThePanelUnderItsGrid()
        {
            var panel = (GameObject)typeof(GeneralEditorManager).GetField("_panelRoot", Any).GetValue(_launcher);
            var rt = (RectTransform)panel.transform;
            rt.sizeDelta = new Vector2(280f, 200f);
            ((Valkur.Core.Editors.IProvidesWorkspaceState)_launcher)
                .RestoreWorkspace(new Valkur.Core.Editors.EditorWorkspace { editorName = "General" });
            Assert.AreEqual(GeneralEditorManager.PANEL_WIDTH, rt.sizeDelta.x, 0.01f);
        }

        [Test]
        public void TheBuiltGrid_FitsInsideThePanelWidth()
        {
            var panel = (GameObject)typeof(GeneralEditorManager).GetField("_panelRoot", Any).GetValue(_launcher);
            var content = panel.transform.Find("Content").GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            foreach (var grid in panel.GetComponentsInChildren<UnityEngine.UI.GridLayoutGroup>(true))
            {
                float needed = content.padding.left + content.padding.right
                             + grid.constraintCount * grid.cellSize.x + (grid.constraintCount - 1) * grid.spacing.x;
                Assert.LessOrEqual(needed, GeneralEditorManager.PANEL_WIDTH + 0.01f,
                    $"'{grid.name}' needs {needed:F1} px inside a {GeneralEditorManager.PANEL_WIDTH} px panel");
                Assert.GreaterOrEqual(grid.cellSize.x, GeneralEditorTile.SocketSize,
                    $"'{grid.name}': a cell narrower than its socket clips the icon");
            }
        }

        [Test]
        public void TabLabels_ShrinkToFit_AndNeverOverflowTheirTab()
        {
            var tabs = typeof(GeneralEditorManager).GetField("_tabs", Any).GetValue(_launcher) as System.Collections.IDictionary;
            foreach (System.Collections.DictionaryEntry kv in tabs)
            {
                var tmp = (TMPro.TextMeshProUGUI)kv.Value.GetType().GetField("Item3").GetValue(kv.Value);
                Assert.IsTrue(tmp.enableAutoSizing, $"{kv.Key}: a fixed-size label runs past its tab");
                Assert.IsFalse(tmp.enableWordWrapping, $"{kv.Key}: a wrapped tab label stacks out of its row");
                Assert.AreNotEqual(TMPro.TextOverflowModes.Overflow, tmp.overflowMode, $"{kv.Key}: overflow mode lets text leave the tab");
            }
        }

        [Test]
        public void EveryGlyph_DrawsSomething_AndHasAnAccent()
        {
            var painter = new FrontendGlyphPainter();
            foreach (FrontendGlyph g in System.Enum.GetValues(typeof(FrontendGlyph)))
            {
                if (g == FrontendGlyph.None) continue;
                painter.Begin(new Rect(0f, 0f, 46f, 46f));
                Assert.IsTrue(FrontendGlyphs.Paint(g, painter), $"{g} has no case in FrontendGlyphs.Paint.");
                Assert.Greater(painter.ShapeCount, 0, $"{g} paints no parts.");
                Assert.AreNotEqual(FrontendPalette.GoldLight, FrontendIconTheme.AccentOf(g),
                    $"{g} fell through to the default accent.");
            }
        }

        [Test]
        public void EveryEntry_IsATile_WithTheRegistrysIcon()
        {
            var tiles = Tiles();
            Assert.AreEqual(GeneralEditorRegistry.BuildEntries().Count, tiles.Count);
            foreach (var t in tiles)
                Assert.AreNotEqual(FrontendGlyph.None, t.Glyph, $"'{t.name}' has an empty socket.");
        }

        [Test]
        public void ATile_EmitsOnlyWhenSomethingHappens()
        {
            var motes = _launcher.Motes;
            Assert.IsNotNull(motes, "the launcher has no particle layer");
            var tile = Tiles()[0];

            for (int i = 0; i < 60; i++) tile.Tick(1f / 60f);
            Assert.AreEqual(0, motes.Alive, "a tile at rest threw particles");

            tile.OnPointerEnter(null);
            Assert.Greater(motes.Alive, 0, "hovering a tile threw nothing");
            int afterHover = motes.Alive;

            tile.Pressed();
            Assert.Greater(motes.Alive, afterHover, "pressing a tile threw nothing");
        }

        [Test]
        public void ActiveState_LightsTheGem_AndTheIconKeepsItsColour()
        {
            var tile = Tiles()[0];
            var accent = tile.Icon.Accent;
            tile.SetActiveState(true);
            Assert.IsTrue(tile.ShowsActive);
            Assert.AreEqual(accent, tile.Icon.Accent, "state must be said by the socket, never by the icon's colour");
            tile.SetActiveState(false);
            Assert.IsFalse(tile.ShowsActive);
        }
    }
}
