using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// Carries the persona of a chat-capable entity on the entity itself.
    ///
    /// Why this exists rather than looking the persona up by name every time: the
    /// catalogue path resolves <c>NPCInteractable.NPCName</c> against a string key, so
    /// the link between an entity and its dialogue was a name spelled identically in two
    /// files. Renaming an entity — or shipping a catalogue whose key is the GameObject
    /// name while the interactable says something else — unhooked its dialogue silently,
    /// with the entity still present, still interactable and simply mute. A direct asset
    /// reference cannot drift.
    ///
    /// <see cref="ChatSystem"/> asks this component first and falls back to
    /// <see cref="ChatAssignmentCatalog"/>, which stays the answer for entities placed by
    /// hand in a scene and for the Python-parity by-name authoring the tests pin.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NPCChatIdentity : MonoBehaviour
    {
        [SerializeField, Tooltip("The persona this entity speaks with.")]
        private NPCPersonaDefinition _persona;

        /// <summary>The persona this entity speaks with. Null until configured.</summary>
        public NPCPersonaDefinition Persona => _persona;

        /// <summary>
        /// Effective chat range in world units: the persona's own, or
        /// <paramref name="fallback"/> when the persona is missing or authored 0.
        /// A persona that ships 0 means "not authored", never "cannot be talked to" —
        /// an entity that carries this component is chat-capable by construction.
        /// </summary>
        public float ResolveChatRange(float fallback) =>
            _persona != null && _persona.chatRange > 0f ? _persona.chatRange : fallback;

        /// <summary>
        /// Assigns the persona. Called by <c>EntitySetup.ConfigureChat</c> at spawn;
        /// the serialized field covers entities authored by hand in a scene.
        /// </summary>
        public void SetPersona(NPCPersonaDefinition persona) => _persona = persona;
    }
}
