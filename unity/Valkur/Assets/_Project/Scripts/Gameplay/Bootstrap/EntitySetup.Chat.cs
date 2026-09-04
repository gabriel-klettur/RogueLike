using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.NPC;

namespace Valkur.Gameplay
{
    public static partial class EntitySetup
    {
        /// <summary>Interaction radius used when neither the persona nor the stats author one.</summary>
        private const float DEFAULT_INTERACTION_RANGE = 2f;

        /// <summary>
        /// Makes an entity talkable, and a vendor tradeable.
        ///
        /// This is the step whose absence made the entire chat subsystem unreachable:
        /// nothing in the project added <see cref="NPCInteractable"/> to a spawned entity,
        /// so <c>ChatSystem.TryOpenChat</c> — which skips anything without one — could
        /// never find a target, and <see cref="VendorNPC"/>, which requires that same
        /// component, was never instantiated either. Both loops ran over empty ground on
        /// every frame of every session.
        ///
        /// Everything here is gated on authored data. A hostile carries no
        /// <c>chatPersona</c> and no <c>vendorConfig</c>, so it returns on the first line
        /// and pays for nothing.
        /// </summary>
        internal static void ConfigureChat(GameObject go, MonsterDefinition def)
        {
            if (go == null || def == null) return;

            // A vendor config carries its own persona reference. Preferring the
            // definition's own field keeps ONE answer to "who is this character" while
            // letting a vendor authored only through its config still speak.
            NPCPersonaDefinition persona = def.chatPersona != null
                ? def.chatPersona
                : (def.vendorConfig != null ? def.vendorConfig.persona : null);

            bool isVendor = def.vendorConfig != null;
            if (persona == null && !isVendor) return;

            var interactable = go.GetComponent<NPCInteractable>();
            if (interactable == null) interactable = go.AddComponent<NPCInteractable>();

            string displayName = persona != null && !string.IsNullOrWhiteSpace(persona.displayName)
                ? persona.displayName
                : def.displayName;
            interactable.Configure(displayName, ResolveInteractionRange(def, persona));

            if (persona != null)
            {
                var identity = go.GetComponent<NPCChatIdentity>();
                if (identity == null) identity = go.AddComponent<NPCChatIdentity>();
                identity.SetPersona(persona);
            }

            if (isVendor)
            {
                var vendor = go.GetComponent<VendorNPC>();
                if (vendor == null) vendor = go.AddComponent<VendorNPC>();
                vendor.Configure(def.vendorConfig);
            }

            // Last, so the component finds the vendor and the name already in place: it reads
            // both in Awake, which AddComponent runs synchronously. This is what puts the
            // character in the interaction registry and therefore what raises the "Conversar"
            // badge — the conversation itself already worked without it, invisibly.
            if (go.GetComponent<NPCConversationInteractable>() == null)
                go.AddComponent<NPCConversationInteractable>();

            // Registered as an NPC as well as a monster. It really is both: it spawns
            // through the monster path and carries Health, and it is also the only kind
            // of entity anything is meant to talk to. ChatSystem walks both lists and
            // de-duplicates, so the overlap costs one reference comparison.
            EntityRegistry.RegisterNPC(go);
        }

        /// <summary>
        /// How close the player must stand to interact, in world units.
        ///
        /// Resolution order is persona, then <c>EntityStats.chatRange</c>, then the
        /// default. The stats field was authored on every shipped entity (vendors carry 2,
        /// hostiles 0) and read by nothing at all — its own tooltip says so. Consulting it
        /// here is what makes that authored intent real, and it agrees with the persona by
        /// construction because both were imported from the same Python
        /// <c>assignments.json</c>.
        /// </summary>
        private static float ResolveInteractionRange(MonsterDefinition def, NPCPersonaDefinition persona)
        {
            if (persona != null && persona.chatRange > 0f) return persona.chatRange;
            if (def.stats.chatRange > 0f) return def.stats.chatRange;
            return DEFAULT_INTERACTION_RANGE;
        }
    }
}
