using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.Chat.Providers;
using Valkur.Gameplay.NPC;

namespace Valkur.Tests.EditMode.Gameplay.Chat
{
    /// <summary>ChatSystemTests: the messages tests. SetUp, TearDown and helpers live in ChatSystemTests.cs.</summary>
    public partial class ChatSystemTests
    {
        // ── Message accumulation ─────────────────────────────────────────────

        [Test]
        public void SubmitPlayerMessage_WhenChatClosed_IsIgnored()
        {
            var chat = CreateChatSystem(null, new FakeChatProvider());
            CreatePlayer();

            chat.SubmitPlayerMessage("hola");

            Assert.IsEmpty(chat.History,
                "Messages submitted while no session is open must be discarded, not buffered " +
                "into the next conversation.");
        }

        [TestCase((string)null, TestName = "SubmitPlayerMessage_NullText_IsIgnored")]
        [TestCase("", TestName = "SubmitPlayerMessage_EmptyText_IsIgnored")]
        [TestCase("   ", TestName = "SubmitPlayerMessage_WhitespaceText_IsIgnored")]
        [TestCase("\t\n", TestName = "SubmitPlayerMessage_TabsAndNewlines_IsIgnored")]
        public void SubmitPlayerMessage_BlankText_IsIgnored(string text)
        {
            var chat = OpenReadyChat(out var fake, out _);

            chat.SubmitPlayerMessage(text);

            Assert.IsEmpty(chat.History,
                "Blank input must never reach the history — an empty bubble and a wasted " +
                "provider call are both visible regressions.");
            Assert.AreEqual(0, fake.CallCount,
                "Blank input must not reach the provider (a real LLM call costs money).");
        }

        [Test]
        public void SubmitPlayerMessage_RaisesOnMessageReceivedTaggedAsPlayer()
        {
            var chat = OpenReadyChat(out _, out _);
            var received = new List<(string sender, string text)>();
            chat.OnMessageReceived += (s, t) => received.Add((s, t));

            chat.SubmitPlayerMessage("hola");

            Assert.IsTrue(received.Contains(("Player", "hola")),
                "The player's own line must be broadcast with the literal sender 'Player' — " +
                "AddMessage keys the memory role off that exact string.");
        }

        [Test]
        public void History_BeyondTenMessages_DropsTheOldestFirst()
        {
            // No persona -> no provider replies, so only player lines accumulate
            // and the cap can be asserted exactly.
            var catalog = MakeCatalog();
            var chat = CreateChatSystem(catalog, new FakeChatProvider());
            CreatePlayer();
            var npc = CreateNpc("Unknown", Vector2.zero);
            chat.OpenChat(npc);

            const int total = MaxHistory + 3; // 13
            for (int i = 0; i < total; i++)
                chat.SubmitPlayerMessage("msg-" + i);

            Assert.AreEqual(MaxHistory, chat.History.Count,
                $"History must be capped at {MaxHistory} entries; an uncapped list grows " +
                "unbounded for the whole session.");
            Assert.AreEqual("msg-3", chat.History[0].text,
                "The cap must drop the OLDEST entries — after 13 messages with cap 10 the " +
                "first survivor is msg-3.");
            Assert.AreEqual("msg-" + (total - 1), chat.History[MaxHistory - 1].text,
                "The newest message must always be last.");
        }

        [Test]
        public void SubmitPlayerMessage_UnicodeAndLongText_IsStoredVerbatim()
        {
            var chat = OpenReadyChat(out var fake, out _);
            string text = "Hola señor — ¿qué tal? 안녕하세요 " + new string('x', 600);

            chat.SubmitPlayerMessage(text);

            Assert.AreEqual(text, chat.History[0].text,
                "Non-ASCII text and long strings must survive untouched — no trimming, no " +
                "truncation, no re-encoding on the way into the history.");
            Assert.AreEqual(text, fake.LastPlayerText,
                "The same untouched string must reach the provider.");
            Assert.AreEqual(text, chat.ActiveMemory.ephemeralHistory[0].content,
                "…and the same string must reach the persisted ephemeral memory.");
        }

        [Test]
        public void SubmitPlayerMessage_RepeatedIdenticalText_IsRecordedEveryTime()
        {
            var chat = OpenReadyChat(out var fake, out _);

            chat.SubmitPlayerMessage("hola");
            chat.SubmitPlayerMessage("hola");
            chat.SubmitPlayerMessage("hola");

            Assert.AreEqual(3, chat.History.Count,
                "Duplicate text must not be de-duplicated — repeating yourself is legal " +
                "player behaviour and each line is a distinct turn.");
            Assert.AreEqual(3, fake.CallCount,
                "Each duplicate must still produce its own provider call.");
        }

        // ── TryOpenChat proximity ────────────────────────────────────────────

        [Test]
        public void TryOpenChat_NoPlayerRegistered_ReturnsFalse()
        {
            var chat = CreateChatSystem(null, new FakeChatProvider());
            var npc = CreateNpc("Gatita", Vector2.zero);
            EntityRegistry.RegisterNPC(npc);

            bool opened = chat.TryOpenChat(Vector2.zero);

            Assert.IsFalse(opened,
                "Without a registered player there is no origin for the proximity test; " +
                "TryOpenChat must bail out instead of dereferencing a null transform.");
            Assert.IsFalse(chat.IsChatOpen, "No session may be opened.");
        }

