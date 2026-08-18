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
        private const int REPLY_CHUNK_WORDS = 8;
        private const float REPLY_CHUNK_DELAY_SEC = 3.0f;
        private const int NPC_BUBBLE_TTL_MS = 2600;
        private const int PLAYER_BUBBLE_TTL_MS = 2800;
        private const int CANCEL_BUBBLE_TTL_MS = 2000;
        private const int NO_ONE_BUBBLE_TTL_MS = 2600;

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

            // Find nearest NPC with chat capability in range
            GameObject bestTarget = null;
            float bestDist = float.MaxValue;

            foreach (var npcGo in EntityRegistry.NPCs)
            {
                if (npcGo == null) continue;
                var interactable = npcGo.GetComponent<NPCInteractable>();
                if (interactable == null) continue;

                float dist = Vector2.Distance(clickWorldPos, (Vector2)npcGo.transform.position);
                string npcName = interactable.NPCName;
                var persona = _catalog != null ? _catalog.GetPersona(npcName) : null;
                float range = persona != null ? persona.chatRange : _defaultChatRange;

                if (dist <= range && dist < bestDist)
                {
                    bestDist = dist;
                    bestTarget = npcGo;
                }
            }

            // Also check monsters that might have chat
            foreach (var monGo in EntityRegistry.Monsters)
            {
                if (monGo == null) continue;
                var interactable = monGo.GetComponent<NPCInteractable>();
                if (interactable == null) continue;

                float dist = Vector2.Distance(clickWorldPos, (Vector2)monGo.transform.position);
                string name = interactable.NPCName;
                var persona = _catalog != null ? _catalog.GetPersona(name) : null;
                float range = persona != null ? persona.chatRange : _defaultChatRange;

                if (dist <= range && dist < bestDist)
                {
                    bestDist = dist;
                    bestTarget = monGo;
                }
            }

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

        /// <summary>Open chat with a specific NPC.</summary>
        public void OpenChat(GameObject target)
        {
            _chatTarget = target;
            var interactable = target.GetComponent<NPCInteractable>();
            string npcName = interactable != null ? interactable.NPCName : target.name;
            _activePersona = _catalog != null ? _catalog.GetPersona(npcName) : null;
            _chatOpen = true;
            _history.Clear();
            _pendingChunks.Clear();

            // ── Load / create persistent memory ──────────────────────────────
            string npcKey = (_activePersona?.personaId ?? npcName) + "-" + npcName;
            _activeMemory = NPCMemoryStore.LoadOrCreate(npcKey, _activePersona?.personaId);
            _activeMemory.visitCount++;
            ChatSessionLogger.OpenSession(npcKey, _activePersona?.role ?? "generic");

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
            }

            NPCMemoryStore.Save(_activeMemory);

            OnChatOpened?.Invoke();
            Debug.Log($"[ChatSystem] Chat opened with {npcName} (visit #{_activeMemory.visitCount})");
        }

        /// <summary>Close current chat.</summary>
    }
}