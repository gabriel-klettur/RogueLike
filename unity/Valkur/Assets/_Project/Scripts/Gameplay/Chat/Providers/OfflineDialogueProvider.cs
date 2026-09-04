using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Valkur.Data;
using Valkur.Gameplay.Chat;

namespace Valkur.Gameplay.Chat.Providers
{
    /// <summary>
    /// Answers a player line from the character's OWN authored material, with no network
    /// and no generation.
    ///
    /// <para>Three pools, chosen by what the player appears to be doing
    /// (<see cref="DialogueIntentClassifier"/>): a haggle is answered from the persona's
    /// negotiation phrases, a question about the world from its small talk, a request for a
    /// joke from its humour, and everything else — the honest majority — from the ordinary
    /// dialogue repertoire. Every branch ends in a sentence a human wrote for this
    /// character, which is the whole reason an offline provider can be worth having.</para>
    ///
    /// <para>The ordinary repertoire still advances STRICTLY in order. That is a real
    /// contract, pinned by <c>OfflineDialogueProviderTests</c>, and it is also the right
    /// behaviour: an author reading their lines back wants to see all of them, in the order
    /// they wrote them, not a shuffle that hides the last one behind luck. What changes here
    /// is only that a line is never repeated back-to-back across pools.</para>
    ///
    /// <para>State is per-provider and per-persona, and is NOT persisted: a session picks up
    /// where the last conversation left off, a restart starts again at the top. Domain
    /// Reload is OFF, and this class is instantiated fresh by <c>ChatSystem</c> on each Play
    /// entry rather than being static, so it needs no reset hook.</para>
    /// </summary>
    public sealed class OfflineDialogueProvider : IChatProvider
    {
        /// <summary>What an NPC with nothing authored says. Pinned by the provider's tests.</summary>
        private const string EMPTY_FALLBACK = "...";

        // Offline provider is always "available" — it never needs a network.
        public bool IsOnline => true;
        public string ProviderName => "offline";

        // Per-persona cursor over dialogueLines (key: persona.personaId or displayName).
        private readonly Dictionary<string, int> _cursorByPersona = new Dictionary<string, int>();

        // The last thing each persona said, so a pool switch cannot echo it straight back.
        private readonly Dictionary<string, string> _lastLineByPersona = new Dictionary<string, string>();

        /// <summary>
        /// Always a spoken reply, never a trade. This provider selects from lines a human
        /// wrote; it has no way to decide that a particular sentence meant "two borsch", and
        /// guessing from keywords would spend the player's coins on a coincidence.
        /// </summary>
        public Task<ChatReply> GenerateReplyAsync(ChatRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(
                ChatReply.Spoken(Compose(request.Persona, request.Memory, request.PlayerText)));
        }

        private string Compose(NPCPersonaDefinition persona, NPCMemory memory, string playerText)
        {
            if (persona == null) return EMPTY_FALLBACK;

            string key = PersonaKey(persona);

            // In-process first, then what this NPC is remembered to have said last. The
            // second half is what makes "don't say the same thing twice" survive a restart:
            // the cursor and the last-line cache are per-session, so without it an NPC
            // greeted again after a reload opens on the very line the player walked away on.
            if (!_lastLineByPersona.TryGetValue(key, out string previous))
                previous = LastNpcLine(memory);

            string reply = SelectByIntent(persona, playerText, previous) ?? NextLine(persona, key);
            if (string.IsNullOrEmpty(reply)) return EMPTY_FALLBACK;

            _lastLineByPersona[key] = reply;
            return reply;
        }

        /// <summary>
        /// A line from the pool that matches the player's intent, or null to fall through to
        /// the ordinary repertoire.
        ///
        /// Returns null for every intent whose pool is empty rather than substituting a
        /// neighbouring pool: a persona with no negotiation phrases has nothing to say about
        /// haggling specifically, and its normal repertoire is a better answer than another
        /// character's idea of one.
        /// </summary>
        private static string SelectByIntent(NPCPersonaDefinition persona, string playerText, string previous)
        {
            var profile = persona.profile;
            if (profile == null) return null;

            switch (DialogueIntentClassifier.Classify(playerText))
            {
                case DialogueIntent.Trade:
                    return PickDifferent(profile.negotiation?.phrases, previous);

                case DialogueIntent.Joke:
                    return PickDifferent(profile.humour != null && profile.humour.enabled
                        ? profile.humour.examples
                        : null, previous);

                case DialogueIntent.SmallTalk:
                case DialogueIntent.Greeting:
                case DialogueIntent.Farewell:
                    return PickDifferent(profile.smallTalk?.examples, previous);

                default:
                    return null;
            }
        }

        /// <summary>
        /// The first entry of <paramref name="pool"/> that is not <paramref name="previous"/>.
        ///
        /// First rather than random: these pools hold two or three lines, so a random pick
        /// repeats about a third of the time and reads as the NPC not having heard the
        /// question. Skipping the previous line is enough to make a second question feel
        /// answered.
        /// </summary>
        private static string PickDifferent(List<string> pool, string previous)
        {
            if (pool == null || pool.Count == 0) return null;

            foreach (string line in pool)
            {
                if (!string.IsNullOrWhiteSpace(line) && line != previous) return line;
            }
            return null;
        }

        /// <summary>
        /// The next line of the persona's ordinary repertoire, advancing its cursor.
        /// Wraps around; unchanged from the behaviour this provider shipped with.
        /// </summary>
        private string NextLine(NPCPersonaDefinition persona, string key)
        {
            if (persona.dialogueLines == null || persona.dialogueLines.Count == 0)
                return EMPTY_FALLBACK;

            int index = _cursorByPersona.TryGetValue(key, out int cursor) ? cursor : 0;
            string line = persona.dialogueLines[index % persona.dialogueLines.Count];
            _cursorByPersona[key] = index + 1;
            return line;
        }

        /// <summary>
        /// The last line this NPC is remembered to have spoken, or null.
        ///
        /// Read from the persisted history rather than tracked here, because this provider
        /// is constructed fresh on every Play entry while <see cref="NPCMemory"/> is what
        /// crosses that boundary.
        /// </summary>
        private static string LastNpcLine(NPCMemory memory)
        {
            var history = memory?.ephemeralHistory;
            if (history == null) return null;

            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i].role == "assistant" && !string.IsNullOrWhiteSpace(history[i].content))
                    return history[i].content;
            }
            return null;
        }

        private static string PersonaKey(NPCPersonaDefinition persona)
        {
            if (!string.IsNullOrEmpty(persona.personaId)) return persona.personaId;
            return !string.IsNullOrEmpty(persona.displayName) ? persona.displayName : "unknown";
        }
    }
}
