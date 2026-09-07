using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Interaction;

namespace Valkur.Gameplay.Crafting
{
    /// <summary>
    /// A stove, a forge, a workbench: somewhere one trade's harder recipes can be made.
    ///
    /// <para>WHAT A STATION IS FOR. Crafting is reachable from the bag anywhere, because a
    /// panel the player cannot open is a system they forget exists. The station is what makes
    /// going somewhere worth it: the recipes flagged <c>requiresStation</c> — the eight
    /// six-ingredient dishes, and whatever the other trades flag — refuse to be made out of a
    /// backpack. That split gives a station a job legible without a tutorial, and it costs
    /// nothing when the player is nowhere near one, because <see cref="IsInRangeOf"/> walks a
    /// list that is normally empty.</para>
    ///
    /// <para>A STATION BELONGS TO ONE TRADE. A forge does not help you cook, and answering
    /// "any station will do" would make the four trades' stations interchangeable — which
    /// erases the only reason to build more than one. A station with NO profession is the
    /// deliberate exception: a general workbench that satisfies every trade, which is what the
    /// generic <c>crafting</c> bucket wants and what a starting camp can offer before the
    /// player has specialised.</para>
    ///
    /// <para>WHY A REGISTRY AND NOT A TRIGGER, and why it is separate from
    /// <see cref="InteractableRegistry"/>. The interactable registry answers "what would the
    /// interact key act on" — a single best target, exactly right for the badge. The panel asks
    /// a different question, "is ANY station for this trade within reach", and that has to stay
    /// true while the player stands in a kitchen with a barrel nearer to them than the stove.
    /// Routing it through the interactable registry would make the answer depend on which
    /// object happened to win the proximity sort.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CraftingStation : MonoBehaviour, IPlayerInteractable
    {
        /// <summary>
        /// How far from the station's surface the player may stand and still craft, in world
        /// units. Generous on purpose: the alternative to a forgiving radius is a player who
        /// opens the panel, finds the paella still refused, and cannot tell whether they are
        /// two steps too far away or missing an ingredient they thought they had.
        /// </summary>
        private const float DEFAULT_RANGE = 2.6f;

        /// <summary>
        /// Half-extent of the footprint used before a collider is resolved, in world units.
        /// A station is furniture, so this stands in for a hearth rather than a pair of feet.
        /// </summary>
        private const float FALLBACK_HALF_EXTENT = 0.6f;

        [SerializeField]
        [Tooltip("Which trade this station serves. LEAVE EMPTY for a general workbench that " +
                 "satisfies every trade — see the class note; that is a deliberate mode, not " +
                 "an unconfigured asset.")]
        private ProfessionDefinition profession;

        [SerializeField]
        [Tooltip("How far from this station's surface the player may craft. Left at the " +
                 "default for everything shipped; exposed because a great hall's hearth is " +
                 "not a camp pot.")]
        private float range = DEFAULT_RANGE;

        [SerializeField]
        [Tooltip("What the floating badge calls this. Empty falls back to the profession's " +
                 "own stationName, so a station placed without authoring still reads right.")]
        private string stationName = "";

        /// <summary>The trade this station serves, or null for a general workbench.</summary>
        public ProfessionDefinition Profession => profession;

        // Domain Reload is OFF, so a static collection that survives a Play-mode restart holds
        // destroyed stations and answers "in range" for a forge that no longer exists. Cleared
        // through field.Clear(), one of the two forms DomainReloadStaticResetTests recognises
        // when it reads this hook's raw IL — an Array.Clear or a helper call passes the field
        // as an ARGUMENT and counts as no reset at all.
        private static readonly List<CraftingStation> _live = new List<CraftingStation>(4);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _live.Clear();

        /// <summary>How many stations are registered. Membership, not eligibility.</summary>
        public static int LiveCount => _live.Count;

        /// <summary>
        /// Whether a station serving <paramref name="profession"/> is close enough to
        /// <paramref name="worldPosition"/> to craft at. A station with no profession serves
        /// every trade.
        ///
        /// <para>Measured against each station's BOUNDS rather than its pivot, the same
        /// correction <c>NPCConversationInteractable</c> and <c>HarvestNode</c> make: a hearth
        /// is several units across, and measuring from its centre would put the player inside
        /// the fireplace before the recipe unlocked.</para>
        ///
        /// <para>Destroyed entries are pruned; DISABLED ones are skipped and kept. Pruning
        /// disabled entries was tried elsewhere in this project and made re-enabling impossible
        /// outside Play Mode, because nothing calls <c>OnEnable</c> there and nothing puts the
        /// entry back — see the note on <c>AlliedUnit</c>.</para>
        /// </summary>
        public static bool IsInRangeOf(Vector2 worldPosition, ProfessionDefinition profession)
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var station = _live[i];
                if (station == null)
                {
                    _live.RemoveAt(i);
                    continue;
                }
                if (!station.isActiveAndEnabled) continue;
                if (!station.Serves(profession)) continue;
                if (station.IsWithinRange(worldPosition)) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a station for <paramref name="profession"/> is in reach of
        /// <paramref name="player"/>. Returns false for a null player rather than true: a
        /// missing player is not a reason to unlock a recipe, and the panel is reachable from a
        /// menu where the player reference can legitimately be absent for a frame.
        /// </summary>
        public static bool IsInRangeOfPlayer(GameObject player, ProfessionDefinition profession)
            => player != null && IsInRangeOf(player.transform.position, profession);

        /// <summary>A general workbench serves everything; a specialised one serves its own trade.</summary>
        private bool Serves(ProfessionDefinition wanted)
            => profession == null || wanted == null || profession == wanted;

        private bool IsWithinRange(Vector2 worldPosition)
        {
            var bounds = InteractionBounds;
            float sqr = ((Vector2)bounds.ClosestPoint(worldPosition) - worldPosition).sqrMagnitude;
            return sqr <= range * range;
        }

        /// <summary>
        /// Joins BOTH registries, and they are not the same thing. <see cref="_live"/> answers
        /// "may this trade's harder recipes be made here"; <see cref="InteractableRegistry"/>
        /// is what puts a badge over the stove. Missing the second is a silent failure — the
        /// station would work perfectly and nothing on screen would ever say it was there.
        ///
        /// <para>Registered STATIC rather than dynamic: a forge does not walk. The dynamic
        /// index exists for entries whose bounds move between hash rebuilds, and paying for it
        /// here would cost every query for a guarantee furniture does not need.</para>
        /// </summary>
        private void OnEnable()
        {
            _live.Add(this);
            InteractableRegistry.Register(this);
        }

        private void OnDisable()
        {
            _live.Remove(this);
            InteractableRegistry.Unregister(this);
        }

        // IPlayerInteractable ---------------------------------------------------------
        //
        // The station is ALSO an interactable, and the two roles answer different questions. As
        // a registry entry it says "this trade's harder recipes are available here"; as an
        // interactable it puts a badge over the forge so the player learns the panel exists at
        // all. Without the badge a station is scenery: nothing on screen would ever say that
        // standing here changes what can be made.

        public Vector2 InteractionPosition => transform.position;

        public Bounds InteractionBounds
        {
            get
            {
                var col = GetComponentInChildren<Collider2D>();
                if (col != null) return col.bounds;

                var sprite = GetComponentInChildren<SpriteRenderer>();
                if (sprite != null) return sprite.bounds;

                return new Bounds(transform.position,
                    new Vector3(FALLBACK_HALF_EXTENT * 2f, FALLBACK_HALF_EXTENT * 2f, 0f));
            }
        }

        public float InteractionRadius => range;

        public bool CanInteract(GameObject player) => isActiveAndEnabled;

        /// <summary>
        /// What the badge says over the station. The detail line names the TRADE rather than
        /// repeating the verb, because the panel opens from anywhere and a badge reading only
        /// "Fabricar" would make the station look like a second way to do something the player
        /// can already do.
        /// </summary>
        public InteractionPromptInfo DescribePrompt(GameObject player)
        {
            if (!CanInteract(player)) return InteractionPromptInfo.None;

            string detail = stationName;
            if (string.IsNullOrWhiteSpace(detail) && profession != null)
                detail = string.IsNullOrWhiteSpace(profession.stationName)
                    ? profession.displayName
                    : profession.stationName;

            return new InteractionPromptInfo(InteractionAvailability.Ready, "Fabricar",
                string.IsNullOrWhiteSpace(detail) ? null : detail);
        }

        /// <summary>
        /// Never true, for the reason <c>NPCConversationInteractable</c> records: a crafting
        /// panel is not a leashed work session, and answering true would have the controller
        /// cancel it the moment the player shifted a few centimetres.
        /// </summary>
        public bool IsInteracting => false;

        /// <summary>Opens the panel on this station's own trade, so the right tab is already up.</summary>
        public void BeginInteraction(GameObject player)
            => CraftingPanelUI.Instance?.OpenAt(profession);

        /// <summary>
        /// Deliberately empty. <see cref="IsInteracting"/> is false, so the controller drops
        /// its session reference on the very next frame and raises this — closing the panel
        /// here would close it one frame after it opened.
        /// </summary>
        public void CancelInteraction()
        {
        }
    }
}