        [Test]
        public void TryOpenChat_NpcInRange_OpensChatWithThatNpc()
        {
            var persona = MakePersona("p1", "Gatita");
            var catalog = MakeCatalog(("Gatita", persona));
            var chat = CreateChatSystem(catalog, new FakeChatProvider());
            CreatePlayer();
            var npc = CreateNpc("Gatita", new Vector2(1f, 0f));
            EntityRegistry.RegisterNPC(npc);

            bool opened = chat.TryOpenChat(Vector2.zero);

            Assert.IsTrue(opened, "An NPC 1 unit away is well inside the 10-unit persona range.");
            Assert.AreSame(npc, chat.ChatTarget, "The in-range NPC must become the chat target.");
        }

        [Test]
        public void TryOpenChat_NoNpcInRange_ReturnsFalseAndLeavesChatClosed()
        {
            var chat = CreateChatSystem(null, new FakeChatProvider());
            CreatePlayer();
            var npc = CreateNpc("Gatita", new Vector2(50f, 0f));
            EntityRegistry.RegisterNPC(npc);

            bool opened = chat.TryOpenChat(Vector2.zero);

            Assert.IsFalse(opened,
                "50 units is far outside the 10-unit default range, so no session may open.");
            Assert.IsFalse(chat.IsChatOpen, "IsChatOpen must stay false.");
            Assert.IsNull(chat.ChatTarget, "No target may be latched on a failed attempt.");
        }

        [Test]
        public void TryOpenChat_TwoNpcsInRange_PicksTheNearest()
        {
            var chat = CreateChatSystem(null, new FakeChatProvider());
            CreatePlayer();
            var far = CreateNpc("Far", new Vector2(6f, 0f));
            var near = CreateNpc("Near", new Vector2(2f, 0f));
            EntityRegistry.RegisterNPC(far);
            EntityRegistry.RegisterNPC(near);

            bool opened = chat.TryOpenChat(Vector2.zero);

            Assert.IsTrue(opened, "Both NPCs are inside the default range.");
            Assert.AreSame(near, chat.ChatTarget,
                "The nearest candidate must win regardless of registration order — clicking " +
                "next to one NPC must never open a conversation with the one behind it.");
        }

        [Test]
        public void TryOpenChat_NpcWithoutInteractable_IsSkippedInFavourOfAValidOne()
        {
            var chat = CreateChatSystem(null, new FakeChatProvider());
            CreatePlayer();
            var prop = CreateNpc("Prop", Vector2.zero, withInteractable: false); // closest
            var real = CreateNpc("Gatita", new Vector2(3f, 0f));
            EntityRegistry.RegisterNPC(prop);
            EntityRegistry.RegisterNPC(real);

            bool opened = chat.TryOpenChat(Vector2.zero);

            Assert.IsTrue(opened, "The valid NPC is inside the default range.");
            Assert.AreSame(real, chat.ChatTarget,
                "Entities without an NPCInteractable are not chat-capable and must be skipped " +
                "even when they are closer to the click.");
        }

        [Test]
        public void TryOpenChat_MonsterWithInteractable_IsAValidTarget()
        {
            var chat = CreateChatSystem(null, new FakeChatProvider());
            CreatePlayer();
            var monster = CreateNpc("TalkingSlime", new Vector2(2f, 0f));
            EntityRegistry.RegisterMonster(monster);

            bool opened = chat.TryOpenChat(Vector2.zero);

            Assert.IsTrue(opened,
                "The monster registry is scanned as well as the NPC registry — chat-capable " +
                "bosses/monsters must be reachable.");
            Assert.AreSame(monster, chat.ChatTarget, "The monster must become the chat target.");
        }

        [Test]
        public void TryOpenChat_PersonaWithNarrowRange_ExcludesNpcOutsideIt()
        {
            // Distance 5: inside the 10-unit default, outside this persona's 2-unit range.
            var persona = MakePersona("p1", "Timida", chatRange: 2f);
            var catalog = MakeCatalog(("Timida", persona));
            var chat = CreateChatSystem(catalog, new FakeChatProvider());
            CreatePlayer();
            var npc = CreateNpc("Timida", new Vector2(5f, 0f));
            EntityRegistry.RegisterNPC(npc);

            bool opened = chat.TryOpenChat(Vector2.zero);

            Assert.IsFalse(opened,
                "persona.chatRange must override the default range; falling back to the default " +
                "would let the player talk to a shy NPC from across the room.");
        }

        [Test]
        public void TryOpenChat_WhileAlreadyOpen_ReturnsFalseAndKeepsCurrentTarget()
        {
            var chat = CreateChatSystem(null, new FakeChatProvider());
            CreatePlayer();
            var first = CreateNpc("First", new Vector2(1f, 0f));
            var second = CreateNpc("Second", new Vector2(2f, 0f));
            EntityRegistry.RegisterNPC(first);
            EntityRegistry.RegisterNPC(second);

            Assert.IsTrue(chat.TryOpenChat(new Vector2(1f, 0f)), "Pre-condition: first open succeeds.");
            var target = chat.ChatTarget;

            bool reopened = chat.TryOpenChat(new Vector2(2f, 0f));

            Assert.IsFalse(reopened,
                "TryOpenChat is guarded against re-entry — clicking another NPC mid-conversation " +
                "must not silently hijack the session.");
            Assert.AreSame(target, chat.ChatTarget, "The original target must be retained.");
        }
    }
}
