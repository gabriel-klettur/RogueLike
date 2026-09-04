using UnityEngine;

namespace Valkur.Gameplay.Interaction
{
    /// <summary>
    /// Something the player can act on by pressing the interact key while standing near it:
    /// a tree to chop, a seam to mine, a villager to talk to, a chest to open.
    ///
    /// <para>WHY A REGISTRY AND A POLL, NEVER A TRIGGER. Buildings carry no
    /// <c>Rigidbody2D</c>, so a trigger would depend entirely on the player's own Dynamic
    /// body — and that body goes to sleep after half a second at rest (Player.prefab:
    /// Sleeping Mode = Start Awake, Time To Sleep = 0.5). A SLEEPING BODY STARTS NO NEW
    /// CONTACTS, so a player who stops beside a tree and then walks the last few centimetres
    /// into range would never be told about it. <c>BuildingDoor</c> and
    /// <c>ResurrectionZone</c> both poll for exactly this reason.</para>
    ///
    /// <para>The prompt is asked for every frame rather than cached, because it changes with
    /// state: the same seam reads "Mine" when it has charges and nothing at all when it is
    /// spent, and a cached string would go on inviting the player to work an empty node.</para>
    /// </summary>
    public interface IPlayerInteractable
    {
        /// <summary>Where the prompt is anchored, and what the range is measured from.</summary>
        Vector2 InteractionPosition { get; }

        /// <summary>
        /// The SURFACE the player has to reach, not the pivot. A mine entrance is several
        /// units across; measuring from its centre would put the player inside the hillside
        /// before the prompt appeared.
        /// </summary>
        Bounds InteractionBounds { get; }

        /// <summary>How far from <see cref="InteractionBounds"/> the prompt reaches.</summary>
        float InteractionRadius { get; }

        /// <summary>
        /// Whether this can be acted on right now. False hides the prompt entirely rather
        /// than showing a prompt that would do nothing — a control the player is told about
        /// and that then refuses them is worse than no control.
        /// </summary>
        bool CanInteract(GameObject player);

        /// <summary>
        /// What the floating prompt should say about this target right now — the verb, the
        /// reason it is refused, the countdown until it can be used again.
        ///
        /// <para>Resolved fresh every frame rather than cached, because every part of the
        /// answer moves with state. It is also what decides whether the target is offered at
        /// all: a result of <see cref="InteractionAvailability.Hidden"/> takes it out of the
        /// running entirely, which is how a felled tree stops competing with the live ones
        /// standing next to it.</para>
        /// </summary>
        InteractionPromptInfo DescribePrompt(GameObject player);

        /// <summary>True while this interactable is holding an ongoing session.</summary>
        bool IsInteracting { get; }

        /// <summary>Act. Called once per interact press, never per frame.</summary>
        void BeginInteraction(GameObject player);

        /// <summary>
        /// Stop an ongoing session. Raised by the controller when the player moves, walks
        /// out of range, dies, or presses the key again — never by the interactable itself,
        /// which cannot see those.
        /// </summary>
        void CancelInteraction();
    }
}
