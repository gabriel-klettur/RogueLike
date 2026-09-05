using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay.Chat;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// The Diario's place in the chat panel: the button in the gutter, the overlay over the
    /// conversation, and the arithmetic that keeps the two off everything else.
    ///
    /// <para>Every failure this guards is SILENT. The gutter's children are
    /// <c>ignoreLayout</c> — they must be, or the panel's <c>VerticalLayoutGroup</c>
    /// overwrites their anchors — so nothing arranges them and nothing complains when two of
    /// them land on the same pixels. The panel has paid for that twice already, with a 504x0
    /// language button lying invisibly across the close control. An overlay that reached one
    /// row too high would swallow the close button and the resize grip, and a window with no
    /// way out does not throw either.</para>
    ///
    /// <para>uGUI does not lay out in EditMode, so nothing here measures a resolved rect —
    /// that trap is recorded in CLAUDE.md. These read the AUTHORED values: anchors, sizes,
    /// offsets and the constants they are computed from.</para>
    /// </summary>
    public class ChatJournalPanelTests
    {
        private GameObject _hostGo;
        private ChatUI _ui;
        private readonly List<Object> _fixtureAssets = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            if (ChatUI.HasInstance && ChatUI.Instance != null)
                Object.DestroyImmediate(ChatUI.Instance.gameObject);
            ClearSingleton<ChatUI>();
            ClearSingleton<ChatSystem>();

            _hostGo = new GameObject("ChatUIHost");
            _ui = _hostGo.AddComponent<ChatUI>();
            BuildUI();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hostGo != null) Object.DestroyImmediate(_hostGo);
            _hostGo = null;
            _ui = null;

            foreach (var asset in _fixtureAssets)
                if (asset != null) Object.DestroyImmediate(asset);
            _fixtureAssets.Clear();

            ClearSingleton<ChatUI>();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── The button ──────────────────────────────────────────────────────

        [Test]
        public void JournalButton_IsUnconditional_UnlikeTrade()
        {
            Assert.IsTrue(PanelChild("JournalButton").activeSelf,
                "Five of the six characters in this game do not trade, and every one of them " +
                "can be remembered. A Diario that appeared only for vendors would hide the " +
                "archive on exactly the conversations that are only conversations.");
            Assert.IsFalse(PanelChild("TradeButton").activeSelf,
                "Trade is still per character and is switched on when a vendor is resolved.");
        }

        [Test]
        public void JournalButton_SharesTheGuttersOneEdge()
        {
            var rt = (RectTransform)PanelChild("JournalButton").transform;

            Assert.AreEqual(0f, rt.anchorMin.x, "It anchors to the panel's LEFT edge.");
            Assert.AreEqual(1f, rt.anchorMin.y, "…and to the TOP, because the column stacks downwards.");
            Assert.AreEqual(Const("GUTTER_BUTTON_WIDTH"), rt.sizeDelta.x,
                "The portrait's width exactly, so the gutter reads as one column rather than " +
                "as three things that happen to be on the left.");
        }

        // ── The column ──────────────────────────────────────────────────────

        [Test]
        public void GutterColumn_StacksBelowTheFaceAndBelowTrade()
        {
            var persona = PersonaWithFace();
            PanelChild("TradeButton").SetActive(true);

            ConfigurePortraitFor(persona);

            float expected = -(Const("PANEL_PADDING") + Const("PORTRAIT_SIZE_H") +
                               Const("GUTTER_GAP") + Const("GUTTER_TRADE_HEIGHT") +
                               Const("GUTTER_GAP"));

            Assert.AreEqual(expected, JournalRect().anchoredPosition.y, 0.01f,
                "The full column: face, then Comerciar, then Diario.");
        }

        [Test]
        public void GutterColumn_ClosesUpWhenThereIsNoFaceAndNoShop()
        {
            // Both conditions really vary, and Gatita is both at once — so a column that
            // assumed either would be wrong for most conversations. A gap where a hidden
            // control would have been is invisible in code and obvious on screen.
            PanelChild("TradeButton").SetActive(false);

            ConfigurePortraitFor(null);

            Assert.AreEqual(-Const("PANEL_PADDING"), JournalRect().anchoredPosition.y, 0.01f,
                "With nothing above it, Diario takes the top of the column.");
        }

        [Test]
        public void GutterColumn_TakesTheShopIntoAccountWithoutAFace()
        {
            PanelChild("TradeButton").SetActive(true);

            ConfigurePortraitFor(null);

            float expected = -(Const("PANEL_PADDING") + Const("GUTTER_TRADE_HEIGHT") + Const("GUTTER_GAP"));
            Assert.AreEqual(expected, JournalRect().anchoredPosition.y, 0.01f);
        }

        [Test]
        public void GutterColumn_DoesNotReachTheResetButtonAtTheMinimumPanelHeight()
        {
            // The real constraint behind PANEL_MIN_H, stated independently of how that
            // constant is derived. Reiniciar is pinned to the FOOT of the column and the rest
            // stacks from the top; at the shortest the panel can be dragged, the two must not
            // meet — and if they did, nothing would say so, because neither participates in
            // layout.
            var persona = PersonaWithFace();
            PanelChild("TradeButton").SetActive(true);
            ConfigurePortraitFor(persona);

            float minHeight = Const("PANEL_MIN_H");
            float columnBottom = -JournalRect().anchoredPosition.y + Const("GUTTER_JOURNAL_HEIGHT");
            float resetTop = minHeight - (Const("PANEL_PADDING") + Const("GUTTER_RESET_HEIGHT"));

            Assert.LessOrEqual(columnBottom, resetTop,
                $"At {minHeight}px the gutter stack reaches {columnBottom}px and Reiniciar " +
                $"starts at {resetTop}px. Raise PANEL_MIN_H or shorten a gutter control — an " +
                "overlap here is two buttons on the same pixels, and the one that draws first " +
                "loses its clicks.");
        }

        // ── The overlay ─────────────────────────────────────────────────────

        [Test]
        public void JournalOverlay_IsBuiltHidden()
        {
            Assert.IsFalse(PanelChild("JournalOverlay").activeSelf,
                "A panel that opened on the archive would hide the conversation the player " +
                "just walked up to have.");
        }

        [Test]
        public void JournalOverlay_IgnoresTheLayoutGroupAndStretchesWithThePanel()
        {
            var overlay = PanelChild("JournalOverlay");
            var element = overlay.GetComponent<LayoutElement>();

            Assert.IsTrue(element != null && element.ignoreLayout,
                "Without ignoreLayout the panel's VerticalLayoutGroup claims the rect and " +
                "overwrites the stretch below — the LangButton came out 504x0 that way.");

            var rt = (RectTransform)overlay.transform;
            Assert.AreEqual(Vector2.zero, rt.anchorMin);
            Assert.AreEqual(Vector2.one, rt.anchorMax,
                "Stretched on both axes, so it follows the resize grip and never needs to be " +
                "told a size.");
        }

        [Test]
        public void JournalOverlay_LeavesTheGutterAndTheTitleRowAlone()
        {
            var rt = JournalOverlayRect();

            Assert.AreEqual(Const("PANEL_PADDING") + Const("PORTRAIT_GUTTER"), rt.offsetMin.x, 0.01f,
                "It covers the conversation and not the column: the face stays on screen, so " +
                "the player is plainly still talking to the same person, and Diario itself " +
                "stays clickable so the control that opened the view also closes it.");

            float topInset = -rt.offsetMax.y;
            Assert.GreaterOrEqual(topInset, Const("CORNER_MARGIN") + Const("CORNER_BUTTON_HEIGHT"),
                "It must start below the corner controls. An overlay that swallows the close " +
                "button and the resize grip is a window the player is stuck in, and nothing " +
                "throws to say so.");
            Assert.AreEqual(
                Const("PANEL_PADDING") + Const("TITLE_ROW_HEIGHT") + Const("PANEL_SPACING"),
                topInset, 0.01f,
                "DERIVED from the title row, not typed: a literal here goes stale the moment " +
                "that row changes height.");
        }

        [Test]
        public void JournalOverlay_IsOpaque()
        {
            var image = PanelChild("JournalOverlay").GetComponent<Image>();

            Assert.IsNotNull(image, "It needs a Graphic to be drawn at all — and to block clicks.");
            Assert.AreEqual(1f, image.color.a, 0.001f,
                "The conversation is directly underneath. A translucent sheet leaves two " +
                "transcripts legible at once, which reads as a rendering fault.");
        }

        [Test]
        public void JournalOverlay_CarriesItsOwnScrollView()
        {
            var scroll = Child(PanelChild("JournalOverlay"), "JournalScroll");
            var rect = scroll.GetComponent<ScrollRect>();

            Assert.IsNotNull(rect, "A day can be hundreds of lines long.");
            Assert.IsFalse(rect.horizontal);
            Assert.IsNotNull(rect.content, "Content was never wired — the view would be empty.");
            Assert.AreEqual(rect.viewport, (RectTransform)scroll.transform);
            Assert.IsNotNull(scroll.GetComponent<Mask>(),
                "Unmasked, the rows draw straight over the nav row above them.");
        }

        [Test]
        public void JournalNavRow_RefusesToCompeteWithTheTranscriptForHeight()
        {
            var nav = Child(PanelChild("JournalOverlay"), "JournalNav").GetComponent<LayoutElement>();

            Assert.AreEqual(Const("JOURNAL_NAV_HEIGHT"), nav.preferredHeight, 0.01f);
            Assert.AreEqual(0f, nav.flexibleHeight,
                "preferredHeight alone leaves flexibleHeight at its unset -1, and the value " +
                "actually used then comes from the HorizontalLayoutGroup on this same object " +
                "— which reports 1 while childForceExpandHeight is on. The chat input row " +
                "shipped exactly that bug and ate 80px for a 32px control.");
        }

        [Test]
        public void JournalNavRow_IsNamedIndependentlyOfTheLanguage()
        {
            // CreateInlineButton names its object after the LABEL, so without an explicit
            // rename the hierarchy would be shaped by whichever language the player is in.
            var nav = Child(PanelChild("JournalOverlay"), "JournalNav");

            foreach (string name in new[] { "JournalOlderButton", "JournalNewerButton", "JournalBackButton" })
                Assert.IsNotNull(nav.transform.Find(name), $"'{name}' is missing from the nav row.");
        }

        [Test]
        public void JournalButton_WithNoChatSystem_DoesNotThrow()
        {
            // Every other button on this panel is pinned the same way: BuildUI runs from
            // Start and the callbacks can fire before ChatSystem exists, or after it is gone.
            var button = PanelChild("JournalButton").GetComponent<Button>();

            Assert.DoesNotThrow(() => button.onClick.Invoke());
            Assert.IsFalse(_ui.IsJournalOpen,
                "With nowhere to read an archive from, the view must decline to open rather " +
                "than showing an empty one.");
        }

        [Test]
        public void JournalBackButton_WithNoChatSystem_DoesNotThrow()
        {
            var back = Child(Child(PanelChild("JournalOverlay"), "JournalNav"), "JournalBackButton")
                .GetComponent<Button>();

            Assert.DoesNotThrow(() => back.onClick.Invoke());
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private void BuildUI()
        {
            var mi = typeof(ChatUI).GetMethod("BuildUI", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mi, "ChatUI.BuildUI() was renamed or removed.");
            mi.Invoke(_ui, null);
        }

        private void ConfigurePortraitFor(NPCPersonaDefinition persona)
        {
            var mi = typeof(ChatUI).GetMethod(
                "ConfigurePortraitFor", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mi, "ChatUI.ConfigurePortraitFor() was renamed or removed.");
            mi.Invoke(_ui, new object[] { persona });
        }

        private NPCPersonaDefinition PersonaWithFace()
        {
            var persona = ScriptableObject.CreateInstance<NPCPersonaDefinition>();
            var tex = new Texture2D(2, 2);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
            persona.portrait = sprite;

            _fixtureAssets.Add(tex);
            _fixtureAssets.Add(sprite);
            _fixtureAssets.Add(persona);
            return persona;
        }

        private GameObject Panel()
        {
            var fi = typeof(ChatUI).GetField("_panel", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(fi, "ChatUI._panel was renamed or removed.");
            var panel = (GameObject)fi.GetValue(_ui);
            Assert.IsNotNull(panel, "BuildUI never created the panel.");
            return panel;
        }

        private GameObject PanelChild(string name) => Child(Panel(), name);

        private static GameObject Child(GameObject parent, string name)
        {
            Transform t = parent.transform.Find(name);
            Assert.IsNotNull(t, $"'{name}' is missing from '{parent.name}'.");
            return t.gameObject;
        }

        private RectTransform JournalRect() => (RectTransform)PanelChild("JournalButton").transform;

        private RectTransform JournalOverlayRect() =>
            (RectTransform)PanelChild("JournalOverlay").transform;

        private static float Const(string name)
        {
            var fi = typeof(ChatUI).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(fi, $"ChatUI.{name} was renamed or removed.");
            return (float)fi.GetValue(null);
        }

        private static void ClearSingleton<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var fi = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (fi != null) { fi.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }
    }
}
