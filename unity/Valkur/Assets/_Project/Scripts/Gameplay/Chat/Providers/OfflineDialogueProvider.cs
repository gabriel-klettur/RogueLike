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

        // Reused by every reaction lookup. An INSTANCE field rather than a static one: this
        // provider is constructed fresh per Play entry, so it needs no Domain-Reload reset
        // hook, and a static scratch buffer shared between two live providers would have one
        // overwrite the other's candidates mid-pick.
        private readonly List<string> _reactionScratch = new List<string>();

        /// <summary>
        /// How many exchanges the world may colour before it is allowed to speak once.
        ///
        /// Three, so a tired character still says three quarters of what she came to say.
        /// The face is NOT rationed with it — she looks tired on every one of those lines,
        /// because that is what being tired looks like; what is rationed is her stopping the
        /// conversation to remark on it.
        /// </summary>
        private const int WORLD_REACTION_INTERVAL = 3;

        // Counts only the exchanges the world was actually eligible to claim, so the rhythm
        // is three MOOD turns and not three turns of any kind.
        private int _worldTurns;

        /// <summary>
        /// Always a spoken reply, never a trade. This provider selects from lines a human
        /// wrote; it has no way to decide that a particular sentence meant "two borsch", and
        /// guessing from keywords would spend the player's coins on a coincidence.
        /// </summary>
        public Task<ChatReply> GenerateReplyAsync(ChatRequest request, CancellationToken cancellationToken)
        {
            DialogueIntent intent = DialogueIntentClassifier.Classify(request.PlayerText);
            ChatMoodContext mood = request.Mood;

            Chosen chosen = Compose(request.Persona, request.Memory, intent, mood);

            // An AUTHORED reaction states its own face and is never re-read. Re-classifying
            // a line that was written FOR a feeling is how the words and the drawing end up
            // disagreeing: "vaya nochecita llevo" holds no keyword and would come back
            // Neutral, delivering a line about exhaustion with a rested face.
            if (chosen.Face.HasValue)
                return Task.FromResult(ChatReply.Spoken(chosen.Text, chosen.Face.Value));

            // Otherwise the face is read off the line that was just chosen, not off the pool
            // it came from. A negotiation phrase is not always a haggling FACE — the same
            // pool holds a wink and a flat refusal — so the words are the better evidence,
            // and the player's intent only stands in when they gave nothing away.
            FacialExpression expression = ExpressionClassifier.Classify(chosen.Text, intent);

            // The world is the last word and only into a silence. Everything above had a
            // reason; Neutral here means nothing did.
            if (expression == FacialExpression.Neutral)
                expression = mood.SuggestedFace();

            return Task.FromResult(ChatReply.Spoken(chosen.Text, expression));
        }

        /// <summary>A line, and the face it was AUTHORED with — null when nobody said.</summary>
        private readonly struct Chosen
        {
            public readonly string Text;
            public readonly FacialExpression? Face;

            public Chosen(string text, FacialExpression? face) { Text = text; Face = face; }
        }

        private Chosen Compose(
            NPCPersonaDefinition persona, NPCMemory memory, DialogueIntent intent, ChatMoodContext mood)
        {
            if (persona == null) return new Chosen(EMPTY_FALLBACK, null);

            string key = PersonaKey(persona);

            // In-process first, then what this NPC is remembered to have said last. The
            // second half is what makes "don't say the same thing twice" survive a restart:
            // the cursor and the last-line cache are per-session, so without it an NPC
            // greeted again after a reload opens on the very line the player walked away on.
            if (!_lastLineByPersona.TryGetValue(key, out string previous))
                previous = LastNpcLine(memory);

            Chosen reply = SelectReaction(persona, intent, mood, previous);

            if (reply.Text == null)
            {
                string line = SelectByIntent(persona, intent, previous) ?? NextLine(persona, key);
                reply = new Chosen(line, null);
            }

            if (string.IsNullOrEmpty(reply.Text)) return new Chosen(EMPTY_FALLBACK, null);

            _lastLineByPersona[key] = reply.Text;
            return reply;
        }

        /// <summary>
        /// A line this character authored for the feeling the exchange has produced, or a
        /// null <see cref="Chosen.Text"/> to fall through to the ordinary pools.
        ///
        /// <para>The feeling comes from the player first and the world second — the same
        /// precedence the caller applies to the classifier's answer, and for the same
        /// reason. What this adds over merely SETTING a face is that the character gets to
        /// say something about it, which is the difference between a portrait that changes
        /// and a character that reacts.</para>
        ///
        /// <para>Falls through rather than substituting a neighbouring feeling's line when
        /// nothing is authored: <see cref="FacialExpressionFallback"/> exists to make a
        /// missing DRAWING degrade gracefully, and reusing it for words would put a line
        /// written about being tired in the mouth of a character who is worried.</para>
        /// </summary>
        private Chosen SelectReaction(
            NPCPersonaDefinition persona, DialogueIntent intent, ChatMoodContext mood, string previous)
        {
            // Something the player DID always earns an answer: being called a thief is not
            // ambient, and a character who ignores it twice running reads as deaf.
            FacialExpression feeling = ExpressionClassifier.FaceForIntent(intent);

            if (feeling == FacialExpression.Neutral)
            {
                // The world only gets to INTERRUPT occasionally. Measured before this gate,
                // a night-time conversation reached Neutral 0 times and Wink 0 times out of
                // 30: every exchange the player did not colour was claimed by the mood, so
                // Gatita stopped selling beetroot and just said one of two tired lines over
                // and over. The weather and the hour do not change between two sentences —
                // they would answer identically every time — so they must speak rarely and
                // then hand the conversation back.
                feeling = mood.SuggestedFace();
                if (feeling == FacialExpression.Neutral) return default;

                if (++_worldTurns % WORLD_REACTION_INTERVAL != 0) return default;
            }

            _reactionScratch.Clear();
            persona.CollectReactions(feeling, _reactionScratch);

            string line = PickRotating(_reactionScratch, PersonaKey(persona) + "/" + feeling, previous);
            return line == null ? default : new Chosen(line, feeling);
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
        private static string SelectByIntent(NPCPersonaDefinition persona, DialogueIntent intent, string previous)
        {
            var profile = persona.profile;
            if (profile == null) return null;

            switch (intent)
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
        /// The next reaction for <paramref name="cursorKey"/>, advancing that feeling's own
        /// cursor and skipping <paramref name="previous"/>.
        ///
        /// <para>Rotating rather than first-that-differs, which is what
        /// <see cref="PickDifferent"/> does for the intent pools. The difference is that a
        /// reaction pool is re-entered from the SAME state over and over — a tired character
        /// is tired all night — so "the first one that is not what I just said" resolves to
        /// the first entry every time whenever anything else spoke in between. Measured over
        /// nine night-time exchanges before this: the first tired line appeared three times
        /// and the second never, which is a line authored and drawn and then unreachable.</para>
        ///
        /// <para>One cursor per persona AND feeling, so being angry does not advance the
        /// place she had reached in being tired.</para>
        /// </summary>
        private string PickRotating(List<string> pool, string cursorKey, string previous)
        {
            if (pool == null || pool.Count == 0) return null;

            int start = _cursorByPersona.TryGetValue(cursorKey, out int cursor) ? cursor : 0;

            for (int step = 0; step < pool.Count; step++)
            {
                int index = (start + step) % pool.Count;
                string line = pool[index];
                if (string.IsNullOrWhiteSpace(line) || line == previous) continue;

                _cursorByPersona[cursorKey] = index + 1;
                return line;
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
