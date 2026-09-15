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
    /// <summary>ChatSystemTests: the providers tests. SetUp, TearDown and helpers live in ChatSystemTests.cs.</summary>
    public partial class ChatSystemTests
    {
        // ── Provider selection ───────────────────────────────────────────────

        [Test]
        public void OnSingletonAwake_NoProviderRegistered_FallsBackToOfflineProvider()
        {
            var chat = CreateChatSystem(null, provider: null);

            var provider = GetFieldValue(chat, "_provider");

            Assert.IsInstanceOf<OfflineDialogueProvider>(provider,
                "With nothing in the ServiceLocator, ChatSystem must fall back to the " +
                "offline canned-line provider rather than leaving _provider null.");
        }

        [Test]
        public void OnSingletonAwake_ProviderRegistered_UsesRegisteredProvider()
        {
            var fake = new FakeChatProvider();
            var chat = CreateChatSystem(null, fake);

            var provider = GetFieldValue(chat, "_provider");

            Assert.AreSame(fake, provider,
                "A provider registered in the ServiceLocator before Awake must win over " +
                "the offline fallback — otherwise an installed LLM provider is silently ignored.");
        }

        [Test]
        public void SubmitPlayerMessage_WithProvider_PassesPersonaMemoryAndTextThrough()
        {
            var persona = MakePersona("p1", "Gatita");
            var catalog = MakeCatalog(("Gatita", persona));
            var fake = new FakeChatProvider();
            var chat = CreateChatSystem(catalog, fake);
            CreatePlayer();

            var npc = CreateNpc("Gatita", Vector2.zero);
            chat.OpenChat(npc);
            var memoryAtOpen = chat.ActiveMemory;

            chat.SubmitPlayerMessage("hola");

            Assert.AreEqual(1, fake.CallCount,
                "Exactly one provider call must be made per submitted player message.");
            Assert.AreSame(persona, fake.LastPersona,
                "The provider must receive the persona resolved from the catalog, not null.");
            Assert.AreSame(memoryAtOpen, fake.LastMemory,
                "The provider must receive the live ActiveMemory instance so replies can be " +
                "personalised with visitCount / language preference.");
            Assert.AreEqual("hola", fake.LastPlayerText,
                "The provider must receive the untouched player text.");
        }

        [Test]
        public void SubmitPlayerMessage_NoPersonaResolved_DoesNotInvokeProvider()
        {
            // Empty catalog -> ActivePersona stays null.
            var catalog = MakeCatalog();
            var fake = new FakeChatProvider();
            var chat = CreateChatSystem(catalog, fake);
            CreatePlayer();

            var npc = CreateNpc("Unknown", Vector2.zero);
            chat.OpenChat(npc);

            chat.SubmitPlayerMessage("hola");

            Assert.IsNull(chat.ActivePersona,
                "Pre-condition: an entity absent from the catalog must resolve to a null persona.");
            Assert.AreEqual(0, fake.CallCount,
                "Without a persona there is nothing to condition a reply on; the provider " +
                "must not be called (it would receive a null persona).");
            Assert.AreEqual(1, chat.History.Count,
                "The player's own line must still be recorded even when no reply can be generated.");
        }

        // ── Provider failure paths ───────────────────────────────────────────

        [Test]
        public void SubmitPlayerMessage_ProviderReturnsNull_SchedulesEllipsisFallback()
        {
            var chat = OpenReadyChat(out var fake, out _);
            fake.ReplyToReturn = null;

            chat.SubmitPlayerMessage("hola");

            CollectionAssert.AreEqual(new[] { "..." }, PendingChunkTexts(chat),
                "A null reply must degrade to the '...' fallback so the conversation does not " +
                "silently stall with nothing scheduled.");
        }

        [Test]
        public void SubmitPlayerMessage_ProviderReturnsEmpty_SchedulesEllipsisFallback()
        {
            var chat = OpenReadyChat(out var fake, out _);
            fake.ReplyToReturn = string.Empty;

            chat.SubmitPlayerMessage("hola");

            CollectionAssert.AreEqual(new[] { "..." }, PendingChunkTexts(chat),
                "An empty-string reply must be treated exactly like null and degrade to '...'.");
        }

        [Test]
        public void SubmitPlayerMessage_ProviderThrows_SchedulesFallbackAndKeepsSessionUsable()
        {
            var chat = OpenReadyChat(out var fake, out _);
            fake.FaultWith = new InvalidOperationException("provider exploded");

            // The failure must reach the console — silently swallowing a provider
            // fault is the regression that makes "the NPC just says ..." unbuggable.
            LogAssert.Expect(LogType.Error, new Regex(@"\[ChatSystem\] Provider 'fake' failed"));

            chat.SubmitPlayerMessage("hola");

            CollectionAssert.AreEqual(new[] { "..." }, PendingChunkTexts(chat),
                "A faulted provider task must be caught and replaced with the '...' fallback.");
            Assert.IsTrue(chat.IsChatOpen,
                "A provider failure must not tear down the session — the player is still talking.");

            // The session must keep working after the failure.
            fake.FaultWith = null;
            fake.ReplyToReturn = "recovered";
            chat.SubmitPlayerMessage("otra vez");

            CollectionAssert.Contains(PendingChunkTexts(chat), "recovered",
                "After a failed call the next message must still reach the provider and be scheduled.");
        }

        // ── Reply chunking ───────────────────────────────────────────────────

        [Test]
        public void SubmitPlayerMessage_LongUnpunctuatedReply_IsSplitAtTheBubbleBudget()
        {
            var chat = OpenReadyChat(out var fake, out _);

            // 50 words with no punctuation at all: one "sentence" too long for a bubble,
            // which is the only case a reply is ever cut mid-phrase.
            var words = new string[50];
            for (int i = 0; i < words.Length; i++) words[i] = "w" + i;
            fake.ReplyToReturn = string.Join(" ", words);

            chat.SubmitPlayerMessage("hola");

            var chunks = PendingChunkTexts(chat);
            Assert.AreEqual(3, chunks.Count,
                $"50 words at a {MaxBubbleWords}-word budget is 22 + 22 + 6 = three bubbles.");
            Assert.AreEqual(string.Join(" ", words, 0, MaxBubbleWords), chunks[0],
                "The first bubble must be the first words in order, with single-space joins.");
            Assert.AreEqual(string.Join(" ", words, 44, 6), chunks[2],
                "The trailing bubble must carry the rest — no word may be dropped.");
        }

        [Test]
        public void SubmitPlayerMessage_MultiSentenceReply_KeepsEachSentenceWhole()
        {
            var chat = OpenReadyChat(out var fake, out _);

            // The shape a model actually returns, and the one the old eight-word cut got
            // wrong: it produced five bubbles from this, one of them "de remolacha— lista
            // para cocinar y regalarte un".
            fake.ReplyToReturn =
                "¡Ay, mi vida! Estoy estupenda, tarareo y con la libreta manchada de " +
                "remolacha, lista para cocinar. Con pancita feliz, todo sale mejor.";

            chat.SubmitPlayerMessage("que tal estas?");

            var chunks = PendingChunkTexts(chat);
            Assert.LessOrEqual(chunks.Count, 2,
                "Whole sentences are packed up to the bubble budget, so this is one or two " +
                "bubbles — not five, and not fifteen seconds of delivery.");
            foreach (string chunk in chunks)
            {
                Assert.IsFalse(chunk.EndsWith("de") || chunk.EndsWith("y") || chunk.EndsWith("un"),
                    $"A bubble must not end mid-phrase: '{chunk}'");
            }
        }

        [Test]
        public void SubmitPlayerMessage_ShortReply_ProducesExactlyOneChunk()
        {
            var chat = OpenReadyChat(out var fake, out _);
            fake.ReplyToReturn = "hola viajero";

            chat.SubmitPlayerMessage("hey");

            CollectionAssert.AreEqual(new[] { "hola viajero" }, PendingChunkTexts(chat),
                "A reply shorter than the chunk size must not be split or padded.");
        }

        [Test]
        public void Update_WithScheduledChunk_MovesChunkIntoHistoryAndRaisesEvent()
        {
            var chat = OpenReadyChat(out var fake, out var persona);
            fake.ReplyToReturn = "buenas";

            var received = new List<(string sender, string text)>();
            chat.OnMessageReceived += (s, t) => received.Add((s, t));

            chat.SubmitPlayerMessage("hola");
            int historyAfterSubmit = chat.History.Count;

            PumpOneChunk(chat);

            Assert.AreEqual(historyAfterSubmit + 1, chat.History.Count,
                "Draining one scheduled chunk must append exactly one history entry.");
            var last = chat.History[chat.History.Count - 1];
            Assert.AreEqual(persona.displayName, last.sender,
                "The NPC reply must be attributed to the persona's displayName, not to the player.");
            Assert.AreEqual("buenas", last.text,
                "The drained chunk text must reach the history verbatim.");
            Assert.IsTrue(received.Contains((persona.displayName, "buenas")),
                "OnMessageReceived must fire for NPC replies too, not only for player lines — " +
                "the chat UI subscribes to it for both.");
            Assert.IsEmpty(PendingChunkTexts(chat),
                "The drained chunk must be dequeued so it cannot be delivered twice.");
        }

        // ── Cancellation ─────────────────────────────────────────────────────

        [Test]
        public void CloseChat_WithInFlightReply_CancelsProviderToken()
        {
            var chat = OpenReadyChat(out var fake, out _);
            chat.SubmitPlayerMessage("hola");

            Assert.AreEqual(1, fake.Tokens.Count, "Pre-condition: one provider call was made.");
            Assert.IsFalse(fake.Tokens[0].IsCancellationRequested,
                "Pre-condition: the token must still be live while the chat is open.");

            chat.CloseChat();

            Assert.IsTrue(fake.Tokens[0].IsCancellationRequested,
                "Closing the chat must cancel the in-flight provider call, otherwise a slow LLM " +
                "reply can land in a conversation the player already walked away from.");
        }

        [Test]
        public void SubmitPlayerMessage_SecondMessage_CancelsPreviousReplyToken()
        {
            var chat = OpenReadyChat(out var fake, out _);

            chat.SubmitPlayerMessage("primera");
            chat.SubmitPlayerMessage("segunda");

            Assert.AreEqual(2, fake.Tokens.Count, "Pre-condition: both messages reached the provider.");
            Assert.IsTrue(fake.Tokens[0].IsCancellationRequested,
                "Starting a new reply must cancel the previous one so replies cannot interleave " +
                "out of order.");
            Assert.IsFalse(fake.Tokens[1].IsCancellationRequested,
                "The newest reply must remain live after superseding the previous one.");
        }
    }
}
