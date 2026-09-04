using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Chat.Providers;
using Valkur.Gameplay.NPC;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// Core chat system: proximity detection, message routing, NPC replies.
    ///
    /// Maps to Python's ChatProximitySystem + ChatRouterSystem + MessageScheduler.
    ///
    /// In Python, this supports an async LLM worker for AI-generated NPC dialogue.
    /// Unity version starts with offline mode (pre-written dialogue lines from persona),
    /// with an extensible IChatProvider interface for future LLM integration.
    ///
    /// Key Python constants preserved:
    ///   Chat range: persona.chatRange (default 10 world units)
    ///   NPC reply bubble TTL: 2600ms
    ///   Player message bubble TTL: 2800ms
    ///   Reply chunk size: 8 words
    ///   Reply chunk delay: 3000ms
    ///   History kept: last 10 messages
    /// </summary>
    public partial class ChatSystem : SingletonMonoBehaviour<ChatSystem>
    {
        private const int MAX_HISTORY = 10;
        /// <summary>
        /// How long a single floating bubble may get, in words.
        ///
        /// Replaces Python's fixed eight-word cut, which was sized for the short authored
        /// lines and produced five bubbles — one of them "de remolacha— lista para cocinar y
        /// regalarte un" — out of a single model reply. Whole sentences are packed up to
        /// this; only a sentence longer than this on its own is ever cut.
        /// </summary>
        private const int MAX_BUBBLE_WORDS = 22;

        /// <summary>
        /// A bubble carrying fewer words than this is folded back into the one before it.
        /// Three, because one and two-word tails ("vecina", "por favor") are what a pure
        /// word-budget split produces on the short authored lines this game ships, and they
        /// read as the NPC having been cut off rather than as a pause.
        /// </summary>
        private const int MIN_CHUNK_WORDS = 3;
        /// <summary>
        /// Gap between bubbles of the same reply. Was 3.0 (Python parity), which at five
        /// bubbles meant a fifteen-second answer; bubbles are sentence-sized now, so there
        /// are fewer of them and each can arrive sooner without overlapping the last.
        /// </summary>
        private const float REPLY_CHUNK_DELAY_SEC = 1.8f;
        private const int NPC_BUBBLE_TTL_MS = 2600;
        private const int PLAYER_BUBBLE_TTL_MS = 2800;
        private const int NO_ONE_BUBBLE_TTL_MS = 2600;

        /// <summary>
        /// Where <see cref="EnsureCatalog"/> looks when nothing injected a catalog.
        /// A SUBFOLDER on purpose — see the note on that method.
        /// </summary>
        private const string CHAT_CATALOG_RESOURCE_PATH = "Chat/ChatAssignmentCatalog";

        /// <summary>
        /// How the player is attributed in <see cref="History"/>, in the session log and in
        /// <see cref="NPCMemory"/>. A literal "Player" was compared in three separate files
        /// — here, in AddMessage's role mapping and in the panel's row colouring — so
        /// renaming it in one place would have silently made the player's own lines render
        /// and persist as the NPC's.
        /// </summary>
        public const string PLAYER_SENDER = "Player";

        [Header("References")]
        [SerializeField, Tooltip("Chat assignment catalog mapping entity names to personas.")]
        private ChatAssignmentCatalog _catalog;

        [Header("Settings")]
        [SerializeField, Tooltip("Default chat range if no persona defines one.")]
        private float _defaultChatRange = 10f;

        // ── State ──
        private bool _chatOpen;
        private GameObject _chatTarget;
        private NPCPersonaDefinition _activePersona;
        private ChatBubble _targetBubble;
        private ChatBubble _playerBubble;
        private readonly List<ChatMessage> _history = new List<ChatMessage>();
        private readonly Queue<ScheduledChunk> _pendingChunks = new Queue<ScheduledChunk>();
        private float _nextChunkTime;

        // ── Provider + persistence ──
        private IChatProvider _provider;
        private CancellationTokenSource _replyCts;
        private NPCMemory _activeMemory;

        // ── Events ──
        public event Action OnChatOpened;
        public event Action OnChatClosed;
        public event Action<string, string> OnMessageReceived; // (sender, text)

        /// <summary>
        /// The transcript was replaced wholesale rather than appended to — today only by
        /// <see cref="ResetActiveMemory"/>. Separate from <see cref="OnMessageReceived"/>
        /// because a listener has to REBUILD its rows here, not add one.
        /// </summary>
        public event Action OnHistoryReset;

        // ── Public API ──

        public bool IsChatOpen => _chatOpen;
        public GameObject ChatTarget => _chatTarget;
        public NPCPersonaDefinition ActivePersona => _activePersona;
        public IReadOnlyList<ChatMessage> History => _history;

        /// <summary>
        /// The persistent memory record for the currently active NPC.
        /// Null when no chat is open. The lang-toggle button writes here.
        /// </summary>
        public NPCMemory ActiveMemory => _activeMemory;

        protected override bool Persist => false;

        // ── Lifecycle ──

        protected override void OnSingletonAwake()
        {
            // Resolve provider via ServiceLocator; fall back to offline.
            if (!ServiceLocator.TryGet<IChatProvider>(out _provider))
                _provider = new OfflineDialogueProvider();

            EnsureCatalog();
        }

        /// <summary>
        /// Assigns the assignment catalog. Bootstrap calls this because
        /// <c>GameplaySceneSetup</c> builds the ChatSystem with
        /// <c>AddComponent</c> on a bare GameObject, which leaves every
        /// <c>[SerializeField]</c> on it null — including this one, for the whole life
        /// of the project. With no catalog, every persona lookup returned null, so no
        /// NPC ever greeted and <c>GenerateReply</c> returned on its first line.
        /// </summary>
        public void SetCatalog(ChatAssignmentCatalog catalog)
        {
            if (catalog == null) return;
            _catalog = catalog;
        }

        /// <summary>
        /// Last-resort catalog resolution, in ServiceLocator-then-Resources order.
        ///
        /// The Resources path names a SUBFOLDER deliberately. <c>Resources.Load</c> with
        /// an empty path is a full-tree scan of ~7,400 assets that logs a missing-script
        /// error for every unresolvable one — the trap <c>SpawnPlayer</c> already fell
        /// into. <c>CHAT_CATALOG_RESOURCE_PATH</c> resolves exactly one asset.
        /// </summary>
        private void EnsureCatalog()
        {
            if (_catalog != null) return;

            if (ServiceLocator.TryGet<ChatAssignmentCatalog>(out var registered) && registered != null)
            {
                _catalog = registered;
                return;
            }

            _catalog = Resources.Load<ChatAssignmentCatalog>(CHAT_CATALOG_RESOURCE_PATH);
            if (_catalog == null)
                Debug.LogWarning(
                    $"[ChatSystem] No ChatAssignmentCatalog at Resources/{CHAT_CATALOG_RESOURCE_PATH}. " +
                    "Entities carrying an NPCChatIdentity still speak; by-name lookup is disabled.");
        }

        /// <summary>
        /// The persona for <paramref name="target"/>.
        ///
        /// Asks the entity itself first (<see cref="NPCChatIdentity"/>, assigned at spawn
        /// from the definition) and only then the by-name catalog. The direct reference
        /// cannot drift when an entity is renamed; the catalog stays the answer for
        /// entities placed by hand and is what the Python-parity authoring uses.
        /// </summary>
        private NPCPersonaDefinition ResolvePersona(GameObject target)
        {
            if (target == null) return null;

            var identity = target.GetComponent<NPCChatIdentity>();
            if (identity != null && identity.Persona != null) return identity.Persona;

            // Deliberately does NOT call EnsureCatalog. Resolution happens once, in
            // OnSingletonAwake, and re-trying it here would make the shipped catalogue
            // impossible to opt out of: Awake never runs on a component added in Edit Mode,
            // so a fixture that constructs a ChatSystem with no catalogue would silently
            // acquire the real one and start resolving real personas — measured, an NPC the
            // fixture named "Gatita" picked up the shipped persona and its 2-unit chat range
            // in place of the fixture's default 10. A conversation is not a hot path anyway;
            // one resolution point is enough.
            if (_catalog == null) return null;

            var interactable = target.GetComponent<NPCInteractable>();
            string npcName = interactable != null ? interactable.NPCName : target.name;
            return _catalog.GetPersona(npcName);
        }

        /// <summary>
        /// Attempt to open chat with the nearest NPC in range.
        /// Called when player left-clicks near an NPC or presses chat key.
        /// Maps to Python ChatProximitySystem.update().
        /// </summary>
        public bool TryOpenChat(Vector2 clickWorldPos)
        {
            if (_chatOpen) return false;

            var player = EntityRegistry.PlayerTransform;
            if (player == null) return false;

            // Find nearest chat-capable entity in range. Both registries are walked
            // because a chat-capable entity spawns through the monster path AND is
            // registered as an NPC, so it appears in both — see EntitySetup.ConfigureChat.
            GameObject bestTarget = null;
            float bestDist = float.MaxValue;

            ConsiderCandidates(EntityRegistry.NPCs, clickWorldPos, ref bestTarget, ref bestDist);
            ConsiderCandidates(EntityRegistry.Monsters, clickWorldPos, ref bestTarget, ref bestDist);

            if (bestTarget == null)
            {
                // Show "no one nearby" bubble on player.
                ShowPlayerBubble("No hay nadie cerca para hablar...", NO_ONE_BUBBLE_TTL_MS,
                    new Color(0.7f, 0.7f, 0.7f));
                return false;
            }

            OpenChat(bestTarget);
            return true;
        }

        /// <summary>
        /// Scores one registry's entries against <paramref name="from"/>, keeping the
        /// nearest that is both chat-capable and inside its own range.
        ///
        /// An entry already chosen by an earlier call is skipped by the strict
        /// <c>&lt; bestDist</c> comparison rather than by a seen-set: the same
        /// GameObject measured twice produces the same distance, so the second sighting
        /// can never displace the first.
        /// </summary>
        private void ConsiderCandidates(
            IReadOnlyList<GameObject> candidates, Vector2 from,
            ref GameObject bestTarget, ref float bestDist)
        {
            if (candidates == null) return;

            for (int i = 0; i < candidates.Count; i++)
            {
                var go = candidates[i];
                if (go == null) continue;
                if (go.GetComponent<NPCInteractable>() == null) continue;

                float dist = Vector2.Distance(from, (Vector2)go.transform.position);
                if (dist >= bestDist) continue;

                var persona = ResolvePersona(go);
                float range = persona != null && persona.chatRange > 0f
                    ? persona.chatRange
                    : _defaultChatRange;
                if (dist > range) continue;

                bestDist = dist;
                bestTarget = go;
            }
        }

        /// <summary>Open chat with a specific NPC.</summary>
        public void OpenChat(GameObject target)
        {
            _chatTarget = target;
            var interactable = target.GetComponent<NPCInteractable>();
            string npcName = interactable != null ? interactable.NPCName : target.name;
            _activePersona = ResolvePersona(target);
            _chatOpen = true;
            _history.Clear();
            _pendingChunks.Clear();

            // ── Load / create persistent memory ──────────────────────────────
            string npcKey = (_activePersona?.personaId ?? npcName) + "-" + npcName;
            _activeMemory = NPCMemoryStore.LoadOrCreate(npcKey, _activePersona?.personaId);
            _activeMemory.visitCount++;
            ChatSessionLogger.OpenSession(npcKey, _activePersona?.role ?? "generic");

            SeedHistoryFromMemory(npcName);

            // Ensure bubble on target
            _targetBubble = target.GetComponentInChildren<ChatBubble>();
            if (_targetBubble == null)
            {
                var bubbleGo = new GameObject("ChatBubble");
                bubbleGo.transform.SetParent(target.transform);
                _targetBubble = bubbleGo.AddComponent<ChatBubble>();
                _targetBubble.Initialize(target.transform);
            }

            // Build the player bubble up front so the first message does not pay for the
            // GameObject mid-conversation. Its absence is handled by ShowPlayerBubble.
            ResolvePlayerBubble();

            // Show greeting only on the first-ever visit (hasGreeted persisted).
            if (_activePersona != null && !_activeMemory.hasGreeted && !string.IsNullOrEmpty(_activePersona.greeting))
            {
                AddMessage(npcName, _activePersona.greeting);
                ShowTargetBubble(_activePersona.greeting, NPC_BUBBLE_TTL_MS);
                _activeMemory.hasGreeted = true;

                // The greeting is authored text that never passed through a provider, so it
                // carries no expression of its own and has to be read the same way an
                // offline line is. Set BEFORE OnChatOpened fires, so the panel builds with
                // the right face rather than opening neutral and correcting itself.
                SetExpression(ClassifySpoken(_activePersona.greeting));
            }

            NPCMemoryStore.Save(_activeMemory);

            OnChatOpened?.Invoke();
            Debug.Log($"[ChatSystem] Chat opened with {npcName} (visit #{_activeMemory.visitCount})");
        }

        /// <summary>
        /// Replays the persisted conversation into <see cref="History"/>, so re-opening a
        /// chat resumes it instead of starting from a blank panel.
        ///
        /// <para>This is the half that made the memory layer mean something. Every message
        /// was already being written to <c>NPCMemory.ephemeralHistory</c> and re-read on the
        /// next open — and then <c>OpenChat</c> cleared the in-memory history two lines
        /// later, so the twelve messages on disk reached no screen and no provider. The data
        /// was correct, persisted, migrated and invisible.</para>
        ///
        /// <para>It deliberately does NOT go through <c>AddMessage</c>: these lines are
        /// already in memory and already in a previous session's log, so re-appending them
        /// would duplicate the record on every open and grow it without bound. Nor does it
        /// raise <c>OnMessageReceived</c> — the panel reads <see cref="History"/> directly
        /// when it opens, and firing the event here would make a bubble pop over the NPC for
        /// each remembered line.</para>
        /// </summary>
        private void SeedHistoryFromMemory(string npcName)
        {
            if (_activeMemory?.ephemeralHistory == null) return;

            string npcLabel = _activePersona != null && !string.IsNullOrEmpty(_activePersona.displayName)
                ? _activePersona.displayName
                : npcName;

            foreach (var message in _activeMemory.ephemeralHistory)
            {
                if (string.IsNullOrWhiteSpace(message.content)) continue;

                _history.Add(new ChatMessage
                {
                    sender = message.role == "user" ? PLAYER_SENDER : npcLabel,
                    // Time.time is this session's clock, so a remembered line has no
                    // meaningful timestamp in it. Zero says "before this session" rather
                    // than claiming the line was spoken the instant the panel opened.
                    text = message.content,
                    timestamp = 0f,
                });
            }

            // The memory cap (12) is larger than the panel's (10), so a full recall
            // overflows by design and the oldest exchanges fall off exactly as they do
            // during a live conversation.
            while (_history.Count > MAX_HISTORY)
                _history.RemoveAt(0);
        }

        /// <summary>
        /// Forgets everything about the character being talked to and starts the
        /// conversation over: the record on disk, the visit count, the greeting flag and the
        /// panel's transcript. Returns false when no conversation is open.
        ///
        /// <para>A testing control, and it earns its place while the dialogue is being
        /// tuned: an NPC's reply depends on what it remembers, so seeing a prompt change
        /// from a clean slate otherwise means finding and deleting a file under
        /// <c>Application.persistentDataPath</c> between runs. It is the ONLY thing in the
        /// chat that destroys player data, which is why it lives behind a confirm in the
        /// panel rather than on a key.</para>
        ///
        /// <para>The greeting is re-spoken rather than merely re-armed: <c>hasGreeted</c>
        /// going false with nothing said would leave the panel blank and the flag primed to
        /// fire on the NEXT open, which is neither the old state nor a fresh one.</para>
        /// </summary>
        public bool ResetActiveMemory()
        {
            if (!_chatOpen || _activeMemory == null) return false;

            string npcKey = _activeMemory.npcKey;
            string personaId = _activeMemory.personaId;

            NPCMemoryStore.Delete(npcKey);

            _history.Clear();
            _pendingChunks.Clear();

            // Cancel anything the provider still owes this conversation, or a reply to the
            // question that was just forgotten would arrive into the fresh transcript.
            _replyCts?.Cancel();
            _replyCts = null;

            _activeMemory = NPCMemoryStore.LoadOrCreate(npcKey, personaId);
            _activeMemory.visitCount = 1;

            if (_activePersona != null && !string.IsNullOrEmpty(_activePersona.greeting))
            {
                AddMessage(_activePersona.displayName ?? npcKey, _activePersona.greeting);
                ShowTargetBubble(_activePersona.greeting, NPC_BUBBLE_TTL_MS);
                _activeMemory.hasGreeted = true;
            }

            NPCMemoryStore.Save(_activeMemory);
            OnHistoryReset?.Invoke();

            Debug.Log($"[ChatSystem] Memory reset for '{npcKey}'.");
            return true;
        }

        /// <summary>
        /// The vendor the open conversation is with, or null when this character does not
        /// trade. The chat panel shows its Trade button on exactly this condition.
        /// </summary>
        public VendorNPC ActiveVendor =>
            _chatOpen && _chatTarget != null ? _chatTarget.GetComponent<VendorNPC>() : null;

        /// <summary>
        /// Opens the shop of the character being talked to.
        ///
        /// Routed through <see cref="NPCInteractable.Interact"/> rather than calling
        /// <c>VendorShopUI.OpenShop</c> directly, so the vendor's own handler stays the
        /// single place that resolves the player's inventory and wallet. That handler had
        /// no caller anywhere in the project until now — the shop was authored, wired and
        /// unreachable.
        /// </summary>
        public bool TryOpenTradeWithTarget()
        {
            if (!_chatOpen || _chatTarget == null) return false;
            if (_chatTarget.GetComponent<VendorNPC>() == null) return false;

            var interactable = _chatTarget.GetComponent<NPCInteractable>();
            if (interactable == null) return false;

            interactable.Interact();
            return true;
        }
    }
}