using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// ScriptableObject defining an NPC persona for chat/dialogue.
    /// Maps to Python's data/chat/personas/{id}.json.
    /// </summary>
    [CreateAssetMenu(fileName = "NewNPCPersona", menuName = "Valkur/Chat/NPC Persona")]
    public class NPCPersonaDefinition : ScriptableObject
    {
        [Tooltip("Unique persona ID matching Python persona_id (e.g. 'vendor_cheff_gatita').")]
        public string personaId;

        [Tooltip("Display name of the NPC.")]
        public string displayName;

        [Tooltip("NPC role: vendor, generic, quest_giver.")]
        public string role = "generic";

        [Tooltip("The narrative half of this character — background, speech, boundaries, lore. " +
                 "Split into its own asset because it is long prose read only by the prompt " +
                 "builder, while everything on THIS asset is consulted whenever a conversation " +
                 "opens. Null is legal: a persona with no profile still greets, still cycles its " +
                 "dialogue lines, and still trades. It just has less to say to a language model.")]
        public PersonaProfileDefinition profile;

        [Tooltip("Last-resort face for the chat portrait, used when 'faces' has no drawing " +
                 "that the requested expression can fall back to. Optional: a persona with " +
                 "neither this nor 'faces' shows no portrait, and the panel keeps its full " +
                 "width instead of reserving an empty gutter.")]
        public Sprite portrait;

        [Tooltip("One drawing per facial expression. Filled by " +
                 "'Valkur > Chat > Import Facial Expressions', which reads the character's " +
                 "own facial/ folder; an entry set by hand is never overwritten. Not every " +
                 "expression needs art — FacialExpressionFallback decides what a missing " +
                 "one shows.")]
        public List<FacialSprite> faces = new List<FacialSprite>();

        [Tooltip("What this character looks like LISTENING — shown while the player is " +
                 "typing, one per expression. Filled by the same importer from files named " +
                 "'<anything>_listening_<expression>.png'. Optional: a character with none " +
                 "keeps its talking face while it listens, which is what every persona but " +
                 "Gatita does today.")]
        public List<FacialSprite> listeningFaces = new List<FacialSprite>();

        /// <summary>One drawing, and the expression it is the drawing OF.</summary>
        [Serializable]
        public struct FacialSprite
        {
            public FacialExpression expression;
            public Sprite sprite;
        }

        /// <summary>True when this character has any listening art at all.</summary>
        public bool HasListeningFaces => listeningFaces != null && listeningFaces.Count > 0;

        /// <summary>
        /// The listening drawing for <paramref name="wanted"/>, or the ordinary face when
        /// this character has none.
        ///
        /// <para>The fallback is to the TALKING face of the same expression, not to the
        /// listening Neutral. Listening is a second axis over the same vocabulary, so the
        /// nearest thing to "listening, amused" on a character who only drew one of them is
        /// "amused" — the emotion is the part the player is reading, and swapping it for a
        /// blank attentive stare loses exactly the information the portrait carries. A
        /// character with no listening art therefore behaves as it did before this existed:
        /// its face simply does not change while the player types.</para>
        /// </summary>
        public Sprite ResolveListeningFace(FacialExpression wanted)
        {
            if (listeningFaces != null)
            {
                foreach (FacialExpression candidate in FacialExpressionFallback.Chain(wanted))
                {
                    foreach (FacialSprite entry in listeningFaces)
                    {
                        if (entry.expression == candidate && entry.sprite != null) return entry.sprite;
                    }
                }
            }
            return ResolveFace(wanted);
        }

        /// <summary>
        /// True when this character has at least one face to show.
        ///
        /// Read by the panel to decide whether to reserve the portrait gutter at all, and by
        /// the prompt builder to decide whether asking a model for an expression is worth the
        /// tokens. A lone <see cref="portrait"/> counts: it is a face, it just never changes.
        /// </summary>
        public bool HasFaces => (faces != null && faces.Count > 0) || portrait != null;

        /// <summary>
        /// The sprite to show for <paramref name="wanted"/>, walking
        /// <see cref="FacialExpressionFallback"/> and ending on <see cref="portrait"/>.
        /// Null only when this character has no face art at all.
        /// </summary>
        public Sprite ResolveFace(FacialExpression wanted)
        {
            if (faces != null)
            {
                foreach (FacialExpression candidate in FacialExpressionFallback.Chain(wanted))
                {
                    foreach (FacialSprite entry in faces)
                    {
                        if (entry.expression == candidate && entry.sprite != null) return entry.sprite;
                    }
                }
            }
            return portrait;
        }

        /// <summary>
        /// True when <paramref name="expression"/> has a drawing of its OWN, rather than
        /// resolving through the fallback chain. The probe commands report this so an author
        /// can see which faces are really there.
        /// </summary>
        public bool HasOwnFace(FacialExpression expression)
        {
            if (faces == null) return false;
            foreach (FacialSprite entry in faces)
            {
                if (entry.expression == expression && entry.sprite != null) return true;
            }
            return false;
        }

        [Tooltip("Chat range in world units. Python default: 10.")]
        public float chatRange = 10f;

        [Tooltip("Greeting message shown on chat open. Empty = no auto-greeting.")]
        [TextArea(1, 3)]
        public string greeting;

        [Tooltip("Tone description for reply generation.")]
        [TextArea(1, 3)]
        public string tone;

        [Header("Style")]
        [Tooltip("Maximum sentences per reply.")]
        public int maxSentences = 3;

        [Tooltip("Verbosity: short, medium, long.")]
        public string verbosity = "medium";

        [Tooltip("Allow emoji in responses.")]
        public bool useEmoji = true;

        [Header("Vendor Settings (if role = vendor)")]
        [Tooltip("Allowed item types this vendor can discuss/trade.")]
        public List<string> allowedItemTypes = new List<string>();

        [Tooltip("Negotiation discount limits per item key. Key='default' for general cap.")]
        public List<DiscountEntry> discountLimits = new List<DiscountEntry>();

        [Serializable]
        public struct DiscountEntry
        {
            public string itemKey;
            [Range(0f, 0.5f)]
            public float maxDiscount;
        }

        [Header("Dialogue Lines (offline mode)")]
        [Tooltip("Pre-written dialogue lines for non-LLM mode. Cycled on interaction.")]
        [TextArea(1, 2)]
        public List<string> dialogueLines = new List<string>();

        [Header("Reactions")]
        [Tooltip("What this character says when it is FEELING something — one or more lines " +
                 "per expression. The ordinary dialogue above is what it says unprompted; " +
                 "these are what it says because the player was rude, or because it is the " +
                 "small hours and it is tired. Optional: a character with none simply keeps " +
                 "its repertoire and shows fewer faces.")]
        public List<ReactionLine> reactions = new List<ReactionLine>();

        /// <summary>
        /// One thing to say, and the face it is said WITH.
        ///
        /// <para>Keyed by <see cref="FacialExpression"/> rather than by an intent or a mood
        /// string for two reasons. The vocabulary is already closed, shared and drawn, so a
        /// reaction cannot name a feeling the portrait has no way to show. And
        /// <see cref="FacialExpression"/> lives in <c>Valkur.Data</c> while
        /// <c>DialogueIntent</c> lives in <c>Valkur.Gameplay</c> — keying on the intent
        /// would require the forbidden Data-to-Gameplay reference, the same constraint that
        /// makes <c>SpellDefinition.previewAnimState</c> a string.</para>
        ///
        /// <para>An authored reaction STATES its own face; nothing re-reads the words to
        /// guess at one. That is the whole point: the classifier is a floor for text nobody
        /// labelled, and re-classifying a line that was written for a feeling would let it
        /// disagree with the drawing it was paired with.</para>
        /// </summary>
        [Serializable]
        public struct ReactionLine
        {
            public FacialExpression expression;

            [TextArea(1, 2)]
            public string line;
        }

        /// <summary>
        /// The reactions authored for <paramref name="expression"/>, appended to
        /// <paramref name="into"/>. Never allocates when the character has none, which is
        /// the case for every persona but Gatita today.
        /// </summary>
        public void CollectReactions(FacialExpression expression, List<string> into)
        {
            if (reactions == null || into == null) return;
            foreach (ReactionLine entry in reactions)
            {
                if (entry.expression == expression && !string.IsNullOrWhiteSpace(entry.line))
                    into.Add(entry.line);
            }
        }

        /// <summary>True when this character has something to say for that feeling.</summary>
        public bool HasReaction(FacialExpression expression)
        {
            if (reactions == null) return false;
            foreach (ReactionLine entry in reactions)
            {
                if (entry.expression == expression && !string.IsNullOrWhiteSpace(entry.line))
                    return true;
            }
            return false;
        }

        public float GetDiscountLimit(string itemKey)
        {
            foreach (var entry in discountLimits)
            {
                if (entry.itemKey == itemKey) return entry.maxDiscount;
            }
            foreach (var entry in discountLimits)
            {
                if (entry.itemKey == "default") return entry.maxDiscount;
            }
            return 0.05f; // Python default
        }
    }
}
