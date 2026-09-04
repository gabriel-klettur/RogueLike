using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// The narrative half of an NPC's identity: who they are, how they speak, what
    /// they refuse to talk about, what they know.
    ///
    /// Split from <see cref="NPCPersonaDefinition"/> on purpose. That asset holds what
    /// the runtime consults on a hot path — chat range, greeting, dialogue lines,
    /// discount caps — and is read every time a conversation opens. THIS asset is long
    /// prose read by exactly one caller, the prompt builder, and only when an online
    /// provider is enabled. Keeping them apart means the 90% of sessions that never
    /// touch a language model never deserialise a page of prose per NPC, and a
    /// designer tuning a chat range is not scrolling past a paragraph of lore to find it.
    ///
    /// Ported field-for-field from Python's <c>data/chat/personas/{id}.json</c>
    /// (schema at <c>tools/chat/personas/persona.schema.json</c>). The JSON is kept in
    /// the repo under <c>tools/chat/personas/</c> and re-imported by
    /// <c>Valkur &gt; Chat &gt; Import Personas</c>, so this asset is reproducible
    /// rather than hand-typed.
    /// </summary>
    [CreateAssetMenu(fileName = "NewPersonaProfile", menuName = "Valkur/Chat/Persona Profile")]
    public class PersonaProfileDefinition : ScriptableObject
    {
        [Tooltip("Persona ID this profile belongs to. Must match NPCPersonaDefinition.personaId — " +
                 "PersonaProfileTests fails if a profile and its persona disagree.")]
        public string personaId;

        [Header("Identity")]
        [Tooltip("Where this character is from. Python 'origin'.")]
        public string origin;

        [Tooltip("Who this character is and what they do. Python 'background'.")]
        [TextArea(2, 6)]
        public string background;

        [Tooltip("What this character wants. Python 'goals'.")]
        public List<string> goals = new List<string>();

        [Header("Humour")]
        public HumourBlock humour = new HumourBlock();

        [Header("Traits")]
        public TraitsBlock traits = new TraitsBlock();

        [Header("Speech")]
        public SpeechBlock speech = new SpeechBlock();

        [Header("Boundaries")]
        [Tooltip("Hard limits the character never crosses, in their own terms. Python 'boundaries'. " +
                 "These are handed to the prompt builder verbatim — they are not a content filter " +
                 "and must not be treated as one; the provider enforces its own.")]
        public List<string> boundaries = new List<string>();

        [Header("Knowledge")]
        public KnowledgeBlock knowledge = new KnowledgeBlock();

        [Header("Moods")]
        public MoodBlock moods = new MoodBlock();

        [Header("Negotiation")]
        public NegotiationBlock negotiation = new NegotiationBlock();

        [Header("Small talk")]
        public SmallTalkBlock smallTalk = new SmallTalkBlock();

        // ── Nested blocks ───────────────────────────────────────────────────
        // One class per JSON object rather than a flat field list, so a block
        // that gains a field in the Python schema gains it in one place here.

        [Serializable]
        public class HumourBlock
        {
            public bool enabled = true;

            [Tooltip("How often the character jokes: never, rarely, sometimes, often.")]
            public string frequency = "sometimes";

            public List<string> topics = new List<string>();

            [Tooltip("How the humour lands — dry, playful, gallows, …")]
            public string style;

            [Tooltip("Written examples. The offline provider draws dialogue lines from these, " +
                     "so an empty list makes a persona quieter rather than broken.")]
            [TextArea(1, 3)]
            public List<string> examples = new List<string>();
        }

        [Serializable]
        public class TraitsBlock
        {
            public List<string> positive = new List<string>();
            public List<string> negative = new List<string>();

            [Tooltip("Small concrete habits. These are what make a character read as a person " +
                     "rather than as a list of adjectives.")]
            public List<string> quirks = new List<string>();
        }

        [Serializable]
        public class SpeechBlock
        {
            [Tooltip("casual, formal, archaic, …")]
            public string register = "casual";

            [Tooltip("Words this character actually uses. Python 'slang'.")]
            public List<string> slang = new List<string>();

            [Tooltip("The only emoji this character reaches for. Ignored entirely when " +
                     "NPCPersonaDefinition.useEmoji is off.")]
            public List<string> emojiPalette = new List<string>();

            public List<string> fillerWords = new List<string>();

            [TextArea(1, 2)]
            public List<string> catchphrases = new List<string>();

            [Tooltip("How they punctuate. Python 'punctuation'.")]
            public string punctuation;

            [Tooltip("Only meaningful for a character who flirts; empty for everyone else. " +
                     "Python 'flirt_style'.")]
            public string flirtStyle;
        }

        [Serializable]
        public class KnowledgeBlock
        {
            [Tooltip("Subjects this character can speak about with authority.")]
            public List<string> domain = new List<string>();

            [Tooltip("Item types this character trades in. Mirrors " +
                     "NPCPersonaDefinition.allowedItemTypes, which is the copy the vendor " +
                     "logic reads; this one is context for the prompt.")]
            public List<string> allowedTypes = new List<string>();

            [Tooltip("What the character may claim about stock. The real catalogue is dynamic, " +
                     "so this exists to stop a generated reply promising an item that is not " +
                     "for sale.")]
            [TextArea(2, 4)]
            public string catalogPolicy;

            public List<string> tabooTopics = new List<string>();

            [Tooltip("Small truths about the world only this character would mention.")]
            [TextArea(1, 3)]
            public List<string> localLore = new List<string>();
        }

        [Serializable]
        public class MoodBlock
        {
            public bool enabled = true;

            [Tooltip("Where this character sits when nothing has happened.")]
            public string baseline = "neutral";

            [Tooltip("What lifts them. Read by the relationship layer, which moves " +
                     "NPCMemory.friendshipScore.")]
            public List<string> triggersUp = new List<string>();

            [Tooltip("What sours them.")]
            public List<string> triggersDown = new List<string>();
        }

        [Serializable]
        public class NegotiationBlock
        {
            [Tooltip("generous, hard-nosed, indifferent, …")]
            public string style;

            [Tooltip("Lines the character uses while haggling. The discount NUMBERS live on " +
                     "NPCPersonaDefinition.discountLimits, which is what the economy reads — " +
                     "these are only how the character talks about them.")]
            [TextArea(1, 2)]
            public List<string> phrases = new List<string>();
        }

        [Serializable]
        public class SmallTalkBlock
        {
            public List<string> topicsPreferred = new List<string>();
            public List<string> topicsAvoid = new List<string>();

            [Tooltip("Written examples. Like humour.examples, these seed the offline provider.")]
            [TextArea(1, 3)]
            public List<string> examples = new List<string>();
        }
    }
}
