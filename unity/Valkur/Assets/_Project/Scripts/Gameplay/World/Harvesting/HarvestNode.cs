using System;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Interaction;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Makes one placed building workable by hand: a tree to chop, a seam to mine, a bush to
    /// pick. Attached by <c>BuildingLoader</c> only to templates whose
    /// <see cref="DestructionProfile"/> says <c>harvestable</c>, so the templates that are
    /// merely scenery cost nothing — no component, no registry entry, no per-frame work.
    ///
    /// <para>WHY IT IS NOT <see cref="BuildingDurability"/>. Durability answers "can this be
    /// broken by a blow", from any source. Harvesting answers "what does working it PAY and
    /// TEACH", and the prompt that invites the player to do it. The two share the resistance
    /// matrix, the tool gate and the skill efficiency through <see cref="HarvestBlowResolver"/>,
    /// and a Destroy-mode node hears every blow through <see cref="BuildingDurability.Worked"/> —
    /// so a tree pays the same whether it was felled with the interact key, a sword or a slash.</para>
    ///
    /// <para>The two <see cref="HarvestMode"/>s are genuinely different verbs and the split is
    /// load-bearing. <c>Destroy</c> hands each blow to <see cref="BuildingDurability"/> and
    /// lets the existing death sequence do the rest. <c>Deplete</c> never touches durability
    /// at all: it consumes charges, marks the node spent, and refills it later.</para>
    ///
    /// Partials: <c>.Prompt</c> (the badge), <c>.Spent</c> (running out and coming back),
    /// <c>.Session</c> (the interact clock), <c>.Swing</c> (Deplete swings and charge work),
    /// <c>.Yield</c> (what work pays and teaches).
    /// </summary>
    [DisallowMultipleComponent]
    public partial class HarvestNode : MonoBehaviour, IPlayerInteractable, IWorkProgress
    {
        /// <summary>How long after a blow the node still counts as being worked, for the bar.</summary>
        private const float WORKING_LINGER_SECONDS = 0.9f;

        private DestructionProfile _profile;
        private BuildingObject _building;
        private BuildingDurability _durability;

        private int _chargesRemaining;
        private bool _spent;

        /// <summary>
        /// WALL-CLOCK deadline at which a spent node refills. 0 = never. Wall clock, not
        /// <c>Time.time</c>: session time restarts at zero on every load, so a deadline in it
        /// either fires instantly or never. Same clock <see cref="BuildingDurability"/> uses.
        /// </summary>
        private double _regrowAtUnix;

        private bool _registered;
        private float _lastWorkedAt = -999f;

        /// <summary>
        /// A blow landed on this node. Carries what the blow amounted to and how many stacks
        /// it produced, so a feedback layer can tell a productive swing from a bounced one
        /// without re-deriving either.
        /// </summary>
        public event Action<HarvestBlow, int> BlowLanded;

        /// <summary>The node ran out. Raised once, before the spent look is applied.</summary>
        public event Action Depleted;

        /// <summary>A spent node refilled and is workable again.</summary>
        public event Action Regrown;

        public DestructionProfile Profile => _profile;
        public BuildingObject Building => _building;
        public HarvestMode Mode => _profile != null ? _profile.harvestMode : HarvestMode.Destroy;
        public int ChargesRemaining => _chargesRemaining;
        public bool IsSpent => _spent;

        /// <summary>Whoever last worked this node, by any route. What feedback aims its chips from.</summary>
        public GameObject LastWorker { get; private set; }

        /// <summary>
        /// How much of the node is left, 1 down to 0. Deplete counts charges; Destroy reads
        /// the durability it does not itself own, so one bar can label either.
        /// </summary>
        public float RemainingFraction
        {
            get
            {
                if (_profile == null) return 0f;
                if (_profile.harvestMode == HarvestMode.Deplete)
                    return Mathf.Clamp01((float)_chargesRemaining / Mathf.Max(1, _profile.charges));

                return _durability != null ? _durability.DurabilityFraction : 0f;
            }
        }

        // IWorkProgress ---------------------------------------------------------------

        /// <summary>Work FILLS toward the tree coming down — see <c>HarvestNode.Progress.cs</c>.</summary>
        public float Progress01 => WorkDone01;

        public Vector2 ProgressAnchor
        {
            get
            {
                var b = WorkableBounds;
                return new Vector2(b.center.x, b.min.y);
            }
        }

        /// <summary>
        /// A session, OR a blow within the last moment. The second half is what gives a tree
        /// chopped with the attack button the same bar as one chopped with the interact key.
        /// </summary>
        public bool IsWorking => _sessionActive || Time.time - _lastWorkedAt < WORKING_LINGER_SECONDS;

        /// <summary>
        /// Wire up and enter the registry. Called explicitly by the loader rather than from
        /// <c>Awake</c>: registering before the profile is known would expose an interactable
        /// whose range, prompt and mode all resolve against nothing.
        /// </summary>
        public void Initialize(DestructionProfile profile, BuildingObject building,
            BuildingDurability durability)
        {
            _profile = profile;
            _building = building;
            _durability = durability;
            _chargesRemaining = profile != null ? Mathf.Max(1, profile.charges) : 0;

            if (_profile == null || !_profile.harvestable) return;

            if (_durability != null)
            {
                _durability.Destroyed += OnBuildingDestroyed;
                _durability.Regrown += OnBuildingRegrown;
                _durability.Worked += OnDurabilityWorked;
            }

            // An idle node has nothing to tick: enabled only while a session runs or a regrow
            // is pending (see RefreshTicking).
            enabled = false;

            // A Deplete seam also joins the SWING registry; a Destroy node is already reached
            // through its own BuildingDurability, and a second path would work it twice.
            RefreshSwingRegistration();

            if (_registered) return;
            InteractableRegistry.Register(this);
            _registered = true;
        }

        /// <summary>Tick only while there is something to tick.</summary>
        private void RefreshTicking()
        {
            enabled = _sessionActive || (_spent && _regrowAtUnix > 0d);
        }

        private void OnDestroy()
        {
            if (_durability != null)
            {
                _durability.Destroyed -= OnBuildingDestroyed;
                _durability.Regrown -= OnBuildingRegrown;
                _durability.Worked -= OnDurabilityWorked;
            }

            if (_swingRegistered)
            {
                HarvestSwingRegistry.Unregister(this);
                _swingRegistered = false;
            }

            if (!_registered) return;
            InteractableRegistry.Unregister(this);
            _registered = false;
        }

        private void Update()
        {
            TickSession();
            TickRegrow();
        }

        private void NoteWorked(GameObject worker)
        {
            _lastWorkedAt = Time.time;
            if (worker != null) LastWorker = worker;
        }

        private void RaiseBlowLanded(HarvestBlow blow, int yields)
        {
            EnsurePresentation();
            BlowLanded?.Invoke(blow, yields);
        }

        // IPlayerInteractable ---------------------------------------------------------

        public Vector2 InteractionPosition => WorkableBounds.center;

        /// <summary>
        /// The FOOTPRINT, not the whole sprite: a tree canopy is drawn several units above the
        /// ground, so measuring range from it would offer the prompt to someone nowhere near
        /// the trunk.
        /// </summary>
        public Bounds InteractionBounds => WorkableBounds;

        public float InteractionRadius => _profile != null ? _profile.interactionRadius : 1.6f;

        /// <summary>
        /// A spent or destroyed node offers nothing; the spent LOOK says to come back later.
        /// </summary>
        public bool CanInteract(GameObject player)
        {
            if (_profile == null || !_profile.harvestable) return false;
            if (_spent) return false;

            if (_profile.harvestMode == HarvestMode.Deplete) return _chargesRemaining > 0;
            return _durability != null && _durability.AcceptsDamage;
        }

        /// <summary>
        /// Build the feedback and the bar the first time this node is actually worked, by any
        /// route. Lazily, because the great majority of nodes are never touched in a run.
        /// </summary>
        private void EnsurePresentation()
        {
            HarvestFeedback.Attach(this);
            HarvestNodeBar.Attach(this);
        }

        private Bounds WorkableBounds
        {
            get
            {
                var footprint = _building != null ? _building.FootprintRenderer : null;
                if (footprint != null && footprint.sprite != null) return footprint.bounds;

                // No renderer yet (the first frame of a programmatic spawn). A zero-size box
                // at the pivot still passes the range test from touching distance.
                return new Bounds(transform.position, Vector3.zero);
            }
        }
    }
}
