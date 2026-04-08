using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
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
    public class ChatSystem : SingletonMonoBehaviour<ChatSystem>
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
        private int _dialogueLineIndex;

        // ── Events ──
        public event Action OnChatOpened;
        public event Action OnChatClosed;
        public event Action<string, string> OnMessageReceived; // (sender, text)

        // ── Public API ──

        public bool IsChatOpen => _chatOpen;
        public GameObject ChatTarget => _chatTarget;
        public NPCPersonaDefinition ActivePersona => _activePersona;
        public IReadOnlyList<ChatMessage> History => _history;

        protected override bool Persist => false;

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
                // Show "no one nearby" bubble on player
                EnsurePlayerBubble();
                _playerBubble.PushBubble("No hay nadie cerca para hablar...", NO_ONE_BUBBLE_TTL_MS,
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
            _dialogueLineIndex = 0;

            // Ensure bubble on target
            _targetBubble = target.GetComponentInChildren<ChatBubble>();
            if (_targetBubble == null)
            {
                var bubbleGo = new GameObject("ChatBubble");
                bubbleGo.transform.SetParent(target.transform);
                _targetBubble = bubbleGo.AddComponent<ChatBubble>();
                _targetBubble.Initialize(target.transform);
            }

            EnsurePlayerBubble();

            // Show greeting if persona has one
            if (_activePersona != null && !string.IsNullOrEmpty(_activePersona.greeting))
            {
                AddMessage(npcName, _activePersona.greeting);
                _targetBubble.PushBubble(_activePersona.greeting, NPC_BUBBLE_TTL_MS);
            }

            OnChatOpened?.Invoke();
            Debug.Log($"[ChatSystem] Chat opened with {npcName}");
        }

        /// <summary>Close current chat.</summary>
        public void CloseChat()
        {
            if (!_chatOpen) return;
            _chatOpen = false;
            _chatTarget = null;
            _activePersona = null;
            _pendingChunks.Clear();
            OnChatClosed?.Invoke();
        }

        /// <summary>
        /// Submit a player message. Routes to NPC reply generation.
        /// Maps to Python ChatRouterSystem._route_message().
        /// </summary>
        public void SubmitPlayerMessage(string text)
        {
            if (!_chatOpen || string.IsNullOrWhiteSpace(text)) return;

            string playerName = "Player";
            AddMessage(playerName, text);

            // Show player bubble
            EnsurePlayerBubble();
            _playerBubble.PushBubble(text, PLAYER_BUBBLE_TTL_MS, Color.cyan);

            // Generate NPC reply (offline mode: cycle through dialogue lines)
            GenerateReply(text);
        }

        private void Update()
        {
            // Drain scheduled chunks
            if (_pendingChunks.Count > 0 && Time.time >= _nextChunkTime)
            {
                var chunk = _pendingChunks.Dequeue();
                if (_targetBubble != null)
                    _targetBubble.PushBubble(chunk.text, NPC_BUBBLE_TTL_MS);
                AddMessage(chunk.sender, chunk.text);
                _nextChunkTime = Time.time + REPLY_CHUNK_DELAY_SEC;
            }

            // ESC closes chat
            if (_chatOpen && Input.GetKeyDown(KeyCode.Escape))
                CloseChat();
        }

        // ── Reply Generation ──

        private void GenerateReply(string playerText)
        {
            string npcName = _activePersona != null ? _activePersona.displayName : "NPC";

            // Offline mode: use pre-written dialogue lines from persona
            string reply = GetNextDialogueLine();
            if (string.IsNullOrEmpty(reply))
            {
                reply = "...";
            }

            // Schedule reply in chunks (maps to Python MessageScheduler.schedule_reply_chunks)
            ScheduleReplyChunks(npcName, reply);
        }

        private string GetNextDialogueLine()
        {
            if (_activePersona == null || _activePersona.dialogueLines.Count == 0)
                return null;

            string line = _activePersona.dialogueLines[_dialogueLineIndex % _activePersona.dialogueLines.Count];
            _dialogueLineIndex++;
            return line;
        }

        private void ScheduleReplyChunks(string sender, string fullReply)
        {
            string[] words = fullReply.Split(' ');
            int idx = 0;

            while (idx < words.Length)
            {
                int chunkEnd = Mathf.Min(idx + REPLY_CHUNK_WORDS, words.Length);
                string chunk = string.Join(" ", words, idx, chunkEnd - idx);
                _pendingChunks.Enqueue(new ScheduledChunk { sender = sender, text = chunk });
                idx = chunkEnd;
            }

            _nextChunkTime = Time.time + 0.5f; // Short initial delay
        }

        private void AddMessage(string sender, string text)
        {
            _history.Add(new ChatMessage { sender = sender, text = text, timestamp = Time.time });
            while (_history.Count > MAX_HISTORY)
                _history.RemoveAt(0);
            OnMessageReceived?.Invoke(sender, text);
        }

        private void EnsurePlayerBubble()
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) return;

            _playerBubble = player.GetComponentInChildren<ChatBubble>();
            if (_playerBubble == null)
            {
                var bubbleGo = new GameObject("PlayerChatBubble");
                bubbleGo.transform.SetParent(player);
                _playerBubble = bubbleGo.AddComponent<ChatBubble>();
                _playerBubble.Initialize(player);
            }
        }

        // ── Data Structures ──

        [Serializable]
        public struct ChatMessage
        {
            public string sender;
            public string text;
            public float timestamp;
        }

        private struct ScheduledChunk
        {
            public string sender;
            public string text;
        }
    }
}
