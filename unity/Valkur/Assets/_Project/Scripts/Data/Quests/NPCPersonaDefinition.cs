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

        /// <summary>One drawing, and the expression it is the drawing OF.</summary>
        [Serializable]
        public struct FacialSprite
        {
            public FacialExpression expression;
            public Sprite sprite;
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
