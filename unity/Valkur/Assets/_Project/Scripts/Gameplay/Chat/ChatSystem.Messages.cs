using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Chat.Providers;
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
            ResetExpression();

            // An offer belongs to the conversation it was made in. Leaving one on the table
            // would let the next chat's Confirm button spend coins on the last chat's deal.
            ClearPendingTrade(notify: true);

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

            AddMessage(PLAYER_SENDER, text);

            ShowPlayerBubble(text, PLAYER_BUBBLE_TTL_MS, Color.cyan);

            // Generate NPC reply (offline mode: cycle through dialogue lines)
            GenerateReply(text);
        }

        private void Update()
        {
            // Drain scheduled chunks
            if (_pendingChunks.Count > 0 && Time.time >= _nextChunkTime)
            {
                var chunk = _pendingChunks.Dequeue();
                SetExpression(chunk.expression);
                ShowTargetBubble(chunk.text, NPC_BUBBLE_TTL_MS);
                AddChunkToHistory(chunk.sender, chunk.text);
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

            // The wait is the one moment the panel had nothing at all to say. A remote call
            // is seconds long and this method is fire-and-forget, so without a face here the
            // player types, the panel goes silent and nothing on screen says anyone is
            // listening. An offline provider completes its await synchronously, so this
            // never renders a frame for it — no branch on the provider is needed.
            SetExpression(FacialExpression.Thinking);

            try
            {
                var request = new ChatRequest(
                    _activePersona, _activeMemory, playerText, BuildTradeContext());

                ChatReply reply = await _provider.GenerateReplyAsync(request, token);

                // A proposal is checked against the live shop BEFORE anything is said, so a
                // refusal can be spoken instead of an offer the game would then reject.
                string spoken = reply.Text;
                if (reply.Proposal.IsSomething)
                    spoken = OfferTrade(reply.Proposal, spoken);

                if (string.IsNullOrEmpty(spoken)) spoken = "...";

                // Recorded ONCE, whole, before it is broken up for delivery. What the NPC
                // said and how it was paced on screen are different things: recording each
                // bubble wrote "Si te llevas canasta, te hago precio de" and "vecina" as two
                // separate assistant turns, so the remembered conversation was a list of
                // fragments and the do-not-repeat check compared against "vecina".
                // The face lands with the FIRST bubble rather than here, so the expression
                // and the words it belongs to arrive together — ScheduleReplyChunks holds
                // the first one back half a second, and changing the portrait during that
                // gap reads as the character reacting to something the player cannot see.
                RecordToMemoryAndLog(npcName, spoken);
                ScheduleReplyChunks(npcName, spoken, reply.Expression);
            }
            catch (System.OperationCanceledException)
            {
                // Chat was closed while the provider was working — silently discard.
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ChatSystem] Provider '{_provider?.ProviderName}' failed: {ex}");
                // Deliver a fallback reply so the chat doesn't silently stall. Not recorded:
                // an ellipsis the NPC never chose to say is noise in a remembered
                // conversation, and it would be what the next session refuses to repeat.
                // The face goes back to neutral rather than staying on Thinking, which would
                // leave the character visibly stuck mid-thought forever.
                ScheduleReplyChunks(npcName, "...", FacialExpression.Neutral);
            }
        }

        /// <summary>
        /// What this character can see about the player's ability to pay.
        ///
        /// Read fresh on every message rather than captured when the conversation opened:
        /// the player can buy something from the shop and come back mid-conversation, and a
        /// vendor still talking about the purse they saw two minutes ago is worse than one
        /// that never mentions money.
        /// </summary>
        private ChatTradeContext BuildTradeContext()
        {
            var wallet = EntityRegistry.PlayerTransform != null
                ? EntityRegistry.PlayerTransform.GetComponent<CurrencyWallet>()
                : null;

            return ChatTradeContext.FromLive(ActiveVendor, wallet);
        }

        /// <summary>
        /// Breaks a reply into the bubbles it will be spoken as, one every
        /// <see cref="REPLY_CHUNK_DELAY_SEC"/>.
        ///
        /// <para>The split is by SENTENCE first and only then by word budget. A pure
        /// eight-word cut is what Python did and it is visibly wrong on the short authored
        /// lines this game actually ships: measured live, Gatita's "Si te llevas canasta, te
        /// hago precio de vecina" — nine words — came out as an eight-word bubble followed
        /// three seconds later by a bubble containing the single word "vecina". A sentence
        /// is the unit a person speaks in, so it is the unit to break on.</para>
        ///
        /// <para>A trailing fragment shorter than <see cref="MIN_CHUNK_WORDS"/> is folded
        /// back into the bubble before it. That is deliberately allowed to exceed the word
        /// budget: a slightly long bubble reads as one sentence, an orphaned word reads as a
        /// bug.</para>
        /// </summary>
        private void ScheduleReplyChunks(
            string sender, string fullReply,
            FacialExpression expression = FacialExpression.Neutral)
        {
            foreach (string chunk in SplitIntoSpokenChunks(fullReply))
                _pendingChunks.Enqueue(new ScheduledChunk
                {
                    sender = sender,
                    text = chunk,
                    expression = expression,
                });

            _nextChunkTime = Time.time + 0.5f; // Short initial delay
        }

        /// <summary>
        /// The bubbles <paramref name="fullReply"/> should be spoken as. Public surface is
        /// internal so the tests can state the contract on the split itself rather than
        /// having to drain a queue to see it.
        /// </summary>
        internal static List<string> SplitIntoSpokenChunks(string fullReply)
        {
            var chunks = new List<string>();
            if (string.IsNullOrWhiteSpace(fullReply)) return chunks;

            // Pack WHOLE sentences into a bubble until it is as long as one comfortably
            // reads, rather than cutting every N words. The old fixed cut was sized for the
            // short authored lines and falls apart on anything longer: a model reply came
            // back as five bubbles, one of which was "de remolacha— lista para cocinar y
            // regalarte un", and at REPLY_CHUNK_DELAY_SEC apiece the whole answer took
            // fifteen seconds to finish arriving.
            var current = new List<string>();
            int currentWords = 0;

            foreach (string sentence in SplitSentences(fullReply))
            {
                foreach (string piece in SplitOverlongSentence(sentence))
                {
                    int pieceWords = WordCount(piece);
                    if (currentWords > 0 && currentWords + pieceWords > MAX_BUBBLE_WORDS)
                    {
                        chunks.Add(string.Join(" ", current));
                        current.Clear();
                        currentWords = 0;
                    }

                    current.Add(piece);
                    currentWords += pieceWords;
                }
            }

            if (current.Count > 0) chunks.Add(string.Join(" ", current));

            // Fold a stray tail back into whatever came before it. A one or two word bubble
            // reads as the NPC having been cut off rather than as a pause.
            for (int i = chunks.Count - 1; i > 0; i--)
            {
                if (WordCount(chunks[i]) >= MIN_CHUNK_WORDS) continue;
                chunks[i - 1] = chunks[i - 1] + " " + chunks[i];
                chunks.RemoveAt(i);
            }

            return chunks;
        }

        /// <summary>
        /// One sentence, or the pieces of it when it is longer than a bubble holds.
        ///
        /// Only a genuinely long sentence is ever cut, and the cut is the last resort: there
        /// is no punctuation to break on and something has to give.
        /// </summary>
        private static List<string> SplitOverlongSentence(string sentence)
        {
            var pieces = new List<string>();
            string[] words = sentence.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return pieces;

            if (words.Length <= MAX_BUBBLE_WORDS)
            {
                pieces.Add(string.Join(" ", words));
                return pieces;
            }

            for (int i = 0; i < words.Length; i += MAX_BUBBLE_WORDS)
            {
                int take = Mathf.Min(MAX_BUBBLE_WORDS, words.Length - i);
                pieces.Add(string.Join(" ", words, i, take));
            }
            return pieces;
        }

        /// <summary>
        /// Splits on sentence-ending punctuation, keeping the punctuation with its sentence.
        /// Deliberately naive — it is choosing where to pause a bubble, not parsing prose,
        /// and the cost of a wrong break here is one bubble reading slightly long.
        /// </summary>
        private static List<string> SplitSentences(string text)
        {
            var sentences = new List<string>();
            int start = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '.' && c != '!' && c != '?' && c != '\n') continue;

                // Run past a cluster like "?!" or "..." so it stays with its sentence.
                while (i + 1 < text.Length &&
                       (text[i + 1] == '.' || text[i + 1] == '!' || text[i + 1] == '?'))
                    i++;

                string sentence = text.Substring(start, i - start + 1).Trim();
                if (sentence.Length > 0) sentences.Add(sentence);
                start = i + 1;
            }

            string tail = start < text.Length ? text.Substring(start).Trim() : "";
            if (tail.Length > 0) sentences.Add(tail);

            // A reply with no punctuation at all is one sentence, not zero.
            if (sentences.Count == 0) sentences.Add(text.Trim());
            return sentences;
        }

        private static int WordCount(string text) =>
            text.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries).Length;

        /// <summary>A whole message: shown, remembered and logged.</summary>
        private void AddMessage(string sender, string text)
        {
            AddChunkToHistory(sender, text);
            RecordToMemoryAndLog(sender, text);
        }

        /// <summary>
        /// One bubble of a reply: shown in the panel, not written to the record.
        ///
        /// The panel is a transcript of the conversation as it is being spoken, so it wants
        /// the pieces; <see cref="NPCMemory"/> is a record of what was said, so it wants the
        /// whole line. <see cref="GenerateReply"/> writes that once.
        /// </summary>
        private void AddChunkToHistory(string sender, string text)
        {
            _history.Add(new ChatMessage { sender = sender, text = text, timestamp = Time.time });
            while (_history.Count > MAX_HISTORY)
                _history.RemoveAt(0);

            OnMessageReceived?.Invoke(sender, text);
        }

        /// <summary>Writes one whole message to the persistent memory and the session log.</summary>
        private void RecordToMemoryAndLog(string sender, string text)
        {
            if (_activeMemory != null)
            {
                string role = sender == PLAYER_SENDER ? "user" : "assistant";
                NPCMemoryStore.AppendEphemeral(_activeMemory, role, text);
            }
            ChatSessionLogger.LogLine(sender, text);
        }

        /// <summary>
        /// Returns the player's chat bubble, creating it on first use, or <c>null</c> when
        /// there is no player to attach one to.
        ///
        /// Named Resolve rather than Ensure deliberately. "Ensure" promises a postcondition
        /// this cannot always deliver — EntityRegistry has no player during boot, while a
        /// cutscene owns the camera, or after the player dies — and every call site had
        /// taken that promise at face value and dereferenced the result. Returning the
        /// bubble instead of assigning a field makes the absence impossible to ignore by
        /// accident.
        ///
        /// Nothing outside <see cref="ShowPlayerBubble"/> should call this.
        /// </summary>
        private ChatBubble ResolvePlayerBubble()
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null)
            {
                _playerBubble = null;
                return null;
            }

            _playerBubble = player.GetComponentInChildren<ChatBubble>();
            if (_playerBubble == null)
            {
                var bubbleGo = new GameObject("PlayerChatBubble");
                bubbleGo.transform.SetParent(player);
                _playerBubble = bubbleGo.AddComponent<ChatBubble>();
                _playerBubble.Initialize(player);
            }
            return _playerBubble;
        }

        /// <summary>
        /// The single way anything in ChatSystem shows a bubble over the player.
        ///
        /// A missing player is a normal state, not an error: the conversation still has a
        /// history, still writes memory and still logs. Only the floating text is skipped,
        /// so the caller has nothing to decide and cannot get it wrong.
        /// </summary>
        private void ShowPlayerBubble(string text, int ttlMs, Color color)
        {
            var bubble = ResolvePlayerBubble();
            if (bubble == null) return;
            bubble.PushBubble(text, ttlMs, color);
        }

        /// <summary>
        /// The single way anything in ChatSystem shows a bubble over the NPC.
        ///
        /// The target can be destroyed mid-conversation — killed, despawned by the zone
        /// streamer — while queued reply chunks are still draining, so this is null-safe
        /// for the same reason as its player-side twin.
        /// </summary>
        private void ShowTargetBubble(string text, int ttlMs)
        {
            if (_targetBubble == null) return;
            _targetBubble.PushBubble(text, ttlMs);
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

            /// <summary>
            /// The face to be wearing while this bubble is on screen. Carried per CHUNK
            /// rather than per reply so the change lands with the words it belongs to —
            /// applying it when the reply arrives would move the portrait half a second
            /// before the first bubble appears, which reads as the character reacting to
            /// nothing. Every chunk of one reply currently carries the same value and
            /// <c>ApplyExpression</c> refuses a repeat, so the extra field costs nothing
            /// until a provider has something per-sentence to say.
            /// </summary>
            public FacialExpression expression;
        }
    }
}