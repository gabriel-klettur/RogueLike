using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Chat.Providers
{
    /// <summary>
    /// Answers as the NPC by asking a language model, with the authored repertoire as the
    /// floor underneath it.
    ///
    /// <para>THE FALLBACK IS THE POINT. Every failure this can hit — no key, no network, a
    /// timeout, a 4xx, a malformed body, the player closing the chat mid-request — ends in
    /// the offline provider answering instead, so a conversation is never left hanging on
    /// something outside the game. A remote call is the only part of this project that can
    /// fail for reasons the repository cannot see.</para>
    ///
    /// <para>WHY UnityWebRequest RATHER THAN HttpClient. It works on every platform Unity
    /// ships to, including WebGL where <c>System.Net.Http</c> does not, and it needs no
    /// assembly reference this project does not already have. The cost is that it must be
    /// started on the main thread — which is where <c>ChatSystem.GenerateReply</c> already
    /// calls from — and awaited through a completion source rather than with a plain await.
    /// </para>
    ///
    /// <para>The key never touches an asset, a log or a save. It is read per-request from
    /// <see cref="EnvFile"/> and put in one header.</para>
    /// </summary>
    public sealed class OpenAiChatProvider : IChatProvider
    {
        private readonly ChatLlmSettings _settings;
        private readonly IChatProvider _fallback;

        /// <summary>
        /// Set once a call has failed for a reason that will not heal within the session —
        /// no key, or a rejected key. Stops a broken configuration from spending a
        /// round-trip and a console warning on every single message.
        /// </summary>
        private bool _disabledForSession;

        /// <summary>Name of the trade tool, in the request and in the response.</summary>
        private const string TRADE_TOOL_NAME = "propose_trade";

        private string _lastFailureReason;

        public OpenAiChatProvider(ChatLlmSettings settings, IChatProvider fallback)
        {
            _settings = settings;
            _fallback = fallback ?? new OfflineDialogueProvider();
        }

        public string ProviderName => _disabledForSession
            ? $"offline (llm off: {_lastFailureReason})"
            : _settings != null ? _settings.model : "offline";

        /// <summary>
        /// True when a call would actually be attempted right now. Read by the console's
        /// status command; deliberately says nothing about the key beyond whether one
        /// resolved.
        /// </summary>
        public bool IsOnline =>
            !_disabledForSession &&
            _settings != null && _settings.IsUsable &&
            _settings.mode != ChatProviderMode.ForceOffline &&
            (_settings.mode == ChatProviderMode.ForceOnline || EnvFile.Has(_settings.apiKeyEnvVar));

        public async Task<ChatReply> GenerateReplyAsync(ChatRequest request, CancellationToken cancellationToken)
        {
            if (!ShouldAttempt(out string apiKey))
                return await _fallback.GenerateReplyAsync(request, cancellationToken);

            try
            {
                ChatReply reply = await RequestAsync(request, apiKey, cancellationToken);

                // A reply with only an action and no words is legal — the model chose to act
                // rather than speak — and the game supplies the sentence for it.
                if (!string.IsNullOrWhiteSpace(reply.Text) || reply.Proposal.IsSomething)
                    return new ChatReply(reply.Text?.Trim() ?? "", reply.Proposal);

                // A 200 with nothing usable in it is a content filter or a truncated
                // reasoning turn. Neither is worth a warning per message, and both are
                // recoverable on the next try, so this does not disable the session.
                VerboseLog.Log(VerboseLog.Category.Bootstrap,
                    () => "[OpenAiChatProvider] Empty reply; answering from the persona instead.");
            }
            catch (OperationCanceledException)
            {
                // The player closed the chat or sent another line. Neither is an error, and
                // there is nobody left to answer.
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OpenAiChatProvider] {ex.GetType().Name}: {ex.Message}. " +
                                 "Answering from the persona's authored lines instead.");
            }

            return await _fallback.GenerateReplyAsync(request, cancellationToken);
        }

        // ── Gate ────────────────────────────────────────────────────────────

        private bool ShouldAttempt(out string apiKey)
        {
            apiKey = null;
            if (_disabledForSession) return false;
            if (_settings == null || !_settings.IsUsable) return false;
            if (_settings.mode == ChatProviderMode.ForceOffline) return false;

            if (!EnvFile.TryGet(_settings.apiKeyEnvVar, out apiKey))
            {
                // Auto mode is the shipped default and a missing key is its NORMAL state —
                // anyone cloning this repository has no key and should still get NPCs that
                // talk, silently. Only an explicit ForceOnline deserves a warning.
                if (_settings.mode == ChatProviderMode.ForceOnline)
                {
                    Debug.LogWarning(
                        $"[OpenAiChatProvider] Mode is ForceOnline but '{_settings.apiKeyEnvVar}' " +
                        $"resolved to nothing (looked in the environment, then {EnvFile.ResolvePath()}).");
                }
                DisableForSession("no key");
                return false;
            }

            return true;
        }

        private void DisableForSession(string reason)
        {
            _disabledForSession = true;
            _lastFailureReason = reason;
        }

        // ── Request ─────────────────────────────────────────────────────────

        private async Task<ChatReply> RequestAsync(
            ChatRequest request, string apiKey, CancellationToken cancellationToken)
        {
            var persona = request.Persona;
            var memory = request.Memory;

            string language = memory != null && !string.IsNullOrEmpty(memory.preferredLanguage)
                ? memory.preferredLanguage
                : "es";

            var messages = new List<object>
            {
                Message("system", PersonaPromptBuilder.BuildSystemPrompt(
                    persona, memory, request.Trade, _settings.sharedSystemRules, language)),
            };

            // The remembered turns already END with the player's newest line: ChatSystem
            // records it before asking for a reply. Appending playerText again would show
            // the model the same question twice and invite it to answer "as I said…".
            var history = PersonaPromptBuilder.BuildHistory(memory, _settings.historyTurns);
            foreach (var (role, content) in history) messages.Add(Message(role, content));

            if (history.Count == 0 || history[history.Count - 1].role != "user")
                messages.Add(Message("user", request.PlayerText));

            var body = new Dictionary<string, object>
            {
                { "model", _settings.model },
                { "messages", messages },
                { _settings.maxOutputTokensField, _settings.maxOutputTokens },
            };
            if (_settings.sendTemperature) body["temperature"] = _settings.temperature;
            if (!string.IsNullOrWhiteSpace(_settings.reasoningEffort))
                body["reasoning_effort"] = _settings.reasoningEffort.Trim();

            // Only a vendor with something on the counter is given the tool. Handing it to a
            // tree would let it offer trades it cannot make, and the model reaches for a
            // tool it has been given far more readily than one it has not.
            if (request.Trade.IsVendor && request.Trade.StockCount > 0)
                body["tools"] = BuildTradeTools();

            string json = MiniJsonRuntime.Serialize(body);
            string response = await PostAsync(json, apiKey, cancellationToken);
            return ExtractReply(response);
        }

        /// <summary>
        /// The one tool a vendor gets: propose a trade.
        ///
        /// <para>PROPOSE, not execute, and the name says so on purpose — a model given a
        /// function called "buy" behaves as though the purchase is done and narrates it in
        /// the same turn. What comes back here is a claim the game then checks against the
        /// live shop and puts in front of the player for confirmation.</para>
        ///
        /// <para>The id is a free string rather than an enum of the current stock. An enum
        /// would be tighter, but the counter changes as things sell out and a schema that
        /// tracks it would be rebuilt per message; the id is validated against the live shop
        /// either way, so a hallucinated one is refused rather than believed.</para>
        /// </summary>
        private static List<object> BuildTradeTools()
        {
            var itemId = new Dictionary<string, object>
            {
                { "type", "string" },
                { "description", "Id exacto del articulo, tal y como aparece entre parentesis en la lista del puesto." },
            };
            var quantity = new Dictionary<string, object>
            {
                { "type", "integer" },
                { "minimum", 1 },
                { "description", "Cuantas unidades. 1 si el viajero no dice un numero." },
            };
            var action = new Dictionary<string, object>
            {
                { "type", "string" },
                { "enum", new List<object> { "buy", "sell" } },
                { "description", "buy = el viajero te compra a ti. sell = el viajero te vende a ti." },
            };

            var parameters = new Dictionary<string, object>
            {
                { "type", "object" },
                {
                    "properties", new Dictionary<string, object>
                    {
                        { "action", action },
                        { "item_id", itemId },
                        { "quantity", quantity },
                    }
                },
                { "required", new List<object> { "action", "item_id", "quantity" } },
            };

            var function = new Dictionary<string, object>
            {
                { "name", TRADE_TOOL_NAME },
                {
                    "description",
                    "Propon un trato cuando el viajero deje claro QUE quiere y CUANTO. " +
                    "No lo llames para preguntar ni para charlar. El trato no se cierra aqui: " +
                    "el viajero tendra que confirmarlo."
                },
                { "parameters", parameters },
            };

            return new List<object>
            {
                new Dictionary<string, object>
                {
                    { "type", "function" },
                    { "function", function },
                }
            };
        }

        private static Dictionary<string, object> Message(string role, string content) =>
            new Dictionary<string, object> { { "role", role }, { "content", content } };

        /// <summary>
        /// Sends the request and returns the raw body.
        ///
        /// <para>The completion source is what bridges Unity's operation callback to the
        /// Task this interface returns. <c>UnityWebRequest.SendWebRequest</c> must be called
        /// on the main thread, which is where <c>ChatSystem.GenerateReply</c> runs; the
        /// continuation resumes there too, so the caller can touch Unity objects safely.</para>
        /// </summary>
        private async Task<string> PostAsync(string json, string apiKey, CancellationToken cancellationToken)
        {
            using (var request = new UnityWebRequest(_settings.endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + apiKey);
                request.timeout = Mathf.CeilToInt(_settings.timeoutSeconds);

                var completion = new TaskCompletionSource<bool>();
                var operation = request.SendWebRequest();
                operation.completed += _ => completion.TrySetResult(true);

                using (cancellationToken.Register(() =>
                {
                    // Abort makes the operation complete, which resolves the task below.
                    try { request.Abort(); } catch (Exception) { /* already done */ }
                }))
                {
                    await completion.Task;
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    // 401 and 403 will not heal by retrying with the same key, and repeating
                    // them costs a round-trip and a warning on every message the player
                    // types. Everything else — a timeout, a 429, a 5xx — is worth retrying.
                    long code = request.responseCode;
                    if (code == 401 || code == 403) DisableForSession("key rejected (" + code + ")");

                    throw new Exception($"HTTP {code}: {request.error}. {Excerpt(request.downloadHandler?.text)}");
                }

                return request.downloadHandler.text;
            }
        }

        // ── Response ────────────────────────────────────────────────────────

        /// <summary>
        /// Pulls <c>choices[0].message.content</c> out of the body, tolerating both a plain
        /// string and the segmented form some models return.
        /// </summary>
        private static ChatReply ExtractReply(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return ChatReply.Spoken(null);
            if (!(MiniJsonRuntime.Deserialize(body) is Dictionary<string, object> root))
                return ChatReply.Spoken(null);

            if (root.TryGetValue("error", out object error) &&
                error is Dictionary<string, object> errorObj &&
                errorObj.TryGetValue("message", out object errorMessage))
            {
                // A 200 carrying an error object is unusual but has happened; surfacing it
                // as a thrown message is what routes it to the fallback with a reason.
                throw new Exception("API error: " + errorMessage);
            }

            if (!(root.TryGetValue("choices", out object choicesRaw) && choicesRaw is List<object> choices))
                return ChatReply.Spoken(null);
            if (choices.Count == 0 || !(choices[0] is Dictionary<string, object> choice))
                return ChatReply.Spoken(null);
            if (!(choice.TryGetValue("message", out object messageRaw) &&
                  messageRaw is Dictionary<string, object> message))
                return ChatReply.Spoken(null);

            string text = ExtractContent(message);
            TradeProposal proposal = ExtractProposal(message);

            // An empty reply that stopped on "length" is the budget being spent before a
            // single word was written — on a reasoning model, thinking is billed against the
            // same allowance as the answer. It is worth its own message because the symptom
            // in game (an NPC falling back to canned lines) is identical to the model never
            // having been called, and the fix is a number rather than anything in the code.
            //
            // A tool call is a real answer, so it does not count as empty.
            bool blank = string.IsNullOrWhiteSpace(text) && !proposal.IsSomething;
            if (blank && choice.TryGetValue("finish_reason", out object finish) &&
                (finish as string) == "length")
            {
                throw new Exception(
                    "the reply budget ran out before any text was produced — raise " +
                    "ChatLlmSettings.maxOutputTokens, or lower reasoningEffort");
            }

            return new ChatReply(text, proposal);
        }

        /// <summary>
        /// The spoken half, tolerating both a plain string and the segmented form some
        /// models return.
        /// </summary>
        private static string ExtractContent(Dictionary<string, object> message)
        {
            if (!message.TryGetValue("content", out object content) || content == null) return null;
            if (content is string text) return text;

            // Segmented content: [{ "type": "text", "text": "..." }, ...]
            if (content is List<object> parts)
            {
                var sb = new System.Text.StringBuilder();
                foreach (object part in parts)
                {
                    if (part is Dictionary<string, object> segment &&
                        segment.TryGetValue("text", out object segmentText) &&
                        segmentText is string s)
                        sb.Append(s);
                }
                return sb.Length > 0 ? sb.ToString() : null;
            }

            return null;
        }

        /// <summary>
        /// The proposed trade, if the model called the tool.
        ///
        /// <para>Everything here is read defensively and nothing is trusted. The arguments
        /// arrive as a JSON STRING inside the response — a string the model wrote — so it
        /// can be malformed, carry the wrong types, or name an item that does not exist.
        /// Every one of those ends as <see cref="TradeProposal.None"/> and the conversation
        /// carries on; the alternative is an exception thrown at a player mid-sentence.</para>
        /// </summary>
        private static TradeProposal ExtractProposal(Dictionary<string, object> message)
        {
            if (!(message.TryGetValue("tool_calls", out object callsRaw) && callsRaw is List<object> calls))
                return TradeProposal.None;

            foreach (object callRaw in calls)
            {
                if (!(callRaw is Dictionary<string, object> call)) continue;
                if (!(call.TryGetValue("function", out object fnRaw) &&
                      fnRaw is Dictionary<string, object> fn)) continue;

                if (!(fn.TryGetValue("name", out object name) && (name as string) == TRADE_TOOL_NAME))
                    continue;
                if (!(fn.TryGetValue("arguments", out object argsRaw) && argsRaw is string argsJson))
                    continue;

                if (!(MiniJsonRuntime.Deserialize(argsJson) is Dictionary<string, object> args))
                {
                    Debug.LogWarning("[OpenAiChatProvider] Tool arguments were not valid JSON; ignoring the proposal.");
                    continue;
                }

                var intent = ParseIntent(args.TryGetValue("action", out object a) ? a as string : null);
                if (intent == TradeIntent.None) continue;

                string itemId = args.TryGetValue("item_id", out object id) ? id as string : null;
                if (string.IsNullOrWhiteSpace(itemId)) continue;

                return new TradeProposal(intent, itemId.Trim(), ParseQuantity(args));
            }

            return TradeProposal.None;
        }

        private static TradeIntent ParseIntent(string action)
        {
            if (string.Equals(action, "buy", StringComparison.OrdinalIgnoreCase)) return TradeIntent.Buy;
            if (string.Equals(action, "sell", StringComparison.OrdinalIgnoreCase)) return TradeIntent.Sell;
            return TradeIntent.None;
        }

        /// <summary>
        /// Quantity, defaulting to one. JSON numbers arrive as <c>long</c> or <c>double</c>
        /// depending on how they were written, and a model sometimes sends "2" as a string.
        /// A missing or unreadable quantity means one unit, never zero — a proposal for zero
        /// of something is a confirmation prompt the player cannot make sense of.
        /// </summary>
        private static int ParseQuantity(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("quantity", out object raw) || raw == null) return 1;

            switch (raw)
            {
                case long l: return l > 0 ? (int)l : 1;
                case int i: return i > 0 ? i : 1;
                case double d: return d >= 1 ? (int)d : 1;
                case string s when int.TryParse(s, out int parsed): return parsed > 0 ? parsed : 1;
                default: return 1;
            }
        }

        /// <summary>
        /// A short slice of an error body for a log line. Capped because an API error body
        /// can be long, and never used for anything but a message.
        /// </summary>
        private static string Excerpt(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            body = body.Replace('\n', ' ').Trim();
            return body.Length <= 300 ? body : body.Substring(0, 300) + "…";
        }
    }
}
