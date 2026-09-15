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
using Valkur.Tests.Support;

namespace Valkur.Tests.EditMode.Gameplay.Chat
{
    /// <summary>ChatSystemTests: the resilience tests. SetUp, TearDown and helpers live in ChatSystemTests.cs.</summary>
    public partial class ChatSystemTests
    {
        // ── Shared arrangement ───────────────────────────────────────────────

        /// <summary>
        /// Builds a ChatSystem with a fake provider, a registered player and an
        /// open (greeting-free, so History starts empty) session with "Gatita".
        /// </summary>
        private ChatSystem OpenReadyChat(out FakeChatProvider provider, out NPCPersonaDefinition persona)
        {
            persona = MakePersona("p1", "Gatita");
            var catalog = MakeCatalog(("Gatita", persona));
            provider = new FakeChatProvider();
            var chat = CreateChatSystem(catalog, provider);
            CreatePlayer();
            var npc = CreateNpc("Gatita", Vector2.zero);
            chat.OpenChat(npc);
            Assert.IsTrue(chat.IsChatOpen, "Arrangement failed: chat did not open.");
            Assert.IsEmpty(chat.History, "Arrangement failed: history should start empty (no greeting).");
            return chat;
        }

        // ── No player registered ─────────────────────────────────────────────
        //
        // Every other test in this fixture registers a player, originally to route
        // around a NullReferenceException in SubmitPlayerMessage: EnsurePlayerBubble
        // early-returned when EntityRegistry had no player, left _playerBubble null, and
        // the caller dereferenced it anyway. The bubble helpers now carry the absence in
        // their return type, so these tests exercise that path directly.
        //
        // A missing player is a normal state, not a corner case: it is what the registry
        // reports during boot, while a cutscene owns the scene, and after the player dies.

        [Test]
        public void SubmitPlayerMessage_NoPlayerRegistered_DoesNotThrow()
        {
            var persona = MakePersona("p1", "Gatita");
            var catalog = MakeCatalog(("Gatita", persona));
            var chat = CreateChatSystem(catalog, new FakeChatProvider());
            var npc = CreateNpc("Gatita", Vector2.zero);
            chat.OpenChat(npc);
            EntityRegistry.Clear();   // player despawned mid-conversation

            Assert.DoesNotThrow(() => chat.SubmitPlayerMessage("hola"),
                "A missing player must cost the floating bubble and nothing else.");
        }

        [Test]
        public void SubmitPlayerMessage_NoPlayerRegistered_StillRecordsTheLine()
        {
            var persona = MakePersona("p1", "Gatita");
            var catalog = MakeCatalog(("Gatita", persona));
            var chat = CreateChatSystem(catalog, new FakeChatProvider());
            var npc = CreateNpc("Gatita", Vector2.zero);
            chat.OpenChat(npc);
            EntityRegistry.Clear();

            chat.SubmitPlayerMessage("hola");

            Assert.IsNotEmpty(chat.History,
                "History, memory and the session log are independent of the visual bubble. " +
                "Losing the line because nothing could draw it would be the wrong trade.");
        }

        [Test]
        public void SubmitPlayerMessage_NoPlayerRegistered_StillReachesTheProvider()
        {
            var persona = MakePersona("p1", "Gatita");
            var catalog = MakeCatalog(("Gatita", persona));
            var provider = new FakeChatProvider();
            var chat = CreateChatSystem(catalog, provider);
            var npc = CreateNpc("Gatita", Vector2.zero);
            chat.OpenChat(npc);
            EntityRegistry.Clear();

            chat.SubmitPlayerMessage("hola");

            Assert.AreEqual(1, provider.CallCount,
                "The NPC must still answer — the conversation is not the bubble.");
        }

        [Test]
        public void OpenChat_NoPlayerRegistered_DoesNotThrow()
        {
            var persona = MakePersona("p1", "Gatita");
            var catalog = MakeCatalog(("Gatita", persona));
            var chat = CreateChatSystem(catalog, new FakeChatProvider());
            var npc = CreateNpc("Gatita", Vector2.zero);

            Assert.DoesNotThrow(() => chat.OpenChat(npc),
                "OpenChat builds the player bubble up front; with no player it must skip it.");
            Assert.IsTrue(chat.IsChatOpen);
        }

        [Test]
        public void TryOpenChat_NoPlayerRegistered_ReturnsFalseWithoutThrowing()
        {
            var chat = CreateChatSystem(MakeCatalog(), new FakeChatProvider());
            CreateNpc("Gatita", Vector2.zero);

            bool opened = true;
            Assert.DoesNotThrow(() => opened = chat.TryOpenChat(Vector2.zero),
                "The no-target path also shows a player bubble, and it ran before the " +
                "proximity search could establish there was a player at all.");
            Assert.IsFalse(opened);
        }

        [Test]
        public void PlayerReappearing_GetsABubbleAgainWithoutReopeningTheChat()
        {
            var chat = OpenReadyChat(out _, out _);
            EntityRegistry.Clear();
            chat.SubmitPlayerMessage("into the void");

            CreatePlayer();
            Assert.DoesNotThrow(() => chat.SubmitPlayerMessage("back again"),
                "The bubble is resolved per message, so a respawned player recovers on its " +
                "own instead of staying mute for the rest of the session.");
            Assert.AreEqual(2, chat.History.Count);
        }

