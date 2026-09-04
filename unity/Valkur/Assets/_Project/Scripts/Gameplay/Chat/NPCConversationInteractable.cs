using UnityEngine;
using Valkur.Gameplay.Interaction;
using Valkur.Gameplay.NPC;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// Puts a talkable character into the interaction registry, so walking up to them raises
    /// the same floating badge a tree or a seam does and the key that works there works here.
    ///
    /// <para>WHY THIS EXISTS AT ALL. <c>PlayerInteractionController</c> already opened a
    /// conversation on the interact key, but as a FALLBACK outside the registry — so an NPC
    /// never produced an <see cref="InteractionPromptInfo"/> and the player was never told the
    /// key would do anything. The control worked and was invisible, which for a player is the
    /// same as not existing: they learn "E chops trees" from the badge over a tree and have no
    /// reason to try it on a person.</para>
    ///
    /// <para>WHY IT DOES NOT RE-INTRODUCE THE DOUBLE SEARCH the controller warns about.
    /// Wrapping NPCs as interactables is dangerous only if the wrapper then delegates to
    /// <c>ChatSystem.TryOpenChat</c>, which runs its own proximity sweep over every persona:
    /// the registry would pick one NPC, the sweep would pick another, and the badge would name
    /// somebody the key does not talk to. This calls <see cref="ChatSystem.OpenChat"/> with the
    /// target already in hand, so there is exactly ONE search — the registry's — and the badge
    /// and the conversation cannot disagree by construction. The controller's fallback stays
    /// for entities placed by hand that carry no persona component and are resolved through the
    /// by-name catalogue instead.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NPCInteractable))]
    public sealed class NPCConversationInteractable : MonoBehaviour, IPlayerInteractable
    {
        /// <summary>
        /// Half-height of the footprint used when the entity has no body collider yet, in
        /// world units. Deliberately shallow: it stands in for a pair of feet, not a body.
        /// </summary>
        private const float FALLBACK_FOOT_HALF_HEIGHT = 0.2f;

        /// <summary>Half-width of that same fallback footprint.</summary>
        private const float FALLBACK_FOOT_HALF_WIDTH = 0.45f;

        private NPCInteractable _npc;
        private Health _health;
        private VendorNPC _vendor;

        private void Awake()
        {
            _npc = GetComponent<NPCInteractable>();
            _health = GetComponent<Health>();
            _vendor = GetComponent<VendorNPC>();
        }

        /// <summary>
        /// Registered as DYNAMIC, which is not a detail. The registry indexes a normal entry by
        /// the position it held when the spatial hash was last rebuilt, and it rebuilds only
        /// when membership changes — so a character who walks would be looked up at wherever
        /// they happened to be standing when some unrelated node registered, and would simply
        /// stop being found. That failure appears only above the registry's hash threshold of
        /// 24, so it works in an empty test scene and breaks in the shipped world, which
        /// carries ~88 harvest nodes before a single villager is added. Gatita paces her stall.
        /// </summary>
        private void OnEnable() => InteractableRegistry.RegisterDynamic(this);

        private void OnDisable() => InteractableRegistry.Unregister(this);

        // IPlayerInteractable ---------------------------------------------------------

        public Vector2 InteractionPosition => transform.position;

        /// <summary>
        /// The character's FOOTPRINT — the body collider the physics already uses — rather
        /// than the sprite. A villager is drawn upward from their feet (Gatita is 2.4 units
        /// tall), so measuring range against the full sprite bounds would raise the badge for
        /// a player standing on a roof two units above her head.
        /// </summary>
        public Bounds InteractionBounds
        {
            get
            {
                var body = EntityColliderConfigurator.GetBodyCollider(gameObject);
                if (body != null) return body.bounds;

                // Resolved fresh rather than cached because the collider is configured after
                // this component is added, and because the entity moves.
                return new Bounds(
                    transform.position,
                    new Vector3(FALLBACK_FOOT_HALF_WIDTH * 2f, FALLBACK_FOOT_HALF_HEIGHT * 2f, 0f));
            }
        }

        /// <summary>
        /// The range authored for this character, resolved once at spawn by
        /// <c>EntitySetup.ConfigureChat</c> from the persona, then <c>EntityStats.chatRange</c>,
        /// then a default. Read from <see cref="NPCInteractable"/> rather than resolved again
        /// here so the badge and every other reader of that range answer identically.
        /// </summary>
        public float InteractionRadius => _npc != null ? _npc.InteractionRange : 2f;

        /// <summary>
        /// A corpse has nothing to say, and a conversation cannot start on top of one that is
        /// already open. The shop counts as open too: it is reached FROM a conversation, so a
        /// press in front of it is the player trying to get back, not to start again.
        /// </summary>
        public bool CanInteract(GameObject player)
        {
            if (_health != null && _health.IsDead) return false;

            var shop = VendorShopUI.Instance;
            if (shop != null && shop.IsVisible) return false;

            var chat = ChatSystem.Instance;
            return chat != null && !chat.IsChatOpen;
        }

        /// <summary>
        /// What the badge says over this character.
        ///
        /// <para>The name is on the second line because a market has several people standing
        /// close together and the badge is the only thing that says which of them the key will
        /// reach — the registry picks by nearest surface, which is not always the one the
        /// player thinks they are facing. Vendors say so as well, since "this person trades" is
        /// the single most useful fact about them and it is otherwise only discoverable by
        /// holding a conversation first.</para>
        ///
        /// <para>Refused reads <see cref="InteractionAvailability.Hidden"/> rather than
        /// <c>Blocked</c>. Blocked exists to explain a wait the player can outlast — a seam
        /// refilling — and none of the refusals here are that: a dead villager is not coming
        /// back, and the other two mean a window is already open on screen, which explains
        /// itself better than a badge underneath it could.</para>
        /// </summary>
        public InteractionPromptInfo DescribePrompt(GameObject player)
        {
            if (!CanInteract(player)) return InteractionPromptInfo.None;

            string name = _npc != null ? _npc.NPCName : null;
            string detail = string.IsNullOrWhiteSpace(name) ? null : name;

            if (_vendor != null && !string.IsNullOrEmpty(detail))
                detail += " · comercia";

            return new InteractionPromptInfo(InteractionAvailability.Ready, "Conversar", detail);
        }

        /// <summary>
        /// Never true. A conversation is not a leashed work session: the controller cancels one
        /// of those when the player drifts 0.35 units, and chat is deliberately something you
        /// stand still for because it disables the Gameplay action map outright.
        ///
        /// <para>Answering false is what makes the controller drop its session reference on the
        /// very next frame, which is correct — from that point the conversation is owned by
        /// <see cref="ChatSystem"/> and closed by Escape or Enter.</para>
        /// </summary>
        public bool IsInteracting => false;

        public void BeginInteraction(GameObject player)
        {
            var chat = ChatSystem.Instance;
            if (chat == null || chat.IsChatOpen) return;

            // The target, not a position. TryOpenChat would sweep every persona again and could
            // land on a different character than the one whose badge the player just read.
            chat.OpenChat(gameObject);
        }

        /// <summary>
        /// Deliberately empty. The controller raises this the moment the session stops being
        /// valid, and opening a conversation is the very thing that makes it invalid — chat
        /// engages <c>InputBlocker</c>, so the controller suppresses on the next frame and tears
        /// the session down. Closing the chat here would close it one frame after it opened.
        /// </summary>
        public void CancelInteraction()
        {
        }
    }
}
