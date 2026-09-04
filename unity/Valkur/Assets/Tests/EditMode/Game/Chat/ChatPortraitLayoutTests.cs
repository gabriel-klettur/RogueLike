using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay.Chat;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// The portrait's place in the chat panel: that it exists, that the panel's layout group
    /// cannot claim it, and that the gutter it sits in appears only for a character who has
    /// a face to put there.
    ///
    /// <para>Every assertion here is about a failure that is INVISIBLE in code. A portrait
    /// the <c>VerticalLayoutGroup</c> owns still exists, still holds the right sprite, and is
    /// drawn as a full-width strip across the panel — which is what happened to the
    /// <c>LangButton</c>, at 504x0, where it silently ate the close button's clicks. And a
    /// gutter reserved for a character with no art is an empty rectangle beside five of the
    /// six conversations in this game, which reads as a portrait that failed to load rather
    /// than as one that was never authored.</para>
    ///
    /// <para>uGUI does not lay out in EditMode, so nothing here measures a resolved rect —
    /// that trap is recorded in CLAUDE.md and cost a whole test file once. These read the
    /// AUTHORED values instead: anchors, pivots, the ignoreLayout flag and the padding the
    /// layout group was handed.</para>
    /// </summary>
    public class ChatPortraitLayoutTests
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

        // ── The portrait itself ─────────────────────────────────────────────

        [Test]
        public void BuildUI_CreatesThePortrait_Hidden()
        {
            GameObject portrait = FindPortrait();

            Assert.IsNotNull(portrait, "BuildUI must create the Portrait object.");
            Assert.IsFalse(portrait.activeSelf,
                "It starts hidden. Whether a conversation shows it at all is decided per " +
                "character by ConfigurePortraitFor, not by the builder.");
        }

        [Test]
        public void Portrait_IgnoresTheLayoutGroup()
        {
            var le = FindPortrait().GetComponent<LayoutElement>();

            Assert.IsNotNull(le, "The portrait needs a LayoutElement to opt out with.");
            Assert.IsTrue(le.ignoreLayout,
                "Without ignoreLayout the panel's VerticalLayoutGroup claims the rect and " +
                "overwrites the anchors every rebuild — the LangButton came out 504x0 that " +
                "way, an invisible full-width strip eating the close button's clicks.");
        }

        [Test]
        public void Portrait_IsPinnedToThePanelsTopLeftCorner()
        {
            var rt = (RectTransform)FindPortrait().transform;

            Assert.AreEqual(new Vector2(0f, 1f), rt.anchorMin);
            Assert.AreEqual(new Vector2(0f, 1f), rt.anchorMax);
            Assert.AreEqual(new Vector2(0f, 1f), rt.pivot,
                "Anchored AND pivoted top-left, so the face stays put when the player drags " +
                "the panel bigger from its own top-right grip.");
            Assert.Greater(rt.sizeDelta.x, 0f);
            Assert.Greater(rt.sizeDelta.y, 0f);
        }

        [Test]
        public void Portrait_FitsInsideTheGutterItReserves()
        {
            var rt = (RectTransform)FindPortrait().transform;
            float gutter = Const("PORTRAIT_GUTTER");
            float padding = Const("PANEL_PADDING");

            Assert.LessOrEqual(rt.sizeDelta.x, gutter,
                $"The face is {rt.sizeDelta.x} wide in a {gutter} gutter, so it reaches into " +
                "the conversation column. The gutter is what the layout padding reserves; " +
                "anything wider draws over the message rows.");
            Assert.AreEqual(padding, rt.anchoredPosition.x, 0.01f);
            Assert.AreEqual(-padding, rt.anchoredPosition.y, 0.01f,
                "Top inset is negative because the pivot is at the top.");
        }

        [Test]
        public void Portrait_HasTwoStackedImages_ForTheCrossfade()
        {
            GameObject portrait = FindPortrait();
            var layers = portrait.GetComponentsInChildren<Image>(includeInactive: true);

            // The frame lives on the root, the two faces on children.
            Assert.AreEqual(3, layers.Length,
                "Frame plus two face layers. A crossfade needs both an outgoing and an " +
                "incoming image; one Image can only cut.");
            Assert.AreEqual(2, portrait.transform.childCount);
        }

        [Test]
        public void Portrait_IncomingLayerDrawsOverTheOutgoingOne()
        {
            Transform portrait = FindPortrait().transform;

            Assert.AreEqual("Back", portrait.GetChild(0).name);
            Assert.AreEqual("Front", portrait.GetChild(1).name,
                "uGUI draws in sibling order, so the face fading IN has to be the later " +
                "child or the dissolve runs backwards.");
        }

        [Test]
        public void Portrait_FaceLayersPreserveAspect()
        {
            foreach (Transform child in FindPortrait().transform)
            {
                var img = child.GetComponent<Image>();
                Assert.IsTrue(img.preserveAspect,
                    $"{child.name} would stretch the drawing to the frame. The faces are " +
                    "370x395 and the gutter is whatever the panel can spare — the two are " +
                    "not the same shape and never will be for every character.");
            }
        }

        [Test]
        public void Portrait_IsBuiltLast_SoItDrawsOverTheRowsBesideIt()
        {
            Transform panel = FindPortrait().transform.parent;

            Assert.AreEqual(panel.childCount - 1, FindPortrait().transform.GetSiblingIndex(),
                "The portrait occupies the gutter those rows were shortened to make. Built " +
                "earlier it would render underneath them.");
        }

        // ── The gutter ──────────────────────────────────────────────────────

        [Test]
        public void Gutter_IsStillReservedForACharacterWithNoFaceArt_ButShowsNoFrame()
        {
            var persona = ScriptableObject.CreateInstance<NPCPersonaDefinition>();
            _fixtureAssets.Add(persona);

            ConfigurePortraitFor(persona);

            // This assertion is the REVERSE of what it used to be, and the reversal is the
            // point. While the gutter held only a face, reserving it for a character without
            // one put an empty rectangle beside five of the six conversations in the game,
            // which reads as a portrait that failed to load. It is a COLUMN now — Comerciar
            // under the face, Reiniciar at its foot — so there is no conversation in which
            // it is empty, and nothing left for the old rule to protect against.
            Assert.AreEqual(
                (int)(Const("PANEL_PADDING") + Const("PORTRAIT_GUTTER")),
                PanelLayout().padding.left,
                "The gutter is unconditional now: it carries the controls, not just a face.");

            Assert.IsFalse(FindPortrait().activeSelf,
                "The FACE is still per conversation. A character with no art must show no " +
                "frame — an empty picture frame is exactly the failed-to-load read the old " +
                "rule existed to avoid, and it is the half of it that still applies.");
        }

        [Test]
        public void Gutter_IsReservedForACharacterThatHasFaces()
        {
            var persona = ScriptableObject.CreateInstance<NPCPersonaDefinition>();
            persona.portrait = MakeSprite("Face");
            _fixtureAssets.Add(persona);

            ConfigurePortraitFor(persona);

            Assert.AreEqual(
                (int)(Const("PANEL_PADDING") + Const("PORTRAIT_GUTTER")),
                PanelLayout().padding.left,
                "The space is reserved by widening the layout group's LEFT padding, which " +
                "is what makes every existing row shorten by itself with no row re-parented.");
            Assert.IsTrue(FindPortrait().activeSelf);
        }

        [Test]
        public void Face_IsTakenDownAgainWhenTheNextCharacterHasNoArt()
        {
            var withArt = ScriptableObject.CreateInstance<NPCPersonaDefinition>();
            withArt.portrait = MakeSprite("Face");
            var without = ScriptableObject.CreateInstance<NPCPersonaDefinition>();
            _fixtureAssets.Add(withArt);
            _fixtureAssets.Add(without);

            ConfigurePortraitFor(withArt);
            ConfigurePortraitFor(without);

            Assert.IsFalse(FindPortrait().activeSelf,
                "The panel is reused across conversations. A face left up from the previous " +
                "one puts the wrong character's head beside this one's words.");

            Assert.AreEqual(
                (int)(Const("PANEL_PADDING") + Const("PORTRAIT_GUTTER")),
                PanelLayout().padding.left,
                "The gutter itself does NOT come and go — it holds the controls either way, " +
                "and a column that appeared and vanished between conversations would move " +
                "every row sideways as the player walked from one villager to the next.");
        }

        [Test]
        public void TradeButton_MovesUpTheColumnWhenThereIsNoFace()
        {
            var withArt = ScriptableObject.CreateInstance<NPCPersonaDefinition>();
            withArt.portrait = MakeSprite("Face");
            var without = ScriptableObject.CreateInstance<NPCPersonaDefinition>();
            _fixtureAssets.Add(withArt);
            _fixtureAssets.Add(without);

            var trade = (RectTransform)FindPortrait().transform.parent.Find("TradeButton");
            Assert.IsNotNull(trade, "TradeButton lives in the gutter, beside the portrait.");

            ConfigurePortraitFor(withArt);
            float underFace = trade.anchoredPosition.y;

            ConfigurePortraitFor(without);
            float atTop = trade.anchoredPosition.y;

            // Anchored to the panel's TOP edge, so both are negative and "higher" is nearer 0.
            Assert.Greater(atTop, underFace,
                "With no face to sit under, the button takes the top of the column. Leaving " +
                "it where the portrait would have been opens a 94px hole above the only way " +
                "into a vendor's counter — and five of the six vendors have no art.");
            Assert.AreEqual(-Const("PANEL_PADDING"), atTop, 0.01f,
                "At the top of the column it clears the panel edge by the same padding " +
                "every other floating child uses.");
        }

        [Test]
        public void PanelMinimumWidth_IsNotRaisedByTheGutter()
        {
            float gutter = Const("PORTRAIT_GUTTER");
            float minW = Const("PANEL_MIN_W");

            Assert.Greater(minW - gutter, 0f,
                "At its minimum the panel still has to hold a conversation beside the face.");
            Assert.AreEqual(470f, minW,
                "PANEL_MIN_W tracks PORTRAIT_GUTTER now, and the reason is that the gutter " +
                "is UNCONDITIONAL: every conversation spends it before a word of dialogue is " +
                "placed. While the gutter came and went per character, refusing to raise " +
                "this was right — it would have clamped a size the player saved on a " +
                "portrait-less NPC upward the moment they talked to Gatita, and a " +
                "per-character minimum would be static mutable state on a class where " +
                "Domain Reload is off. Neither hazard exists once the column is always there.");

            Assert.Less(gutter, minW * 0.5f,
                "A gutter worth half the minimum panel would leave the conversation with " +
                "less room than the face beside it.");
        }

        // ── Reflection helpers ──────────────────────────────────────────────

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

        private GameObject FindPortrait()
        {
            var fi = typeof(ChatUI).GetField("_portraitRoot", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(fi, "ChatUI._portraitRoot was renamed or removed.");
            return (GameObject)fi.GetValue(_ui);
        }

        private VerticalLayoutGroup PanelLayout()
        {
            var fi = typeof(ChatUI).GetField("_panelLayout", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(fi, "ChatUI._panelLayout was renamed or removed — the gutter " +
                                 "cannot be reserved or released without it.");
            var vlg = (VerticalLayoutGroup)fi.GetValue(_ui);
            Assert.IsNotNull(vlg, "The builder never assigned _panelLayout.");
            return vlg;
        }

        private static float Const(string name)
        {
            var fi = typeof(ChatUI).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(fi, $"ChatUI.{name} was renamed or removed.");
            return (float)fi.GetValue(null);
        }

        private Sprite MakeSprite(string name)
        {
            var tex = new Texture2D(2, 2);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            _fixtureAssets.Add(tex);
            _fixtureAssets.Add(sprite);
            return sprite;
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
