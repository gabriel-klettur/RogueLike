using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Gameplay.Chat;

namespace Valkur.Tests.EditMode.Gameplay.Chat
{
    /// <summary>ChatUIBuilderTests: the robustness tests. SetUp, TearDown and helpers live in ChatUIBuilderTests.cs.</summary>
    public partial class ChatUIBuilderTests
    {
        [Test]
        public void BuildUI_Always_AssignsEveryReferenceDereferencedByChatUI()
        {
            // Exactly the fields ChatUI.cs uses without a null guard, in
            // OnChatOpened / OnChatClosed / OnMessageReceived / SubmitInput.
            Assert.IsTrue(Field<Canvas>("_canvas") != null, "_canvas must be assigned by BuildUI.");
            Assert.IsTrue(Field<GameObject>("_panel") != null, "_panel is dereferenced by OnChatOpened/OnChatClosed.");
            Assert.IsTrue(Field<GameObject>("_backdrop") != null, "_backdrop is toggled together with the panel.");
            Assert.IsTrue(Field<ScrollRect>("_scrollRect") != null, "_scrollRect is used for the auto-scroll on new messages.");
            Assert.IsTrue(Field<RectTransform>("_contentRect") != null, "_contentRect parents every message row.");
            Assert.IsTrue(Field<TMP_InputField>("_inputField") != null, "_inputField is read in Update and SubmitInput.");
            Assert.IsTrue(Field<TextMeshProUGUI>("_titleText") != null, "_titleText is written in OnChatOpened.");
            Assert.IsTrue(Field<TextMeshProUGUI>("_langButtonText") != null, "_langButtonText is written in ToggleLang.");
        }

        [Test]
        public void BuildUI_Always_LeavesMessageRowListEmpty()
        {
            var rows = Field<List<GameObject>>("_messageRows");

            Assert.IsNotNull(rows, "_messageRows must be initialised before any message can arrive.");
            Assert.AreEqual(0, rows.Count,
                "BuildUI must not seed message rows; history is replayed by OnChatOpened instead.");
        }

        // -------------------------------------------------------------------------
        // The Image + TMP separation rule, enforced over the whole tree
        // -------------------------------------------------------------------------

        [Test]
        public void BuildUI_Always_KeepsImageAndTextMeshProOnSeparateGameObjects()
        {
            var offenders = new List<string>();
            foreach (var t in CanvasGo.GetComponentsInChildren<Transform>(true))
            {
                var go = t.gameObject;
                if (go.GetComponent<Image>() != null && go.GetComponent<TextMeshProUGUI>() != null)
                    offenders.Add(go.name);
            }

            CollectionAssert.IsEmpty(offenders,
                "Image + TextMeshProUGUI on one GameObject throws an NRE in Unity 2022.3 - the label must live "
                + "on a child. Offending objects: " + string.Join(", ", offenders));
        }

        // -------------------------------------------------------------------------
        // Idempotency (the _isBuilt guard)
        // -------------------------------------------------------------------------

        [Test]
        public void BuildUI_CalledTwice_DoesNotDuplicateOrRebuildTheHierarchy()
        {
            var canvasBefore = CanvasGo;
            var panelBefore = Panel;
            var inputBefore = Field<TMP_InputField>("_inputField");
            int canvasChildrenBefore = canvasBefore.transform.childCount;
            int panelChildrenBefore = panelBefore.transform.childCount;

            BuildUI();

            Assert.AreEqual(1, _hostGo.transform.childCount,
                "A second BuildUI must not create a second ChatCanvas - that is what the _isBuilt guard is for.");
            Assert.AreSame(canvasBefore, CanvasGo, "The canvas reference must survive a repeated BuildUI call.");
            Assert.AreSame(panelBefore, Panel, "The panel reference must survive a repeated BuildUI call.");
            Assert.AreSame(inputBefore, Field<TMP_InputField>("_inputField"),
                "Rebuilding would orphan the input field that the Enter handler still points at.");
            Assert.AreEqual(canvasChildrenBefore, canvasBefore.transform.childCount,
                "Canvas children must not be duplicated by a second BuildUI call.");
            Assert.AreEqual(panelChildrenBefore, panelBefore.transform.childCount,
                "Panel children must not be duplicated by a second BuildUI call.");
        }

