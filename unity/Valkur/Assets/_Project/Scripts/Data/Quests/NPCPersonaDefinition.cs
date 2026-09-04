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

        [Tooltip("Face shown in the chat panel's header. Optional — the panel falls back to the " +
                 "NPC's own world sprite, and to no portrait at all if there is none.")]
        public Sprite portrait;

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
