using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.UI.HUD.Sheet
{
    /// <summary>
    /// Pins the contract the four tabs of the character sheet share, on the two rebuilt today.
    ///
    /// <para>The canvas half is the one that already broke once: all five canvases of the sheet
    /// shipped on Unity's default 800x600 with <c>matchWidthOrHeight = 0</c>, a 2.0 scale factor
    /// at 1600 wide against every other HUD surface's 1.0, at a sorting order that put them UNDER
    /// the minimap. <see cref="HudLayout"/> exists to stop exactly that.</para>
    ///
    /// <para>What is deliberately NOT asserted: any drawn colour. Unity calls no <c>Awake</c> on
    /// a component added in Edit Mode, so a <c>CanvasRenderer</c> keeps its construction value
    /// whichever branch the code took, and an assertion about a colour passes with the window
    /// unreadable.</para>
    /// </summary>
    [TestFixture]
    public class SheetPanelContractTests
    {
        private GameObject _characterGo, _recordsGo;
        private CharacterSheetHUD _character;
        private RecordsHUD _records;

        [SetUp]
        public void SetUp()
        {
            _characterGo = new GameObject("CharacterSheetHUD");
            _character = _characterGo.AddComponent<CharacterSheetHUD>();
            _character.EnsureBuilt();

            _recordsGo = new GameObject("RecordsHUD");
            _records = _recordsGo.AddComponent<RecordsHUD>();
            _records.EnsureBuilt();
        }

        [TearDown]
        public void TearDown()
        {
            if (_characterGo != null) Object.DestroyImmediate(_characterGo);
            if (_recordsGo != null) Object.DestroyImmediate(_recordsGo);
        }

        private static void AssertCanvasContract(GameObject go, string who)
        {
            var canvas = go.GetComponentInChildren<Canvas>(true);
            Assert.IsNotNull(canvas, who + ": EnsureBuilt must create a canvas.");

            var scaler = canvas.GetComponent<CanvasScaler>();
            Assert.IsNotNull(scaler, who + ": a HUD canvas without a scaler cannot honour R9.");
            Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode, who);
            Assert.AreEqual(HudLayout.ReferenceWidth, scaler.referenceResolution.x, 0.01f, who);
            Assert.AreEqual(HudLayout.ReferenceHeight, scaler.referenceResolution.y, 0.01f, who);
            Assert.AreEqual(HudLayout.Match, scaler.matchWidthOrHeight, 0.001f, who);
            Assert.AreEqual(HudLayout.CharacterSheetSortingOrder, canvas.sortingOrder, who);
        }

        [Test]
        public void BothTabs_UseTheSharedCanvasContract()
        {
            AssertCanvasContract(_characterGo, "CHARACTER");
            AssertCanvasContract(_recordsGo, "RECORDS");
        }

        /// <summary>
        /// The counter-scale, asserted as a PRODUCT. <c>HudRect.Place</c> ends with
        /// <c>localScale = Vector3.one</c> — right for a texel-space child and fatal for the one
        /// rect that carries the scale — and it cost the talents board a whole capture before a
        /// test said so. It holds at any resolution, which matters because the Game view's size
        /// in Edit Mode is whatever the machine's window happens to be.
        /// </summary>
        [Test]
        public void BothTabs_DrawTheirTexelContentAtTheCounterScale()
        {
            AssertCounterScale(_character.Chrome, "CHARACTER");
            AssertCounterScale(_records.Chrome, "RECORDS");
        }

        private static void AssertCounterScale(SheetPanelChrome chrome, string who)
        {
            chrome.Refit(force: true);
            var pixels = chrome.Pixels;
            var panel = chrome.PanelRect;
            Assert.Greater(pixels.localScale.x, 0f, who + ": the pixel root must carry a scale.");
            Assert.AreEqual(chrome.Style.widthTexels, pixels.sizeDelta.x, 0.01f,
                who + ": the pixel root is sized in TEXELS.");
            Assert.AreEqual(pixels.sizeDelta.x * pixels.localScale.x, panel.sizeDelta.x, 0.01f,
                who + ": the drawn panel must be its texel content times the counter-scale.");
            Assert.AreEqual(pixels.sizeDelta.y * pixels.localScale.y, panel.sizeDelta.y, 0.01f, who);
        }

        /// <summary>Both tabs occupy the same rect, or switching tabs reads as the window jumping.</summary>
        [Test]
        public void BothTabs_AreTheSameSize()
        {
            Assert.AreEqual(_character.Chrome.Style.widthTexels, _records.Chrome.Style.widthTexels);
            Assert.AreEqual(_character.Chrome.Style.heightTexels, _records.Chrome.Style.heightTexels);
        }

        /// <summary>
        /// A click that misses the panel must not reach the world behind it. In the war stance
        /// that click casts a spell.
        /// </summary>
        [Test]
        public void BothTabs_EatClicksThatMissThePanel()
        {
            foreach (var go in new[] { _characterGo, _recordsGo })
            {
                var veil = go.GetComponentInChildren<Canvas>(true).transform.Find("Veil");
                Assert.IsNotNull(veil, go.name + ": no veil.");
                Assert.IsTrue(veil.GetComponent<Image>().raycastTarget, go.name);
            }
        }

        /// <summary>
        /// One row per stat in the closed catalogue. The old panel produced fourteen LINES of a
        /// string; a row is a rect, which is the only thing that can be aligned.
        /// </summary>
        [Test]
        public void Character_HasOneRowPerStat()
        {
            Assert.AreEqual(StatCatalog.All.Length, _character.Rows.Count);

            // Every row is the same width and they do not overlap: the property the old panel
            // could not have, because PadRight in a proportional font spread the value column
            // across 87 px.
            for (int i = 1; i < _character.Rows.Count; i++)
            {
                var a = _character.Rows[i - 1];
                var b = _character.Rows[i];
                Assert.AreNotEqual(a.Stat, b.Stat);
                Assert.AreEqual(a.ValueCentre.x, b.ValueCentre.x, 0.01f,
                    "Every value sits in the SAME column. That is what a column is.");
            }
        }

        /// <summary>
        /// The window must fit the screen at every resolution the grid supports, not only at the
        /// one everybody captures at.
        ///
        /// <para><b>Why this is not a formality.</b> The grimoire was found breaking on GOOD
        /// displays and not bad ones: its panel derived its TEXEL size by dividing a fixed pixel
        /// canvas by the pixel scale, so the higher the DPI the fewer texels it had, and at 1080p
        /// its board fell to 110 texels for a row that inks 270. All three sessions rebuilding
        /// this HUD capture at 1600x800 — which is exactly the resolution where that is invisible.
        /// These two windows declare FIXED texels instead, which should make them immune; "should"
        /// is what has been wrong five times tonight, so it is measured rather than reasoned.</para>
        /// </summary>
        [Test]
        public void TheWindow_FitsEveryResolutionTheGridSupports()
        {
            var grid = PlayerHudStyle.Active;
            var style = SheetHudStyle.Active;

            var screens = new[]
            {
                new Vector2Int(1600, 800),    // the reference, and what everyone captures at
                new Vector2Int(1920, 1080),
                new Vector2Int(2560, 1440),
                new Vector2Int(3840, 2160),
                new Vector2Int(1280, 720),    // the smallest a player is likely to run
            };

            foreach (var s in screens)
            {
                int scale = grid.HudPixelScaleFor(s.x, s.y);
                Assert.GreaterOrEqual(scale, 1, $"{s.x}x{s.y}: the pixel scale must be a whole number ≥ 1.");

                int w = style.widthTexels * scale;
                int h = style.heightTexels * scale;
                Assert.LessOrEqual(w, s.x, $"{s.x}x{s.y}: the sheet is {w} px wide at scale {scale}.");
                Assert.LessOrEqual(h, s.y, $"{s.x}x{s.y}: the sheet is {h} px tall at scale {scale}.");
            }
        }

        /// <summary>
        /// The body's own arithmetic must leave the table a positive width at any identity column,
        /// or a style edit silently produces a negative rect that uGUI draws as nothing.
        /// </summary>
        [Test]
        public void TheBody_LeavesTheTableRoom()
        {
            var chrome = _character.Chrome;
            int tableWidth = chrome.BodyWidth - chrome.Style.identityWidthTexels - 6;
            Assert.Greater(tableWidth, chrome.Style.valueColumnTexels + 20,
                "The stat table needs room for a name, a bar and a value.");
            Assert.Greater(chrome.BodyHeight, chrome.Style.statRowTexels * StatCatalog.All.Length - 1,
                "Every stat row must fit in the body: there is no scroll here and there should " +
                "not need to be.");
        }

        [Test]
        public void EnsureBuilt_IsIdempotent()
        {
            int before = _characterGo.GetComponentsInChildren<Canvas>(true).Length;
            _character.EnsureBuilt();
            _character.EnsureBuilt();
            Assert.AreEqual(before, _characterGo.GetComponentsInChildren<Canvas>(true).Length);
        }

        /// <summary>
        /// No pixel label may be wider than the rect it was given. <c>InkWidth</c> is measured
        /// from the FONT, so it is exact in Edit Mode where uGUI lays nothing out.
        /// </summary>
        [Test]
        public void NoPixelLabel_OverflowsItsRect()
        {
            _character.Open();
            _records.Open();

            int inspected = 0;
            foreach (var go in new[] { _characterGo, _recordsGo })
                foreach (var label in go.GetComponentsInChildren<HudPixelText>(true))
                {
                    if (string.IsNullOrEmpty(label.Text)) continue;
                    inspected++;
                    Assert.LessOrEqual(label.InkWidth, label.rectTransform.sizeDelta.x,
                        $"{go.name}/{label.name} draws \"{label.Text}\" at {label.InkWidth} texels " +
                        $"in {label.rectTransform.sizeDelta.x}.");
                }

            Assert.Greater(inspected, 0, "No label carried text — the fixture is vacuous.");
        }
    }
}