        [Test]
        public void BuildUI_CalledTwice_LeavesExactlyOneObjectPerNamedNode()
        {
            BuildUI();

            var counts = new Dictionary<string, int>();
            foreach (var t in CanvasGo.GetComponentsInChildren<Transform>(true))
            {
                counts.TryGetValue(t.name, out int n);
                counts[t.name] = n + 1;
            }

            foreach (var name in new[] { "Backdrop", "ChatPanel", "ScrollArea", "Content",
                                         "InputRow", "InputField", "SendButton",
                                         "TradeButton", "JournalButton", "ResetButton", "CloseXButton",
                                         "LangButton", "LangLabel", "Text Area", "Placeholder",
                                         "JournalOverlay", "JournalScroll", "JournalContent",
                                         "QuestsButton", "QuestOverlay", "QuestScroll", "QuestContent" })
            {
                Assert.IsTrue(counts.ContainsKey(name), "Node '" + name + "' disappeared from the hierarchy.");
                Assert.AreEqual(1, counts[name],
                    "Node '" + name + "' must exist exactly once - a duplicate means BuildUI really ran twice.");
            }
        }

        // -------------------------------------------------------------------------
        // Button callbacks must survive a missing ChatSystem
        // -------------------------------------------------------------------------

        [Test]
        public void CloseAndBackdropButtons_WithNoChatSystem_DoNotThrow()
        {
            // Both handlers rely on ChatSystem.Instance?.CloseChat(). Losing the null-conditional
            // would throw on any click made before ChatSystem exists in the scene.
            Assert.IsFalse(ChatSystem.HasInstance,
                "SetUp clears the ChatSystem singleton; a live one here means another "
                + "fixture leaked it and the null-guard paths below would not be exercised.");

            var closeBtn = Child(Panel, "CloseXButton").GetComponent<Button>();
            var backdropBtn = Backdrop.GetComponent<Button>();

            Assert.DoesNotThrow(() => closeBtn.onClick.Invoke(),
                "The close button must tolerate a missing ChatSystem.");
            Assert.DoesNotThrow(() => backdropBtn.onClick.Invoke(),
                "Backdrop click-to-close must tolerate a missing ChatSystem.");
        }

        [Test]
        public void SendButton_WithEmptyInputAndNoChatSystem_DoesNotThrowOrAppendARow()
        {
            Assert.IsFalse(ChatSystem.HasInstance,
                "SetUp clears the ChatSystem singleton; a live one here means another "
                + "fixture leaked it and the null-guard paths below would not be exercised.");

            var sendBtn = Child(Panel, "InputRow/SendButton").GetComponent<Button>();

            // SubmitInput() must early-out on empty text before it ever touches ChatSystem.
            Assert.DoesNotThrow(() => sendBtn.onClick.Invoke(),
                "Sending an empty message must be a no-op, not an exception.");
            Assert.AreEqual(0, Field<List<GameObject>>("_messageRows").Count,
                "An empty submit must not append a message row.");
        }

        [Test]
        public void LangButton_WithNoActiveMemory_DoesNotThrowAndKeepsLabel()
        {
            Assert.IsFalse(ChatSystem.HasInstance,
                "SetUp clears the ChatSystem singleton; a live one here means another "
                + "fixture leaked it and the null-guard paths below would not be exercised.");

            var langBtn = Child(Panel, "LangButton").GetComponent<Button>();

            Assert.DoesNotThrow(() => langBtn.onClick.Invoke(),
                "ToggleLang must early-out when there is no active NPC memory to persist the change into.");
            Assert.AreEqual("ES", Field<TextMeshProUGUI>("_langButtonText").text,
                "The label must not flip when there is no memory to write the change to.");
        }

        // -------------------------------------------------------------------------
        // AppendMessageRow - the only builder API that mutates the tree at runtime
        // -------------------------------------------------------------------------

