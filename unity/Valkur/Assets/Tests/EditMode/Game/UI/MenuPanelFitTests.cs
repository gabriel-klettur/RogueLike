using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.UI.MainMenu;
using Valkur.UI.MainMenu.Kit;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// A panel may not grow past the screen, and a list that does not fit must still be reachable.
    ///
    /// <para><b>What this caught.</b> Every panel hangs from <c>MenuStyle.panelTopOffset</c> (296)
    /// and grew freely from there. On the 800-unit reference canvas, Audio with its advanced rows
    /// open reached <b>956</b> and Controls <b>1100</b> — the last rows and the entire hint bar
    /// off the bottom of the screen. Nothing structural could see it: each number is reasonable
    /// alone (a row is 46, panels start at 296) and only the SUM is false. It is the same shape
    /// as the spawners' coordinate drift, and it was found by adding up the constants rather than
    /// by looking at the game.</para>
    ///
    /// <para>Assertions are on the ANCHORS and the declared sizes, never on a laid-out rect:
    /// uGUI performs no layout in Edit Mode.</para>
    /// </summary>
    public class MenuPanelFitTests
    {
        private GameObject _root;
        private MenuArt _art;
        private MenuStyle _style;

        [SetUp]
        public void SetUp()
        {
            _style = MenuStyle.Active;
            _art = MenuArt.Get(_style);
            _root = new GameObject("MenuPanelFitRoot", typeof(RectTransform));
            var rt = (RectTransform)_root.transform;
            rt.sizeDelta = new Vector2(1600f, 800f);
        }

        [TearDown]
        public void TearDown()
        {
            // DestroyImmediate: Object.Destroy is an outright ERROR in Edit Mode.
            if (_root != null) Object.DestroyImmediate(_root);
        }

        private MenuPanelView Panel() =>
            new MenuPanelView(_root.transform, _art, _style, "Title", _style.panelWidth, true);

        [Test]
        public void AShortPanel_TakesExactlyTheHeightItAskedFor()
        {
            var panel = Panel();
            panel.FitToContent(200f);
            Assert.IsFalse(panel.Overflows);
            float expected = _style.titleBarHeight + 8f + 200f + _style.hintBarHeight + _style.panelPadding;
            Assert.AreEqual(expected, panel.Root.sizeDelta.y, 0.01f);
        }

        [Test]
        public void ATallPanel_IsClampedToTheScreen_AndSaysSo()
        {
            var panel = Panel();
            panel.FitToContent(5000f);
            Assert.IsTrue(panel.Overflows, "a panel that cannot fit must report it");
            Assert.AreEqual(MenuPanelView.AvailableHeight(_style), panel.Root.sizeDelta.y, 0.01f);
        }

        /// <summary>
        /// The composition that was actually wrong: where the panel STARTS plus how tall it GREW.
        /// Neither number alone is suspicious.
        /// </summary>
        [Test]
        public void NoPanel_CanReachPastTheBottomOfTheCanvas()
        {
            float canvas = _style.referenceResolution.y > 1f ? _style.referenceResolution.y : 800f;
            foreach (float content in new[] { 100f, 300f, 546f, 646f, 2000f })
            {
                var panel = Panel();
                panel.FitToContent(content);
                float bottom = _style.panelTopOffset + panel.Root.sizeDelta.y;
                Assert.LessOrEqual(bottom, canvas,
                    $"content {content} put the panel's bottom edge at {bottom} on a {canvas} canvas");
                Object.DestroyImmediate(panel.Root.gameObject);
            }
        }

        [Test]
        public void TheAvailableHeight_FollowsTheAnchorItIsMeasuredFrom()
        {
            // Derived, not written down: moving where panels start moves the ceiling with it.
            float canvas = _style.referenceResolution.y > 1f ? _style.referenceResolution.y : 800f;
            Assert.Less(MenuPanelView.AvailableHeight(_style), canvas - _style.panelTopOffset + 1f);
            Assert.Greater(MenuPanelView.AvailableHeight(_style), 160f);
        }

        [Test]
        public void BodyHeight_IsWhatIsLeftAfterTheChrome()
        {
            var panel = Panel();
            panel.FitToContent(200f);
            float chrome = _style.titleBarHeight + 8f + _style.hintBarHeight + _style.panelPadding;
            Assert.AreEqual(panel.Root.sizeDelta.y - chrome, panel.BodyHeight, 0.01f);
        }

        // ── The list's window ────────────────────────────────────────────────

        private MenuList ListOf(int rows, float viewport)
        {
            var body = MenuUIKit.Rect("Body", _root.transform);
            body.sizeDelta = new Vector2(500f, 400f);
            var list = new MenuList(body, _art, _style, reduceMotion: true);
            for (int i = 0; i < rows; i++) list.Add(_art, "Row " + i);
            list.SetViewport(viewport);
            return list;
        }

        [Test]
        public void AListThatFits_DoesNotScroll()
        {
            var list = ListOf(4, 400f);
            Assert.IsFalse(list.Scrolls);
            Assert.AreEqual(0f, list.Scroll, 0.01f);
            foreach (var row in list.Rows)
                Assert.IsTrue(row.Root.gameObject.activeSelf, "a row that fits must stay drawn");
        }

        [Test]
        public void AListThatDoesNot_ScrollsAndHidesWhatIsOutsideTheWindow()
        {
            var list = ListOf(14, 200f);
            Assert.IsTrue(list.Scrolls);

            list.Index = 13;
            Assert.Greater(list.Scroll, 0f, "the window never moved to reach the last row");

            bool anyHidden = false;
            foreach (var row in list.Rows) if (!row.Root.gameObject.activeSelf) anyHidden = true;
            Assert.IsTrue(anyHidden,
                "a row outside the window must be hidden: the body has no mask, so it would be " +
                "drawn over whatever sits behind the panel");
        }

        [Test]
        public void TheSelectedRow_IsAlwaysInsideTheWindow()
        {
            var list = ListOf(14, 200f);
            for (int i = 0; i < list.Count; i++)
            {
                list.Index = i;
                Assert.IsTrue(list.Rows[i].Root.gameObject.activeSelf,
                    $"row {i} is selected and not drawn");
            }
        }

        [Test]
        public void TheWindow_NeverScrollsPastEitherEnd()
        {
            var list = ListOf(14, 200f);
            list.Index = 0;
            Assert.AreEqual(0f, list.Scroll, 0.01f, "scrolled above the first row");
            list.Index = 13;
            Assert.LessOrEqual(list.Scroll, list.ContentHeight - 200f + 0.01f,
                "scrolled past the last row");
        }

        [Test]
        public void MovingDownOneRow_ScrollsByOneRow_NotByAPage()
        {
            // Only the minimum needed, so a keyboard list reads as a list rather than jumping the
            // selection to the middle of the window on every step.
            var list = ListOf(14, 200f);
            list.Index = 0;
            float before = list.Scroll;
            for (int i = 1; i <= 5; i++) list.Index = i;
            float after = list.Scroll;
            Assert.LessOrEqual(after - before, (_style.rowHeight + _style.rowGap) * 5f + 0.01f);
        }
    }
}