        // ── Language, relationship and durable memory ────────────────────────
        //
        // These three are what one player message leaves behind, and each was a defect
        // rather than a missing feature: the conversation opened in the wrong language, the
        // relationship score was read by the prompt builder and written by nobody, and
        // anything said more than twelve messages ago was gone with no trace.

        [Test]
        public void OpenChat_SeedsPreferredLanguageFromThePlayersGlobalChoice()
        {
            // PlayerPrefs is machine state, so whatever this developer had is put back.
            string original = ChatLanguage.Current;
            try
            {
                ChatLanguage.Set(ChatLanguage.ENGLISH);
                var chat = OpenReadyChat(out _, out _);

                Assert.AreEqual(ChatLanguage.ENGLISH, chat.ActiveMemory.preferredLanguage,
                    "PersonaPromptBuilder reads preferredLanguage and nothing else. The panel " +
                    "toggle only wrote it while a conversation was already open, so every " +
                    "conversation opened AFTER the switch started from the record's own 'es' " +
                    "default — English chrome, Spanish character.");
            }
            finally
            {
                ChatLanguage.Set(original);
            }
        }

        [Test]
        public void SubmitPlayerMessage_Insult_CostsRegardAndIsRemembered()
        {
            var chat = OpenReadyChat(out _, out _);

            chat.SubmitPlayerMessage("eres una ladrona");

            Assert.Less(chat.ActiveMemory.friendshipScore, 0,
                "The intent classifier has always seen the insult; until ChatRelationship " +
                "existed nothing acted on it and every character met the player at 0 forever.");
            Assert.IsTrue(chat.ActiveMemory.digest.Exists(n => n.key == ChatMemoryDigest.KEY_INSULTED),
                "And it must outlive the twelve-message window, which is the whole point of " +
                "the digest.");
        }

        [Test]
        public void SubmitPlayerMessage_SelfDisclosure_SurvivesInTheDigest()
        {
            var chat = OpenReadyChat(out _, out _);

            chat.SubmitPlayerMessage("me llamo Bruno");

            MemoryNote note = chat.ActiveMemory.digest.Find(n => n.key == ChatMemoryDigest.KEY_NAME);
            Assert.AreEqual("Bruno", note.value,
                "A name given once must still be known after the verbatim history has rolled " +
                "past it.");
        }

        [Test]
        public void SubmitPlayerMessage_OrdinaryLine_WritesNoNoteAndMovesNothing()
        {
            var chat = OpenReadyChat(out _, out _);

            chat.SubmitPlayerMessage("¿cuánto vale el pan?");

            Assert.IsEmpty(chat.ActiveMemory.digest,
                "Everyone asks a vendor about prices. A note per message would evict the real " +
                "ones inside one conversation.");
            Assert.AreEqual(0, chat.ActiveMemory.friendshipScore,
                "And trade talk is worth no regard, or the score measures shopping.");
        }

        [Test]
        public void SubmitPlayerMessage_RepeatedGreetings_StopEarningWithinOneConversation()
        {
            var chat = OpenReadyChat(out _, out _);

            for (int i = 0; i < 30; i++) chat.SubmitPlayerMessage("hola");

            Assert.AreEqual(ChatRelationship.GAIN_CAP_PER_CONVERSATION, chat.ActiveMemory.friendshipScore,
                "The per-conversation cap has to be wired to the OPEN, not just to exist: a " +
                "tally that is never reset makes the cap permanent, and one that is reset per " +
                "message makes it no cap at all.");
        }

        [Test]
        public void SubmitPlayerMessage_DigestAndScore_SurviveACloseAndReopen()
        {
            var chat = OpenReadyChat(out _, out _);
            chat.SubmitPlayerMessage("me llamo Bruno");
            var npc = chat.ChatTarget;
            chat.CloseChat();

            chat.OpenChat(npc);

            Assert.AreEqual("Bruno",
                chat.ActiveMemory.digest.Find(n => n.key == ChatMemoryDigest.KEY_NAME).value,
                "The note is written to disk on the message that produced it — a conversation " +
                "that ends with the game being killed is exactly the one worth not losing.");
        }

        [Test]
        public void TargetDestroyedMidConversation_DrainingChunksDoesNotThrow()
        {
            var chat = OpenReadyChat(out var provider, out _);
            provider.ReplyToReturn = "one two three four five six seven eight nine";
            chat.SubmitPlayerMessage("hi");

            // The NPC is killed or streamed out while its reply is still queued.
            var target = chat.ChatTarget;
            if (target != null) UnityEngine.Object.DestroyImmediate(target);

            SetFieldValue(chat, "_nextChunkTime", 0f);
            Assert.DoesNotThrow(() => TestReflection.Invoke(chat, "Update"),
                "Queued chunks outlive their speaker; draining them must not fault.");
        }
    }
}
