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
    /// <summary>ChatSystemTests: the sessions tests. SetUp, TearDown and helpers live in ChatSystemTests.cs.</summary>
    public partial class ChatSystemTests
    {
        // ── Session state machine ────────────────────────────────────────────

        [Test]
        public void OpenChat_WithPersonaInCatalog_SetsOpenStateTargetAndPersona()
        {
            var persona = MakePersona("p1", "Gatita");
            var catalog = MakeCatalog(("Gatita", persona));
            var chat = CreateChatSystem(catalog, new FakeChatProvider());
            CreatePlayer();

            var npc = CreateNpc("Gatita", Vector2.zero);
            int opened = 0;
            chat.OnChatOpened += () => opened++;

            chat.OpenChat(npc);

            Assert.IsTrue(chat.IsChatOpen, "IsChatOpen must be true after OpenChat.");
            Assert.AreSame(npc, chat.ChatTarget, "ChatTarget must be the GameObject passed in.");
            Assert.AreSame(persona, chat.ActivePersona,
                "The persona must be resolved through NPCInteractable.NPCName, not the GameObject name.");
            Assert.AreEqual(1, opened, "OnChatOpened must fire exactly once per OpenChat call.");
            Assert.IsNotNull(chat.ActiveMemory, "ActiveMemory must be populated while a chat is open.");
        }

        [Test]
        public void OpenChat_TargetWithoutInteractable_FallsBackToGameObjectName()
        {
            var persona = MakePersona("p1", "Estatua");
            // Catalog keyed by the GameObject name, since there is no NPCInteractable.
            var catalog = MakeCatalog(("PlainObject", persona));
            var chat = CreateChatSystem(catalog, new FakeChatProvider());
            CreatePlayer();

            var target = CreateNpc("PlainObject", Vector2.zero, withInteractable: false);

            chat.OpenChat(target);

            Assert.AreSame(persona, chat.ActivePersona,
                "When the target has no NPCInteractable, ChatSystem must fall back to " +
                "GameObject.name for the catalog lookup.");
        }

        [Test]
        public void OpenChat_NoCatalogAssigned_LeavesPersonaNullWithoutThrowing()
        {
            var chat = CreateChatSystem(null, new FakeChatProvider());
            CreatePlayer();
            var npc = CreateNpc("Gatita", Vector2.zero);

            Assert.DoesNotThrow(() => chat.OpenChat(npc),
                "A missing ChatAssignmentCatalog is a designer mistake, not a crash — " +
                "OpenChat must tolerate a null catalog.");
            Assert.IsTrue(chat.IsChatOpen, "The session must still open without a catalog.");
            Assert.IsNull(chat.ActivePersona, "No catalog means no persona.");
        }

        [Test]
        public void OpenChat_CalledTwiceWithoutClosing_ResetsHistoryAndCountsASecondVisit()
        {
            var chat = OpenReadyChat(out _, out _);
            var npc = chat.ChatTarget;

            chat.SubmitPlayerMessage("hola");
            Assert.Greater(chat.History.Count, 0, "Pre-condition: history has content.");
            Assert.AreEqual(1, chat.ActiveMemory.visitCount, "Pre-condition: this is visit #1.");

            // OpenChat has no re-entrancy guard (unlike TryOpenChat) — re-opening
            // must therefore behave like a clean restart, not append to the old one.
            chat.OpenChat(npc);

            // Rebuilt from the record, never appended to. It used to be asserted as EMPTY,
            // which was true only because nothing had reached disk yet: memory was saved at
            // CloseChat and this test never closes. A player line is persisted as it is said
            // now — a conversation that ends with the game being killed is the one worth not
            // losing — so what re-opening shows is the exchange the player left, exactly once.
            Assert.AreEqual(1, chat.History.Count,
                "Re-opening a session must REBUILD the transcript from the record, not append " +
                "to the one already on screen.");
            Assert.AreEqual("hola", chat.History[0].text,
                "And what it rebuilds is what was actually said.");
            Assert.AreEqual(2, chat.ActiveMemory.visitCount,
                "Each OpenChat call counts as a visit, so a second call must reach visitCount 2.");
            Assert.IsTrue(chat.IsChatOpen, "The session must remain open after re-opening.");
        }

        [Test]
        public void CloseChat_AfterOpen_ClearsAllSessionStateAndRaisesClosed()
        {
            var chat = OpenReadyChat(out _, out _);
            int closed = 0;
            chat.OnChatClosed += () => closed++;

            chat.CloseChat();

            Assert.IsFalse(chat.IsChatOpen, "IsChatOpen must be false after CloseChat.");
            Assert.IsNull(chat.ChatTarget, "ChatTarget must be released so the NPC can be destroyed.");
            Assert.IsNull(chat.ActivePersona, "ActivePersona must be cleared on close.");
            Assert.IsNull(chat.ActiveMemory,
                "ActiveMemory must be cleared on close — the lang-toggle button writes through it " +
                "and must not mutate a stale record.");
            Assert.AreEqual(1, closed, "OnChatClosed must fire exactly once.");
        }

        [Test]
        public void CloseChat_NeverOpened_IsNoOpAndRaisesNoEvent()
        {
            var chat = CreateChatSystem(null, new FakeChatProvider());
            int closed = 0;
            chat.OnChatClosed += () => closed++;

            Assert.DoesNotThrow(() => chat.CloseChat(),
                "Closing a session that was never opened must be a silent no-op.");
            Assert.AreEqual(0, closed,
                "OnChatClosed must not fire when there was no open session — subscribers " +
                "(ChatInputGate, ChatUI) would otherwise unblock input that was never blocked.");
            Assert.IsFalse(chat.IsChatOpen, "State must remain closed.");
        }

        [Test]
        public void CloseChat_CalledTwice_RaisesClosedOnlyOnce()
        {
            var chat = OpenReadyChat(out _, out _);
            int closed = 0;
            chat.OnChatClosed += () => closed++;

            chat.CloseChat();
            chat.CloseChat();

            Assert.AreEqual(1, closed,
                "The second CloseChat must short-circuit on the _chatOpen guard; a double " +
                "OnChatClosed would double-unblock input.");
        }

        [Test]
        public void CloseChat_WithPendingChunks_DiscardsThem()
        {
            var chat = OpenReadyChat(out var fake, out _);
            fake.ReplyToReturn = "una respuesta bastante larga que se parte en varios trozos distintos aqui";
            chat.SubmitPlayerMessage("hola");
            Assert.IsNotEmpty(PendingChunkTexts(chat), "Pre-condition: chunks are queued.");

            chat.CloseChat();

            Assert.IsEmpty(PendingChunkTexts(chat),
                "Undelivered chunks must be dropped on close, otherwise they leak into the " +
                "next conversation with a different NPC.");
        }

        // ── Greeting + memory ────────────────────────────────────────────────

        [Test]
        public void OpenChat_FirstVisit_DeliversGreetingAndStampsTheDay()
        {
            var persona = MakePersona("p1", "Gatita", greeting: "Bienvenido, viajero.");
            var catalog = MakeCatalog(("Gatita", persona));
            var chat = CreateChatSystem(catalog, new FakeChatProvider());
            CreatePlayer();
            var npc = CreateNpc("Gatita", Vector2.zero);

            chat.OpenChat(npc);

            Assert.AreEqual(1, chat.History.Count,
                "The one-time greeting must be the only history entry after a first open.");
            Assert.AreEqual("Bienvenido, viajero.", chat.History[0].text,
                "The greeting text must come from the persona verbatim.");
            Assert.AreEqual("Gatita", chat.History[0].sender,
                "The greeting must be attributed to the NPC name used for the catalog lookup.");
            Assert.AreEqual(ChatDayClock.TodayKey, chat.ActiveMemory.lastGreetedDayKey,
                "The greeting must stamp today, so it is not said again until the day turns.");
        }

        [Test]
        public void OpenChat_SecondVisitSameDay_SkipsGreetingBecauseTheDayIsStamped()
        {
            var persona = MakePersona("p1", "Gatita", greeting: "Bienvenido, viajero.");
            var catalog = MakeCatalog(("Gatita", persona));
            var chat = CreateChatSystem(catalog, new FakeChatProvider());
            CreatePlayer();
            var npc = CreateNpc("Gatita", Vector2.zero);

            chat.OpenChat(npc);
            chat.CloseChat();
            chat.OpenChat(npc);

            // The second conversation is NOT blank: OpenChat replays what was persisted, so
            // the panel opens on the exchange the player left. What "skips the greeting"
            // means is that the greeting is not SAID again — it is recalled, and the record
            // still holds exactly one copy of it. Asserting an empty history here was
            // asserting the absence of continuity, which is the defect the memory layer was
            // written for and never delivered.
            //
            // Both opens happen inside one test, so they share a day key. That is the whole
            // point of the assertion: the greeting is once per DAY, and this is the same day.
            int greetings = chat.ActiveMemory.ephemeralHistory
                .Count(m => m.content == "Bienvenido, viajero.");
            Assert.AreEqual(1, greetings,
                "The day stamp is persisted, so a second visit on the same day recalls the " +
                "greeting rather than re-emitting it.");
            Assert.AreEqual(1, chat.History.Count(m => m.text == "Bienvenido, viajero."),
                "And the recall must show it once, not once per visit.");
            Assert.AreEqual(2, chat.ActiveMemory.visitCount,
                "visitCount must survive the close/open round-trip through disk.");
        }

        [Test]
        public void OpenChat_PersonaWithEmptyGreeting_AddsNoHistoryEntry()
        {
            var persona = MakePersona("p1", "Mudo", greeting: "");
            var catalog = MakeCatalog(("Mudo", persona));
            var chat = CreateChatSystem(catalog, new FakeChatProvider());
            CreatePlayer();
            var npc = CreateNpc("Mudo", Vector2.zero);

            chat.OpenChat(npc);

            Assert.IsEmpty(chat.History,
                "An empty greeting must produce no history entry — an empty bubble would " +
                "otherwise pop over the NPC's head.");
            Assert.IsTrue(string.IsNullOrEmpty(chat.ActiveMemory.lastGreetedDayKey),
                "The day must stay unstamped when nothing was actually greeted, so a greeting " +
                "added later by a designer still fires.");
        }

        [Test]
        public void OpenChat_MemoryPersistedToDisk_SurvivesANewChatSystemInstance()
        {
            var persona = MakePersona("p1", "Gatita");
            var catalog = MakeCatalog(("Gatita", persona));

            var chat = CreateChatSystem(catalog, new FakeChatProvider());
            CreatePlayer();
            var npc = CreateNpc("Gatita", Vector2.zero);
            chat.OpenChat(npc);
            chat.CloseChat();

            // Simulate a scene reload: tear the ChatSystem down and rebuild it.
            // The NPC, the player registration and the on-disk memory all survive.
            UnityEngine.Object.DestroyImmediate(chat.gameObject);
            ClearSingleton<ChatSystem>();
            ServiceLocator.Clear();

            var chat2 = CreateChatSystem(catalog, new FakeChatProvider());
            chat2.OpenChat(npc);

            Assert.AreEqual(2, chat2.ActiveMemory.visitCount,
                "The memory record is keyed by personaId+npcName on disk, so a brand new " +
                "ChatSystem must pick up the previous visit count.");
        }

        [Test]
        public void SubmitPlayerMessage_AppendsToActiveMemoryAsUserRole()
        {
            var chat = OpenReadyChat(out _, out _);

            chat.SubmitPlayerMessage("hola");

            // Two entries, not one: the player's line, and the reply the provider produced
            // for it. The reply is recorded the moment it arrives rather than when the panel
            // finishes drip-feeding it as bubbles — a record of what was said must not
            // depend on Update() having ticked, and in Edit Mode it never does.
            var history = chat.ActiveMemory.ephemeralHistory;
            Assert.AreEqual(2, history.Count,
                "The player line and the NPC's reply must both be mirrored into memory.");
            Assert.AreEqual("user", history[0].role,
                "Player lines must be tagged 'user' — the role drives LLM prompt construction.");
            Assert.AreEqual("hola", history[0].content, "The stored content must be verbatim.");
            Assert.AreEqual("assistant", history[1].role,
                "And the reply must be tagged 'assistant'.");
        }

        [Test]
        public void Update_DrainedNpcChunk_IsStoredInMemoryAsAssistantRole()
        {
            var chat = OpenReadyChat(out var fake, out _);
            fake.ReplyToReturn = "saludos";
            chat.SubmitPlayerMessage("hola");

            PumpOneChunk(chat);

            var history = chat.ActiveMemory.ephemeralHistory;
            Assert.AreEqual(2, history.Count,
                "Both the player line and the drained NPC chunk must land in ephemeral memory.");
            Assert.AreEqual("assistant", history[1].role,
                "Anything not sent by 'Player' must be tagged 'assistant'; mis-tagging would " +
                "make the LLM think it was the player speaking.");
        }
    }
}
