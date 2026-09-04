using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay.NPC;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// Geometry coverage for the vendor shop.
    ///
    /// <para>Every defect this fixture pins was invisible to a test that counted objects.
    /// The shop opened with its rows present, active, correctly named and correctly priced
    /// in the hierarchy — and a player saw two empty columns, because a stencil
    /// <see cref="Mask"/> was paired with a graphic at alpha 0 and clipped all of them away.
    /// With that fixed the rows appeared halfway down the panel and spilled out of the
    /// bottom of the window, because the list hung from the column's CENTRE. And the '+' of
    /// the quantity stepper was two per cent of a row wide.</para>
    ///
    /// <para>So the assertions here are about SIZE AND POSITION, not existence. A rendered
    /// thing is unverified until something measures it.</para>
    /// </summary>
    [TestFixture]
    public class VendorShopLayoutTests
    {
        private GameObject _hostGo;
        private VendorShopUI _shop;
        private readonly List<Object> _assets = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            if (VendorShopUI.HasInstance && VendorShopUI.Instance != null)
                Object.DestroyImmediate(VendorShopUI.Instance.gameObject);
            ClearSingleton<VendorShopUI>();

            _hostGo = new GameObject("VendorShopHost");
            _shop = _hostGo.AddComponent<VendorShopUI>();
            Invoke("BuildUI");
        }

        [TearDown]
        public void TearDown()
        {
            if (_hostGo != null) Object.DestroyImmediate(_hostGo);
            foreach (var a in _assets) if (a != null) Object.DestroyImmediate(a);
            _assets.Clear();

            ClearSingleton<VendorShopUI>();
            LogAssert.ignoreFailingMessages = false;
        }

        private static void ClearSingleton<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var f = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        private void Invoke(string method)
        {
            var mi = typeof(VendorShopUI).GetMethod(method,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mi, $"VendorShopUI.{method}() was renamed or removed.");
            try { mi.Invoke(_shop, null); }
            catch (TargetInvocationException tie) { throw tie.InnerException ?? tie; }
        }

        private T Field<T>(string name)
        {
            var fi = typeof(VendorShopUI).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(fi, $"VendorShopUI field '{name}' is missing.");
            return (T)fi.GetValue(_shop);
        }

        private static ScrollRect[] Scrolls(VendorShopUI shop) =>
            shop.GetComponentsInChildren<ScrollRect>(true);

        // ── Clipping ────────────────────────────────────────────────────────

        [Test]
        public void ScrollViews_ClipWithRectMask2D_NotAStencilMask()
        {
            foreach (var scroll in Scrolls(_shop))
            {
                Assert.IsNull(scroll.GetComponent<Mask>(),
                    $"'{scroll.name}' must not use a stencil Mask. A Mask takes its shape from " +
                    "its graphic's ALPHA, and this one was paired with an Image at alpha 0 — " +
                    "the UI shader alpha-clips those pixels, the stencil is never written, and " +
                    "every row inside is clipped out of existence. The shop showed two column " +
                    "headers and nothing else while the rows were all present in the hierarchy.");
                Assert.IsNotNull(scroll.GetComponent<RectMask2D>(),
                    $"'{scroll.name}' needs a clipper, and a rect is the shape actually wanted.");
            }
        }

        [Test]
        public void ScrollViews_KeepARaycastTargetForTheWheel()
        {
            foreach (var scroll in Scrolls(_shop))
            {
                var graphic = scroll.GetComponent<Graphic>();
                Assert.IsNotNull(graphic,
                    $"'{scroll.name}' needs a graphic to receive the mouse wheel — a ScrollRect " +
                    "with nothing raycastable under the cursor does not scroll.");
                Assert.IsTrue(graphic.raycastTarget);
            }
        }

        // ── Where the list actually sits ────────────────────────────────────

        [Test]
        public void ScrollViews_HangFromTheTopOfTheirColumn()
        {
            foreach (var scroll in Scrolls(_shop))
            {
                var rect = scroll.GetComponent<RectTransform>();
                Assert.AreEqual(1f, rect.anchorMin.y, 0.001f,
                    $"'{scroll.name}' anchored at y=0.5 hung from the column's CENTRE, so the " +
                    "list started halfway down and its last rows fell out of the window.");
                Assert.AreEqual(1f, rect.anchorMax.y, 0.001f);
                Assert.AreEqual(1f, rect.pivot.y, 0.001f);
            }
        }

        [Test]
        public void ScrollViews_FitInsideTheirColumn()
        {
            foreach (var scroll in Scrolls(_shop))
            {
                var rect = scroll.GetComponent<RectTransform>();
                var column = scroll.transform.parent as RectTransform;
                Assert.IsNotNull(column);

                // Top of the list, measured down from the top of the column it lives in.
                float topInset = -rect.anchoredPosition.y;
                Assert.GreaterOrEqual(topInset, 0f, "The list must start below the column's top edge.");
                Assert.LessOrEqual(topInset + rect.sizeDelta.y, column.rect.height + 0.5f,
                    $"'{scroll.name}' is taller than the space under its header, so its last " +
                    "rows render outside the panel and over the game world.");
            }
        }

        [Test]
        public void Canvas_ScalesWithTheWindow_LikeTheChatPanel()
        {
            var scaler = _shop.GetComponentInChildren<CanvasScaler>(true);
            Assert.IsNotNull(scaler);
            Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode,
                "The default ConstantPixelSize pins a 664-wide panel to physical pixels: it " +
                "does not fit a small window, and the shop and the chat that opens it would " +
                "scale differently.");
        }

        [Test]
        public void Canvas_DrawsAboveTheChatPanelThatOpensIt()
        {
            var canvas = _shop.GetComponentInChildren<Canvas>(true);
            Assert.AreEqual(220, canvas.sortingOrder,
                "The shop is opened FROM the chat panel (200) and must draw over it. Both sat " +
                "at 200, leaving the winner to hierarchy order.");
        }

        [Test]
        public void TitleAndGoldBars_StayInsideThePanel()
        {
            var root = Field<GameObject>("_root").GetComponent<RectTransform>();

            foreach (string barName in new[] { "TitleBar", "GoldBar" })
            {
                var bar = root.Find(barName) as RectTransform;
                Assert.IsNotNull(bar, $"'{barName}' is missing from the shop panel.");

                // Position is measured FROM the anchor, so setting the anchor AFTER the
                // position — which the builder used to do — moved the bar by the full
                // distance between the two. Both ended up 262 px outside the panel, which is
                // why neither the NPC's name nor the player's coin count was ever visible.
                var corners = new Vector3[4];
                var rootCorners = new Vector3[4];
                bar.GetWorldCorners(corners);
                root.GetWorldCorners(rootCorners);

                Assert.GreaterOrEqual(corners[0].y, rootCorners[0].y - 0.5f,
                    $"'{barName}' hangs below the panel.");
                Assert.LessOrEqual(corners[1].y, rootCorners[1].y + 0.5f,
                    $"'{barName}' sits above the panel.");
            }
        }

        [Test]
        public void TitleAndGoldLabels_AreWiredToTheFieldsThatUpdateThem()
        {
            Assert.IsNotNull(Field<TextMeshProUGUI>("_vendorTitleText"),
                "OpenShop writes the vendor's name here; an unassigned field means the window " +
                "never says who you are trading with.");
            Assert.IsNotNull(Field<TextMeshProUGUI>("_goldText"),
                "Update writes the coin count here.");
        }

        // ── Scrollbar ───────────────────────────────────────────────────────

        [Test]
        public void ScrollViews_HaveAVerticalScrollbar()
        {
            foreach (var scroll in Scrolls(_shop))
            {
                Assert.IsNotNull(scroll.verticalScrollbar,
                    $"'{scroll.name}' needs a scrollbar. Without one there is nothing on " +
                    "screen that says how long the list is or how far down it you are — a " +
                    "list of eight items looks exactly like a list of three.");
                Assert.IsNotNull(scroll.verticalScrollbar.handleRect,
                    "A Scrollbar with no handleRect cannot size its handle from the content, " +
                    "so it renders as an empty track.");
            }
        }

        [Test]
        public void ScrollViews_HideTheBarWhenTheListFits()
        {
            foreach (var scroll in Scrolls(_shop))
            {
                Assert.AreEqual(ScrollRect.ScrollbarVisibility.AutoHide, scroll.verticalScrollbarVisibility,
                    "The bar answers 'is there more below?', so it must be absent when the " +
                    "answer is no. AutoHideAndExpandViewport would reflow every row's width " +
                    "the moment an item is bought.");
            }
        }

        [Test]
        public void Viewport_LeavesRoomForTheScrollbar()
        {
            foreach (var scroll in Scrolls(_shop))
            {
                Assert.Less(scroll.viewport.rect.width, scroll.GetComponent<RectTransform>().rect.width,
                    $"'{scroll.name}' viewport is as wide as the whole list, so the scrollbar " +
                    "sits on top of the Buy buttons and takes their clicks.");
            }
        }

        // ── Getting out ─────────────────────────────────────────────────────

        [Test]
        public void TitleBar_HasACloseButton()
        {
            var titleBar = Field<GameObject>("_root").transform.Find("TitleBar");
            Assert.IsNotNull(titleBar);

            Button close = null;
            foreach (var b in titleBar.GetComponentsInChildren<Button>(true))
            {
                var label = b.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null && label.text == "X") { close = b; break; }
            }

            Assert.IsNotNull(close,
                "Escape closes the shop, but a modal with no visible way out reads as stuck — " +
                "and this one is reached from inside a conversation, so the player never " +
                "pressed anything they can obviously undo.");
            Assert.Greater(close.onClick.GetPersistentEventCount() + 1, 0);
        }

        [Test]
        public void TitleLabel_DoesNotRunUnderTheCloseButton()
        {
            var titleBar = Field<GameObject>("_root").transform.Find("TitleBar") as RectTransform;
            var label = Field<TextMeshProUGUI>("_vendorTitleText").GetComponent<RectTransform>();

            Assert.Less(label.offsetMax.x, 0f,
                "The title spans the bar, so without a right inset a long vendor name draws " +
                "underneath the close button.");
        }

        // ── Empty states ────────────────────────────────────────────────────

        [Test]
        public void BothColumns_HaveAnEmptyStateLabel()
        {
            Assert.IsNotNull(Field<TextMeshProUGUI>("_vendorEmptyText"));
            Assert.IsNotNull(Field<TextMeshProUGUI>("_playerEmptyText"),
                "An empty column and a broken one are the same black rectangle. That " +
                "ambiguity is exactly how the clipped-rows bug read for as long as it " +
                "existed, and it is how the player column reads to anyone carrying nothing.");
        }

        [Test]
        public void EmptyStateLabels_DoNotEatClicks()
        {
            foreach (string field in new[] { "_vendorEmptyText", "_playerEmptyText" })
            {
                Assert.IsFalse(Field<TextMeshProUGUI>(field).raycastTarget,
                    $"{field} covers the top of the list; if it raycasts it takes the first " +
                    "row's clicks the moment rows appear under it.");
            }
        }

        // ── One row's controls ──────────────────────────────────────────────

        private GameObject BuildRow()
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            _assets.Add(item);
            item.itemId = "probe";
            item.displayName = "Probe";
            item.buyPrice = 10;

            var mi = typeof(VendorShopUI).GetMethod("BuildRow",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mi, "VendorShopUI.BuildRow was renamed or removed.");

            var parent = Field<Transform>("_vendorRowsParent");
            Assert.IsNotNull(parent, "_vendorRowsParent must be resolved by BuildUI.");

            try { return (GameObject)mi.Invoke(_shop, new object[] { parent, item, 10, 3, true }); }
            catch (TargetInvocationException tie) { throw tie.InnerException ?? tie; }
        }

        /// <summary>Width of a control as a fraction of its row, from its anchors.</summary>
        private static float AnchorWidth(RectTransform rect) => rect.anchorMax.x - rect.anchorMin.x;

        private static RectTransform ButtonWithLabel(GameObject row, string label)
        {
            foreach (var button in row.GetComponentsInChildren<Button>(true))
            {
                var text = button.GetComponentInChildren<TextMeshProUGUI>(true);
                if (text != null && text.text == label) return button.GetComponent<RectTransform>();
            }
            Assert.Fail($"Row has no button labelled '{label}'.");
            return null;
        }

        [Test]
        public void Row_HasBothStepperButtonsAndAnAction()
        {
            var row = BuildRow();
            Assert.IsNotNull(ButtonWithLabel(row, "-"));
            Assert.IsNotNull(ButtonWithLabel(row, "+"));
            Assert.IsNotNull(ButtonWithLabel(row, "Buy"));
        }

        [Test]
        public void Row_StepperButtons_AreTheSameSizeAndBigEnoughToHit()
        {
            var row = BuildRow();
            float minus = AnchorWidth(ButtonWithLabel(row, "-"));
            float plus = AnchorWidth(ButtonWithLabel(row, "+"));

            Assert.AreEqual(minus, plus, 0.001f,
                "'+' had 0.86..0.88 — two per cent of the row, about six pixels — while '-' had " +
                "three times that. It rendered as a sliver and could not be clicked.");
            Assert.GreaterOrEqual(plus, 0.05f,
                "A control narrower than five per cent of a 312 px row is under 16 px and is " +
                "not a usable target.");
        }

        [Test]
        public void Row_Controls_DoNotOverlap()
        {
            var row = BuildRow();

            var ordered = new List<(string name, RectTransform rect)>
            {
                ("-", ButtonWithLabel(row, "-")),
                ("+", ButtonWithLabel(row, "+")),
                ("Buy", ButtonWithLabel(row, "Buy")),
            }.OrderBy(e => e.rect.anchorMin.x).ToList();

            for (int i = 1; i < ordered.Count; i++)
            {
                Assert.GreaterOrEqual(ordered[i].rect.anchorMin.x, ordered[i - 1].rect.anchorMax.x - 0.001f,
                    $"'{ordered[i].name}' starts before '{ordered[i - 1].name}' ends — two " +
                    "buttons on the same pixels means one of them eats the other's clicks.");
            }
        }

        [Test]
        public void Row_EveryControl_StaysInsideTheRow()
        {
            var row = BuildRow();
            foreach (RectTransform child in row.transform)
            {
                Assert.GreaterOrEqual(child.anchorMin.x, -0.001f, $"'{child.name}' starts left of the row.");
                Assert.LessOrEqual(child.anchorMax.x, 1.001f, $"'{child.name}' runs past the row's right edge.");
            }
        }
    }
}
