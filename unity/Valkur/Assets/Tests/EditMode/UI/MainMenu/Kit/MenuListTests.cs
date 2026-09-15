using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.UI.MainMenu;
using Valkur.UI.MainMenu.Kit;

namespace Valkur.Tests.EditMode.UI.MainMenu.Kit
{
    /// <summary>
    /// The list widget every menu screen is built from.
    ///
    /// <para><b>What it can and cannot see.</b> uGUI performs NO layout in Edit Mode, so nothing
    /// here may assert on a rendered rect — the numbers would be the RectTransform's defaults.
    /// What is real without a layout pass is the widget's own state: which row is selected, where
    /// the highlight is HEADING, whether a dead row can be reached, and whether the slide is a
    /// slide at all.</para>
    ///
    /// <para>The shipped menus gave every row its own pill and switched them on and off, so the
    /// highlight teleported and there was nothing that could be animated. One pill that MOVES is
    /// the whole of what this fixture protects.</para>
    /// </summary>
    public class MenuListTests
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
            _root = new GameObject("MenuListTestRoot", typeof(RectTransform));
            _body = (RectTransform)_root.transform;
            _body.sizeDelta = new Vector2(400f, 400f);
        }

        [TearDown]
        public void TearDown()
        {
            // DestroyImmediate: Object.Destroy is an outright ERROR in Edit Mode.
            if (_root != null) Object.DestroyImmediate(_root);
        }

        private MenuList Build(int rows, bool reduceMotion = false)
        {
            var list = new MenuList(_body, _art, _style, reduceMotion);
            for (int i = 0; i < rows; i++) list.Add(_art, "Row " + i);
            return list;
        }

        [Test]
        public void ANewList_SelectsItsFirstRow()
        {
            var list = Build(4);
            Assert.AreEqual(4, list.Count);
            Assert.AreEqual(0, list.Index);
            Assert.IsTrue(list.Rows[0].Selected);
            Assert.IsFalse(list.Rows[1].Selected);
        }

        [Test]
        public void MoveBy_WrapsAtBothEnds()
        {
            var list = Build(3);
            list.MoveBy(-1);
            Assert.AreEqual(2, list.Index, "moving up from the first row must wrap to the last");
            list.MoveBy(1);
            Assert.AreEqual(0, list.Index, "moving down from the last row must wrap to the first");
        }

        [Test]
        public void MoveBy_SkipsARowThatCannotBeChosen_ButLeavesItVisible()
        {
            var list = Build(3);
            list.Rows[1].Interactable = false;
            list.Index = 0;
            list.MoveBy(1);
            Assert.AreEqual(2, list.Index, "the keyboard must step over a dead row");
            Assert.IsTrue(list.Rows[1].Root.gameObject.activeSelf,
                "a dead row stays on screen: hidden, it is indistinguishable from one that does " +
                "not exist");
        }

        [Test]
        public void EveryRowDead_LeavesTheSelectionWhereItWas_RatherThanLooping()
        {
            var list = Build(3);
            foreach (var row in list.Rows) row.Interactable = false;
            list.MoveBy(1);
            Assert.AreEqual(0, list.Index);
        }

        [Test]
        public void ChooseCurrent_RefusesADeadRow_AndSaysSo()
        {
            var list = Build(2);
            int chosen = -1;
            list.Chosen += i => chosen = i;

            list.Rows[0].Interactable = false;
            Assert.IsFalse(list.ChooseCurrent(), "a dead row must report the refusal to the caller");
            Assert.AreEqual(-1, chosen, "and must not raise Chosen");

            list.Rows[0].Interactable = true;
            Assert.IsTrue(list.ChooseCurrent());
            Assert.AreEqual(0, chosen);
        }

        [Test]
        public void Changed_IsRaisedOnlyWhenTheSelectionActuallyMoves()
        {
            var list = Build(3);
            int raised = 0;
            list.Changed += _ => raised++;

            list.MoveBy(1);
            Assert.AreEqual(1, raised);

            list.Index = 1;                       // already there
            Assert.AreEqual(1, raised, "re-selecting the same row must not re-announce it");
        }

        /// <summary>
        /// The highlight has a POSITION and a TARGET, and they differ while it travels. That gap
        /// is the animation; without it the pill teleports, which is what the shipped menu did.
        /// </summary>
        [Test]
        public void TheHighlight_TravelsRatherThanTeleporting()
        {
            var list = Build(4);
            float start = list.HighlightY;
            list.Index = 3;

            Assert.AreNotEqual(list.HighlightTargetY, start, "the target must have moved");
            Assert.AreEqual(start, list.HighlightY, 0.001f,
                "the highlight must not have arrived on the same frame it was told to move");

            list.Tick(0.016f);
            Assert.AreNotEqual(start, list.HighlightY, "one tick must move it");
            Assert.AreNotEqual(list.HighlightTargetY, list.HighlightY,
                "and must not finish the whole journey in one frame");

            for (int i = 0; i < 200; i++) list.Tick(0.016f);
            Assert.AreEqual(list.HighlightTargetY, list.HighlightY, 0.01f,
                "it must settle exactly on the target, not near it");
        }

        [Test]
        public void UnderReduceMotion_TheHighlightArrivesAtOnce()
        {
            var list = Build(4, reduceMotion: true);
            list.Index = 3;
            Assert.AreEqual(list.HighlightTargetY, list.HighlightY, 0.001f,
                "reduce motion must remove the travel, not merely shorten it");
        }

        [Test]
        public void SetReduceMotion_SnapsAnInFlightHighlight()
        {
            var list = Build(4);
            list.Index = 3;
            list.Tick(0.016f);
            list.SetReduceMotion(true);
            Assert.AreEqual(list.HighlightTargetY, list.HighlightY, 0.001f);
        }

        [Test]
        public void ContentHeight_GrowsWithTheRows_SoAPanelCanSizeItself()
        {
            var one = Build(1).ContentHeight;
            TearDown(); SetUp();
            var four = Build(4).ContentHeight;
            Assert.Greater(four, one * 3f, "four rows must be meaningfully taller than one");
        }

        [Test]
        public void ARow_ShowsItsValueOnlyOnceSomethingAsksForIt()
        {
            var list = Build(1);
            var row = list.Rows[0];
            // The value column is created lazily: a row with nothing to say must not carry an
            // empty label that still costs a TMP component.
            Assert.IsNotNull(row.Label);
            Assert.IsNotNull(row.Value, "asking for it must create it");
            row.Value.text = "42";
            Assert.AreEqual("42", row.Value.text);
        }

        [Test]
        public void OnlyTheHitTarget_CatchesThePointer()
        {
            var list = Build(1);
            var row = list.Rows[0];
            foreach (var g in row.Root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            {
                if (g.gameObject == row.HitTarget) continue;
                Assert.IsFalse(g.raycastTarget,
                    $"'{g.gameObject.name}' catches the pointer and would steal the row's hover");
            }
        }

        [Test]
        public void SelectingARow_DarkensItsInk()
        {
            var list = Build(2);
            var selected = list.Rows[0];
            var other = list.Rows[1];
            Assert.AreEqual(_style.textOnSelection, selected.Label.color,
                "on the filled pill the text must go dark — gold on gold measured 4.15:1");
            Assert.AreEqual(_style.TextPrimary, other.Label.color);
        }

        [Test]
        public void ADeadRow_IsDrawnMuted()
        {
            var list = Build(2);
            list.Rows[1].Interactable = false;
            Assert.AreEqual(_style.TextMuted, list.Rows[1].Label.color);
        }
    }
}
