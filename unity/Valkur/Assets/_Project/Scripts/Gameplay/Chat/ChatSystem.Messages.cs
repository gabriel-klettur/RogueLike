using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.NPC;

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

            // ESC closes chat (New Input System)
            var kb = Keyboard.current;
            if (_chatOpen && kb != null && kb.escapeKey.wasPressedThisFrame)
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