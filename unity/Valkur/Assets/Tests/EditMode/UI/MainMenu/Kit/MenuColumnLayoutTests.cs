using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.UI.MainMenu;
using Valkur.UI.MainMenu.Kit;

namespace Valkur.Tests.EditMode.UI.MainMenu.Kit
{
    /// <summary>
    /// A row's columns must not overlap.
    ///
    /// <para><b>Why this can be tested at all.</b> uGUI performs no layout in Edit Mode, so a
    /// test that reads a rect's width measures the RectTransform's default. What IS exact without
    /// a layout pass is the ANCHOR — a fraction of the parent, written by the builder and read
    /// back unchanged. Comparing anchors is the same move as measuring contrast on composited
    /// colours: find the quantity that does not depend on uGUI having run.</para>
    ///
    /// <para><b>What it caught.</b> The first cut ran the label to 0.52 of the row while the
    /// content began at 0.50 — eleven pixels of a slider track under the tail of its own label on
    /// a 560 px panel — and the audio panel's slider reached 0.91 while its value column began at
    /// 0.86, putting twenty-eight pixels of number on top of the track it describes. Neither is
    /// visible to any structural probe, and both are the family of defect a peer session found by
    /// photographing its own window (<c>SIGUIENTE+36</c>: a nine-glyph label eating its value).</para>
    /// </summary>
    public class MenuColumnLayoutTests
    {
        private GameObject _root;
        private RectTransform _body;
        private MenuArt _art;
        private MenuStyle _style;

        [SetUp]
        public void SetUp()
        {
            _style = MenuStyle.Active;
            _art = MenuArt.Get(_style);
            _root = new GameObject("MenuColumnTestRoot", typeof(RectTransform));
            _body = (RectTransform)_root.transform;
            _body.sizeDelta = new Vector2(560f, 400f);
        }

        [TearDown]
        public void TearDown()
        {
            // DestroyImmediate: Object.Destroy is an outright ERROR in Edit Mode.
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void TheColumnConstants_AreOrderedAndDoNotTouch()
        {
            Assert.Less(MenuRow.LabelColumnEnd, MenuRow.ContentColumnStart,
                "the label runs into the content column");
            Assert.LessOrEqual(MenuRow.ContentColumnStart, MenuRow.ValueColumnStart,
                "the content starts after the value it is supposed to sit left of");
            Assert.GreaterOrEqual(MenuRow.ContentColumnStart - MenuRow.LabelColumnEnd, 0.02f,
                "the gap between label and content is too small to survive a long label");
        }

        [Test]
        public void ARowsLabelAndContent_DoNotOverlap()
        {
            var list = new MenuList(_body, _art, _style, reduceMotion: true);
            var row = list.Add(_art, "A label long enough to reach its own column edge");

            var labelRt = (RectTransform)row.Label.transform;
            Assert.LessOrEqual(labelRt.anchorMax.x, row.Content.anchorMin.x + 0.0001f,
                $"label ends at {labelRt.anchorMax.x:F3}, content starts at " +
                $"{row.Content.anchorMin.x:F3}");
        }

        [Test]
        public void ARowsValueColumn_StartsAfterItsLabel()
        {
            var list = new MenuList(_body, _art, _style, reduceMotion: true);
            var row = list.Add(_art, "Label");
            var value = row.Value;                       // created lazily

            var labelRt = (RectTransform)row.Label.transform;
            var valueRt = (RectTransform)value.transform;
            Assert.LessOrEqual(labelRt.anchorMax.x, valueRt.anchorMin.x + 0.0001f,
                "the value column begins inside the label's");
        }

        /// <summary>
        /// The slider lives inside Content, so its reach in ROW space is
        /// <c>ContentStart + hostMax * (1 - ContentStart)</c>. That composition is the thing that
        /// was wrong: each half was reasonable and only the product ran past the value column.
        /// </summary>
        [Test]
        public void ASliderStopsShortOfTheValueColumn()
        {
            var list = new MenuList(_body, _art, _style, reduceMotion: true);
            var row = list.Add(_art, "Volume");
            var value = row.Value;
            var slider = new MenuSlider(row.Content, _art, _style, 0f, 1f, 0.5f, 0.02f, _ => { });
            // A row carrying BOTH a slider and a number needs its value pushed right; the
            // default column is for a row that has only a number in it. Saying so is the fix —
            // the first cut left the two layouts sharing one constant and the slider ran under
            // the value in every audio row.
            row.UseWideContentColumns();

            var hostRt = (RectTransform)slider.Slider.transform;
            float contentStart = row.Content.anchorMin.x;
            float sliderEndInRow = contentStart + hostRt.anchorMax.x * (1f - contentStart);
            float valueStart = ((RectTransform)value.transform).anchorMin.x;

            Assert.LessOrEqual(sliderEndInRow, valueStart + 0.0001f,
                $"the slider reaches {sliderEndInRow:F3} of the row and the value column begins " +
                $"at {valueStart:F3}: the number would sit on the track");
        }

        [Test]
        public void EveryRowInAList_KeepsTheSameColumns()
        {
            var list = new MenuList(_body, _art, _style, reduceMotion: true);
            for (int i = 0; i < 5; i++) list.Add(_art, "Row " + i);

            float labelEnd = -1f;
            foreach (var row in list.Rows)
            {
                var labelRt = (RectTransform)row.Label.transform;
                if (labelEnd < 0f) labelEnd = labelRt.anchorMax.x;
                Assert.AreEqual(labelEnd, labelRt.anchorMax.x, 0.0001f,
                    "two rows of the same list disagree about where the label column ends");
            }
        }

        [Test]
        public void ARowsRectsAllSitInsideTheRow()
        {
            var list = new MenuList(_body, _art, _style, reduceMotion: true);
            var row = list.Add(_art, "Label");
            var value = row.Value;

            foreach (RectTransform child in row.Root)
            {
                Assert.GreaterOrEqual(child.anchorMin.x, -0.0001f, child.name + " starts left of the row");
                Assert.LessOrEqual(child.anchorMax.x, 1.0001f, child.name + " runs past the row");
            }
            Assert.IsNotNull(value);
        }
    }
}
