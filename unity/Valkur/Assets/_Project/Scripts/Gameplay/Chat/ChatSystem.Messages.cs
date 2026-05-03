using System;
using System.Threading.Tasks;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Chat
{
    public partial class ChatSystem : SingletonMonoBehaviour<ChatSystem>
    {

        public void CloseChat()
        {
            if (!_chatOpen) return;
            _chatOpen = false;
            _chatTarget = null;
            _activePersona = null;
            _pendingChunks.Clear();

            // Persist memory and close log session.
            if (_activeMemory != null)
                NPCMemoryStore.Save(_activeMemory);
            ChatSessionLogger.CloseSession();

            // Cancel any in-flight provider call.
            _replyCts?.Cancel();
            _replyCts = null;
            _activeMemory = null;

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

            // ESC closes chat. Routed through KeyboardInputManager for legacy fallback.
            if (_chatOpen && Valkur.Core.Input.KeyboardInputManager.WasEscapePressedThisFrame())
                CloseChat();
        }

        // ── Reply Generation ──

        /// <summary>
        /// Fire-and-forget async reply. The provider runs (possibly on a thread pool)
        /// and delivers the result back to the main thread via ScheduleReplyChunks.
        /// Uses async void deliberately — it is a top-level event handler with a
        /// try/catch guard so no exception is silently lost.
        /// Maps to Python's ChatRouterSystem._route_message() async path.
        /// </summary>
        private async void GenerateReply(string playerText)
        {
            if (_provider == null || _activePersona == null) return;

            // Cancel any previous pending reply before starting a new one.
            _replyCts?.Cancel();
            _replyCts = new System.Threading.CancellationTokenSource();
            var token = _replyCts.Token;

            string npcName = _activePersona.displayName ?? "NPC";

            try
            {
                string reply = await _provider.GenerateReplyAsync(
                    _activePersona, _activeMemory, playerText, token);

                if (string.IsNullOrEmpty(reply)) reply = "...";

                // Schedule reply in chunks (maps to Python MessageScheduler.schedule_reply_chunks).
                ScheduleReplyChunks(npcName, reply);
            }
            catch (System.OperationCanceledException)
            {
                // Chat was closed while the provider was working — silently discard.
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ChatSystem] Provider '{_provider?.ProviderName}' failed: {ex}");
                // Deliver a fallback reply so the chat doesn't silently stall.
                ScheduleReplyChunks(npcName, "...");
            }
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

            // Persist to ephemeral memory and session log.
            if (_activeMemory != null)
            {
                string role = sender == "Player" ? "user" : "assistant";
                NPCMemoryStore.AppendEphemeral(_activeMemory, role, text);
            }
            ChatSessionLogger.LogLine(sender, text);

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