        [Test]
        public void AppendMessageRow_PlayerSender_AddsCyanTaggedRowUnderContent()
        {
            AppendMessageRow("Player", "hola mundo");

            var rows = Field<List<GameObject>>("_messageRows");
            Assert.AreEqual(1, rows.Count, "Every appended message must be tracked so ClearMessages can dispose it.");

            var content = Field<RectTransform>("_contentRect");
            Assert.AreSame(content.transform, rows[0].transform.parent,
                "Message rows must be parented to Content or they are neither masked nor scrolled.");

            var tmp = rows[0].GetComponent<TextMeshProUGUI>();
            Assert.IsTrue(tmp != null, "A message row must carry a TextMeshProUGUI.");
            StringAssert.Contains("<color=#00FFFF>Player</color>: hola mundo", tmp.text,
                "Player messages are tagged cyan; losing the rich-text tag makes senders indistinguishable.");
            Assert.IsTrue(tmp.richText, "richText must stay on or the colour tag renders as literal markup.");
        }

        [Test]
        public void AppendMessageRow_NonPlayerSender_UsesADifferentColourThanThePlayer()
        {
            AppendMessageRow("Player", "hi");
            AppendMessageRow("Herrero", "hi");

            var rows = Field<List<GameObject>>("_messageRows");
            string playerText = rows[0].GetComponent<TextMeshProUGUI>().text;
            string npcText = rows[1].GetComponent<TextMeshProUGUI>().text;

            Assert.AreNotEqual(playerText, npcText,
                "Player and NPC lines must not render identically - the sender colour is what distinguishes them.");
            StringAssert.Contains("Herrero</color>: hi", npcText, "The NPC name must sit inside the colour tag.");
            StringAssert.DoesNotContain("00FFFF", npcText,
                "Only the player's own lines are cyan; a cyan NPC line means the sender check regressed.");
        }

        [Test]
        public void AppendMessageRow_CalledRepeatedly_KeepsInsertionOrder()
        {
            for (int i = 0; i < 5; i++) AppendMessageRow("Player", "m" + i);

            var rows = Field<List<GameObject>>("_messageRows");
            Assert.AreEqual(5, rows.Count, "Every appended row must be tracked.");

            var content = Field<RectTransform>("_contentRect");
            for (int i = 0; i < 5; i++)
            {
                Assert.AreSame(rows[i].transform, content.transform.GetChild(i),
                    "Message rows must sit in the content in append order - the VerticalLayoutGroup renders "
                    + "sibling order top to bottom, so a reversal would show the conversation backwards.");
                StringAssert.EndsWith("m" + i, rows[i].GetComponent<TextMeshProUGUI>().text,
                    "Row " + i + " must hold the i-th message.");
            }
        }

        [Test]
        public void AppendMessageRow_UnicodeAndVeryLongText_IsPreservedVerbatim()
        {
            // NPC replies are offline/LLM text: accents, CJK, emoji and long paragraphs all occur.
            string payload = "niño 你好 😀 " + new string('x', 4000);
            AppendMessageRow("Herrero", payload);

            var tmp = Field<List<GameObject>>("_messageRows")[0].GetComponent<TextMeshProUGUI>();

            StringAssert.EndsWith(payload, tmp.text,
                "Message text must not be truncated or re-encoded - long replies rely on word wrap, not clipping.");
            Assert.IsTrue(tmp.enableWordWrapping,
                "Word wrapping must stay enabled or a long reply renders as a single clipped line.");
        }

        [Test]
        public void AppendMessageRow_NullSenderAndText_DoesNotThrowAndStillAddsARow()
        {
            // Providers can hand back nulls; the row builder must survive that.
            Assert.DoesNotThrow(() => AppendMessageRow(null, null),
                "A null sender or body must not take down the chat panel.");

            var rows = Field<List<GameObject>>("_messageRows");
            Assert.AreEqual(1, rows.Count, "A null message still occupies a row rather than silently vanishing.");
            Assert.IsTrue(rows[0].GetComponent<TextMeshProUGUI>() != null,
                "The row must still be a valid TMP row even with empty content.");
        }

        [Test]
        public void AppendMessageRow_EmptySender_KeepsTheSenderTextSeparator()
        {
            AppendMessageRow("", "solo texto");

            var tmp = Field<List<GameObject>>("_messageRows")[0].GetComponent<TextMeshProUGUI>();
            StringAssert.Contains("</color>: solo texto", tmp.text,
                "The 'sender: text' separator must survive an empty sender name.");
        }
    }
}
