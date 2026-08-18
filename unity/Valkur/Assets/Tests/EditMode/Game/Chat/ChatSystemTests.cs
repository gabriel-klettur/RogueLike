using System;
using System.Collections.Generic;
using System.IO;
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

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// EditMode coverage for <see cref="ChatSystem"/> (both partials:
    /// <c>ChatSystem.cs</c> and <c>ChatSystem.Messages.cs</c>).
    ///
    /// What this fixture protects:
    ///   * The session state machine — <c>TryOpenChat</c> / <c>OpenChat</c> /
    ///     <c>CloseChat</c>, the events they raise, and the fact that closing a
    ///     session that was never opened is a silent no-op.
    ///   * Provider selection: <c>OnSingletonAwake</c> resolves
    ///     <see cref="IChatProvider"/> from <see cref="ServiceLocator"/> and falls
    ///     back to <see cref="OfflineDialogueProvider"/> when nothing is registered.
    ///     A regression here would silently send every conversation to the offline
    ///     canned lines even with an LLM provider installed.
    ///   * Provider failure paths — null reply, empty reply and a faulted task must
    ///     all degrade to the "..." fallback instead of stalling the conversation.
    ///   * Cancellation — closing the chat, or submitting a second message, must
    ///     cancel the in-flight provider call so a late reply cannot leak into a
    ///     conversation the player already walked away from.
    ///   * Message accumulation, the 10-entry history cap, and the hand-off into
    ///     <see cref="NPCMemory"/> / <see cref="NPCMemoryStore"/>.
    ///
    /// Isolation notes:
    ///   * <see cref="ChatPersistencePaths.OverrideRoot"/> is redirected to a unique
    ///     temp folder per test, so neither <c>Application.persistentDataPath</c>
    ///     nor any real player data is touched.
    ///   * The <see cref="IChatProvider"/> is always a local fake — nothing here
    ///     can reach the network.
    ///   * <c>SingletonMonoBehaviour&lt;ChatSystem&gt;._instance</c> is cleared
    ///     around every test; Domain Reload is OFF in this project so a leaked
    ///     instance would poison later fixtures.
    ///
    /// EditMode caveats: <c>ChatBubble.PushBubble</c> builds TMP objects and
    /// <c>ChatBubble.OnDestroy</c> calls <c>Object.Destroy</c> (illegal in edit
    /// mode), both of which log. <c>LogAssert.ignoreFailingMessages</c> is on for
    /// the whole fixture so that noise cannot mask the structural assertions.
    /// </summary>
    [TestFixture]
    public class ChatSystemTests
    {
        // ChatSystem's private constants, mirrored here so the tests state the
        // contract explicitly instead of silently tracking whatever the code does.
        private const int MaxHistory = 10;
        private const int ReplyChunkWords = 8;

        private readonly List<GameObject> _scene = new List<GameObject>();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();
        private string _testRoot;

        // ── Reflection helpers ───────────────────────────────────────────────

        private static FieldInfo FindField(Type t, string name)
        {
            while (t != null)
            {
                var f = t.GetField(name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static object GetFieldValue(object target, string name)
        {
            var f = FindField(target.GetType(), name);
            Assert.IsNotNull(f, $"Field '{name}' not found on {target.GetType().Name}.");
            return f.GetValue(target);
        }

        private static void SetFieldValue(object target, string name, object value)
        {
            var f = FindField(target.GetType(), name);
            Assert.IsNotNull(f, $"Field '{name}' not found on {target.GetType().Name}.");
            f.SetValue(target, value);
        }

        private static void InvokeMethod(object target, string name)
        {
            var t = target.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                t = t.BaseType;
            }
            Assert.IsNotNull(m, $"Method '{name}' not found on {target.GetType().Name}.");
            m.Invoke(target, null);
        }

        /// <summary>
        /// SingletonMonoBehaviour&lt;T&gt; stores _instance on the *base* generic
        /// type, not on T. Walk up until the field is found.
        /// </summary>
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

        /// <summary>
        /// Reads the private <c>_pendingChunks</c> queue and projects the chunk
        /// texts. The queue is the only observable trace of a provider reply in
        /// EditMode, because <c>Update()</c> (which drains it) does not tick.
        /// </summary>
        private static List<string> PendingChunkTexts(ChatSystem chat)
        {
            var raw = GetFieldValue(chat, "_pendingChunks") as System.Collections.IEnumerable;
            Assert.IsNotNull(raw, "_pendingChunks must be an enumerable queue.");

            var texts = new List<string>();
            foreach (var item in raw)
            {
                var f = item.GetType().GetField("text",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(f, "ScheduledChunk must expose a 'text' field.");
                texts.Add((string)f.GetValue(item));
            }
            return texts;
        }

        /// <summary>
        /// Forces the chunk gate open and runs one <c>Update()</c> tick, which
        /// moves exactly one scheduled chunk into the public History.
        /// </summary>
        private static void PumpOneChunk(ChatSystem chat)
        {
            SetFieldValue(chat, "_nextChunkTime", -1e9f);
            InvokeMethod(chat, "Update");
        }

        // ── Fakes ────────────────────────────────────────────────────────────

        /// <summary>
        /// Deterministic, offline stand-in for a real provider. Returns an
        /// already-completed Task so the <c>await</c> inside
        /// <c>ChatSystem.GenerateReply</c> resumes synchronously — that is what
        /// makes these EditMode assertions deterministic.
        /// </summary>
        private sealed class FakeChatProvider : IChatProvider
        {
            public bool IsOnline => true;
            public string ProviderName => "fake";

            public int CallCount;
            public NPCPersonaDefinition LastPersona;
            public NPCMemory LastMemory;
            public string LastPlayerText;
            public readonly List<CancellationToken> Tokens = new List<CancellationToken>();

            public string ReplyToReturn = "ok";
            public Exception FaultWith;

            public Task<string> GenerateReplyAsync(
                NPCPersonaDefinition persona,
                NPCMemory memory,
                string playerText,
                CancellationToken cancellationToken)
            {
                CallCount++;
                LastPersona = persona;
                LastMemory = memory;
                LastPlayerText = playerText;
                Tokens.Add(cancellationToken);

                if (FaultWith != null)
                    return Task.FromException<string>(FaultWith);

                return Task.FromResult(ReplyToReturn);
            }
        }

        // ── Builders ─────────────────────────────────────────────────────────

        private NPCPersonaDefinition MakePersona(
            string personaId, string displayName, string greeting = "", float chatRange = 10f)
        {
            var p = ScriptableObject.CreateInstance<NPCPersonaDefinition>();
            p.personaId = personaId;
            p.displayName = displayName;
            p.role = "generic";
            p.greeting = greeting;
            p.chatRange = chatRange;
            _assets.Add(p);
            return p;
        }

        private ChatAssignmentCatalog MakeCatalog(params (string entityName, NPCPersonaDefinition persona)[] entries)
        {
            var cat = ScriptableObject.CreateInstance<ChatAssignmentCatalog>();
            foreach (var e in entries)
            {
                cat.assignments.Add(new ChatAssignmentCatalog.ChatAssignment
                {
                    entityName = e.entityName,
                    persona = e.persona
                });
            }
            cat.RebuildLookup();
            _assets.Add(cat);
            return cat;
        }

        /// <summary>
        /// Registers <paramref name="provider"/> (when non-null) BEFORE the
        /// component's Awake runs: ChatSystem resolves its provider once, in
        /// OnSingletonAwake, from the ServiceLocator.
        ///
        /// Unity does NOT invoke Awake for a plain MonoBehaviour added via
        /// AddComponent outside Play Mode, so it is invoked explicitly here.
        /// That is deliberate — the provider-selection branch under test lives
        /// inside OnSingletonAwake and would otherwise never execute.
        /// </summary>
        private ChatSystem CreateChatSystem(ChatAssignmentCatalog catalog, IChatProvider provider)
        {
            if (provider != null)
                ServiceLocator.Register<IChatProvider>(provider);

            var go = new GameObject("[ChatSystem_Test]");
            _scene.Add(go);
            var chat = go.AddComponent<ChatSystem>();
            InvokeMethod(chat, "Awake");

            if (catalog != null)
                SetFieldValue(chat, "_catalog", catalog);

            return chat;
        }

        private GameObject CreatePlayer(Vector2 pos = default)
        {
            var go = new GameObject("Player");
            go.transform.position = pos;
            _scene.Add(go);
            EntityRegistry.RegisterPlayer(go);
            return go;
        }

        private GameObject CreateNpc(string npcName, Vector2 pos, bool withInteractable = true)
        {
            var go = new GameObject(npcName);
            go.transform.position = pos;
            _scene.Add(go);

            if (withInteractable)
            {
                var it = go.AddComponent<NPCInteractable>();
                SetFieldValue(it, "npcName", npcName);
            }
            return go;
        }

        // ── Fixture lifecycle ────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            // ChatBubble builds TMP objects and calls Object.Destroy in OnDestroy;
            // both are noisy (and the latter illegal) in edit mode.
            LogAssert.ignoreFailingMessages = true;

            ClearSingleton<ChatSystem>();
            ServiceLocator.Clear();
            EntityRegistry.Clear();
            InputBlocker.SetBlocked(false);

            _testRoot = Path.Combine(
                Path.GetTempPath(), "valkur_test_chatsystem_" + Guid.NewGuid().ToString("N"));
            ChatPersistencePaths.OverrideRoot = _testRoot;
        }

        [TearDown]
        public void TearDown()
        {
            // Release the session log file handle before deleting the temp tree.
            ChatSessionLogger.CloseSession();

            foreach (var go in _scene)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _scene.Clear();

            foreach (var so in _assets)
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _assets.Clear();

            ClearSingleton<ChatSystem>();
            ServiceLocator.Clear();
            EntityRegistry.Clear();
            InputBlocker.SetBlocked(false);

            ChatPersistencePaths.OverrideRoot = null;
            try
            {
                if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, recursive: true);
            }
            catch
            {
                // Best-effort — the OS reclaims temp eventually.
            }

            // Reset last: the destruction above is itself a source of log noise.
            LogAssert.ignoreFailingMessages = false;
        }

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
        public void SubmitPlayerMessage_LongReply_SplitsIntoEightWordChunks()
        {
            var chat = OpenReadyChat(out var fake, out _);

            // 20 words -> 8 + 8 + 4 = three chunks.
            var words = new string[20];
            for (int i = 0; i < words.Length; i++) words[i] = "w" + i;
            fake.ReplyToReturn = string.Join(" ", words);

            chat.SubmitPlayerMessage("hola");

            var chunks = PendingChunkTexts(chat);
            Assert.AreEqual(3, chunks.Count,
                $"A 20-word reply must be split into ceil(20/{ReplyChunkWords}) = 3 chunks.");
            Assert.AreEqual(string.Join(" ", words, 0, ReplyChunkWords), chunks[0],
                "The first chunk must be the first 8 words in order, with single-space joins.");
            Assert.AreEqual(string.Join(" ", words, 16, 4), chunks[2],
                "The trailing chunk must contain the remaining 4 words — no word may be dropped.");
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

            Assert.IsEmpty(chat.History,
                "Re-opening a session must clear the previous conversation's history.");
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
        public void OpenChat_FirstVisit_DeliversGreetingAndMarksHasGreeted()
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
            Assert.IsTrue(chat.ActiveMemory.hasGreeted,
                "hasGreeted must be flipped so the greeting is never replayed.");
        }

        [Test]
        public void OpenChat_SecondVisit_SkipsGreetingBecauseHasGreetedPersisted()
        {
            var persona = MakePersona("p1", "Gatita", greeting: "Bienvenido, viajero.");
            var catalog = MakeCatalog(("Gatita", persona));
            var chat = CreateChatSystem(catalog, new FakeChatProvider());
            CreatePlayer();
            var npc = CreateNpc("Gatita", Vector2.zero);

            chat.OpenChat(npc);
            chat.CloseChat();
            chat.OpenChat(npc);

            Assert.IsEmpty(chat.History,
                "hasGreeted is persisted, so the second conversation must start with an empty " +
                "history instead of replaying the greeting.");
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
            Assert.IsFalse(chat.ActiveMemory.hasGreeted,
                "hasGreeted must stay false when nothing was actually greeted, so a greeting " +
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

            var history = chat.ActiveMemory.ephemeralHistory;
            Assert.AreEqual(1, history.Count,
                "The player line must be mirrored into the NPC's ephemeral memory.");
            Assert.AreEqual("user", history[0].role,
                "Player lines must be tagged 'user' — the role drives LLM prompt construction.");
            Assert.AreEqual("hola", history[0].content, "The stored content must be verbatim.");
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
    }
}
