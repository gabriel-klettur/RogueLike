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
    public partial class ChatSystemTests
    {
        // ChatSystem's private constants, mirrored here so the tests state the
        // contract explicitly instead of silently tracking whatever the code does.
        private const int MaxHistory = 10;
        private const int MaxBubbleWords = 22;

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
            TestReflection.Invoke(chat, "Update");
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

            /// <summary>Trade facts of the last request — the purse the NPC was told about.</summary>
            public ChatTradeContext LastTrade;

            /// <summary>Trade the fake offers alongside its words. None by default.</summary>
            public TradeProposal ProposalToReturn = TradeProposal.None;

            public Task<ChatReply> GenerateReplyAsync(ChatRequest request, CancellationToken cancellationToken)
            {
                CallCount++;
                LastPersona = request.Persona;
                LastMemory = request.Memory;
                LastPlayerText = request.PlayerText;
                LastTrade = request.Trade;
                Tokens.Add(cancellationToken);

                if (FaultWith != null)
                    return Task.FromException<ChatReply>(FaultWith);

                return Task.FromResult(new ChatReply(ReplyToReturn, ProposalToReturn));
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
            TestReflection.Invoke(chat, "Awake");

            // Awake resolves a catalog from Resources/Chat when none was assigned, which is
            // what makes the shipped one reachable at all (ChatSystem is AddComponent-ed
            // onto a bare GameObject, so its [SerializeField] can never be wired). That is
            // right in the game and wrong here: a fixture asking for NO catalog would
            // silently acquire the real one and start resolving real personas — an NPC this
            // fixture names "Gatita" picked up the shipped persona and its 2-unit chat range
            // in place of the default 10. Passing null now MEANS null.
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

    }
}
