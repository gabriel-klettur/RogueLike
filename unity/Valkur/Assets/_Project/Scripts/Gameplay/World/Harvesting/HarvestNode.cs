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
    /// broken by a blow", and its blows arrive from combat through
    /// <c>DestructibleObstacleRegistry</c>. Harvesting answers "can the player stand here and
    /// work this", and its blows arrive from a session on a clock. The two share the
    /// resistance matrix and the tool gate — through <see cref="HarvestBlowResolver"/>, which
    /// is the single owner of that question — and nothing else. Folding them together would
    /// mean either a mine that a stray fireball can delete or a tree that cannot be chopped
    /// down, depending on which side won.</para>
    ///
    /// <para>The two <see cref="HarvestMode"/>s are genuinely different verbs and the split is
    /// load-bearing. <c>Destroy</c> hands each blow to <see cref="BuildingDurability"/> and
    /// lets the existing death sequence do the rest. <c>Deplete</c> never touches durability
    /// at all: it consumes charges, marks the node spent, and refills it later. A mine
    /// expressed as a building whose durability reached zero would be correct in code and
    /// read, on screen, as the player deleting a hillside.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public partial class HarvestNode : MonoBehaviour, IPlayerInteractable, IWorkProgress
    {
        private DestructionProfile _profile;
        private BuildingObject _building;
        private BuildingDurability _durability;

        private int _chargesRemaining;
        private bool _spent;

        /// <summary>
        /// WALL-CLOCK deadline at which a spent node refills. 0 = never.
        ///
        /// <para>Wall clock, not <c>Time.time</c>, and the distinction is not cosmetic:
        /// session time restarts at zero on every load, so a deadline expressed in it either
        /// fires the instant the world comes back or never fires at all, depending only on
        /// which side of the comparison the stale number landed on. It is the same clock
        /// <see cref="BuildingDurability"/> uses for a felled building, so the save layer
        /// stores one kind of deadline rather than two.</para>
        /// </summary>
        private double _regrowAtUnix;

        private bool _registered;

        private SpriteRenderer[] _tintedRenderers = Array.Empty<SpriteRenderer>();
        private Color[] _pristineColors = Array.Empty<Color>();

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

        /// <summary>
        /// How much of the node is left, 1 down to 0. Deplete counts charges; Destroy reads
        /// the durability it does not itself own, so one bar can label either without the
        /// caller knowing which mode it is looking at.
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
        // What the shared world-space bar reads. Deliberately thin: the bar knows how full it
        // is and where to sit, and nothing else about a harvest node.

        /// <summary>A seam DRAINS, so the bar shows what is left rather than what is done.</summary>
        public float Progress01 => RemainingFraction;

        public Vector2 ProgressAnchor
        {
            get
            {
                var b = WorkableBounds;
                return new Vector2(b.center.x, b.max.y);
            }
        }

        public bool IsWorking => IsInteracting;

        /// <summary>
        /// Wire up and enter the registry. Called explicitly by the loader rather than from
        /// <c>Awake</c> for the same reason <see cref="BuildingDurability.Initialize"/> is:
        /// registering before the profile is known would expose an interactable whose range,
        /// prompt and mode all resolve against nothing, and the very next frame would ask.
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
            }

            // An idle node has nothing to tick. A populated forest is hundreds of these, and
            // Unity pays a managed-to-native call for every Update on every component whether
            // or not the body does anything: 88 of them on the shipped world, sixty times a
            // second, to run two early-returns. The component switches itself on only while a
            // session is running or a regrow is pending, and back off at the end of both.
            enabled = false;

            // A Deplete seam also joins the SWING registry, so holding the attack button works
            // it the same way it already chops a tree. A Destroy node does not: it is already
            // reached through its own BuildingDurability, and a second path to the same
            // building would work it twice per swing.
            RefreshSwingRegistration();

            if (_registered) return;
            InteractableRegistry.Register(this);
            _registered = true;
        }

        /// <summary>
        /// Tick only while there is something to tick. Called from every place that can change
        /// the answer, so the flag can never be left on for a node doing nothing — or, worse,
        /// off for one with a regrow pending, which would strand a spent seam forever.
        /// </summary>
        private void RefreshTicking()
        {
            enabled = _sessionActive || (_spent && _regrowAtUnix > 0d);
        }

        /// <summary>
        /// When this node refills, as a Unix timestamp. 0 while it still has charges, or when
        /// its profile names no regrow. What the save layer records so a spent seam comes back
        /// on the schedule it was actually spent on.
        /// </summary>
        public double RegrowAtUnix => _regrowAtUnix;

        /// <summary>
        /// Restore a partially worked node, for the save layer. Clamped rather than trusted:
        /// a profile whose charge count was rebalanced downward since the run was saved would
        /// otherwise leave a node holding more than a full one.
        /// </summary>
        public void RestoreCharges(int value)
        {
            if (_profile == null) return;

            _chargesRemaining = Mathf.Clamp(value, 0, Mathf.Max(1, _profile.charges));
            if (_chargesRemaining <= 0 && !_spent) EnterSpentState();
        }

        /// <summary>
        /// Put a spent node back exactly as a previous session left it, INCLUDING when it is
        /// due to refill.
        ///
        /// <para>This exists because <see cref="RestoreCharges"/> alone cannot express it.
        /// Restoring zero charges necessarily enters the spent state, and entering it computes
        /// a fresh deadline — so a seam emptied five minutes before the player quit would come
        /// back with its full timer running again, and would do it once more on every load. A
        /// deadline that has ALREADY passed is the normal case for a long absence and is
        /// handled by letting <see cref="TickRegrow"/> bring the node back through the same
        /// path a live regrow uses, rather than a second restore path that would drift.</para>
        /// </summary>
        public void RestoreSpent(int charges, double regrowAtUnix)
        {
            RestoreCharges(charges);
            if (_spent) _regrowAtUnix = regrowAtUnix;
            RefreshTicking();
        }

        private void OnDestroy()
        {
            if (_durability != null)
            {
                _durability.Destroyed -= OnBuildingDestroyed;
                _durability.Regrown -= OnBuildingRegrown;
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

        // IPlayerInteractable ---------------------------------------------------------

        public Vector2 InteractionPosition => WorkableBounds.center;

        /// <summary>
        /// The FOOTPRINT, not the whole sprite, for the reason <see cref="BuildingDurability"/>
        /// records: a tree canopy is drawn several units above the ground and sorted over the
        /// player, so measuring range from it would offer the prompt to someone standing
        /// nowhere near the trunk.
        /// </summary>
        public Bounds InteractionBounds => WorkableBounds;

        public float InteractionRadius => _profile != null ? _profile.interactionRadius : 1.6f;

        /// <summary>
        /// A spent or destroyed node offers nothing. Hiding the prompt is deliberate: a
        /// control the player is told about and that then refuses them is worse than no
        /// control, and the spent LOOK is what says to come back later.
        /// </summary>
        public bool CanInteract(GameObject player)
        {
            if (_profile == null || !_profile.harvestable) return false;
            if (_spent) return false;

            if (_profile.harvestMode == HarvestMode.Deplete) return _chargesRemaining > 0;
            return _durability != null && _durability.AcceptsDamage;
        }

        /// <summary>
        /// What the badge over this node should say right now.
        ///
        /// <para>Answering three questions rather than one is the whole difference between a
        /// prompt and a label: what the key does, why it is refused, and when to come back. A
        /// spent seam that simply showed nothing would be indistinguishable from a decorative
        /// rock, so the player either concludes the feature is broken or keeps walking into it
        /// hoping — which is exactly what a worked-out mine used to do.</para>
        ///
        /// <para>The wrong-tool line is the other half of that. Bare-handed chopping is not
        /// refused — the floor in <see cref="HarvestBlowResolver.Scale"/> keeps it at one
        /// damage a blow — so a tree takes forty swings instead of four. Forty swings with no
        /// explanation reads as broken; the same forty swings with "muy lento sin herramienta"
        /// over them reads as a game telling you to go and find an axe.</para>
        /// </summary>
        public InteractionPromptInfo DescribePrompt(GameObject player)
        {
            if (_profile == null || !_profile.harvestable) return InteractionPromptInfo.None;

            string verb = string.IsNullOrEmpty(_profile.harvestVerb)
                ? "Recolectar"
                : _profile.harvestVerb;

            if (_sessionActive)
                return new InteractionPromptInfo(
                    InteractionAvailability.Busy, "Detener", RemainingDetail());

            if (_profile.harvestMode == HarvestMode.Deplete)
            {
                if (!_spent)
                    return Available(player, verb);

                // A node that will never refill has nothing to promise, so it says so once
                // rather than showing a countdown that never moves.
                if (_regrowAtUnix <= 0d)
                    return new InteractionPromptInfo(
                        InteractionAvailability.Blocked, "Agotada", "No volverá a llenarse");

                double secondsLeft = _regrowAtUnix - WorldDamageService.UnixNow();
                return new InteractionPromptInfo(
                    InteractionAvailability.Blocked, "Agotada",
                    secondsLeft > 0d
                        ? "Vuelve en " + FormatCountdown(secondsLeft)
                        : "Reponiéndose…");
            }

            // Destroy mode: once the building is gone there is nothing left to work. It has
            // already left the registry, and this keeps the two answers agreeing.
            if (_spent || _durability == null || !_durability.AcceptsDamage)
                return InteractionPromptInfo.None;

            return Available(player, verb);
        }

        /// <summary>
        /// A node the player can work, with a warning when their tools make it a bad idea.
        /// The blow is resolved through the SAME entry point a real swing uses, so the badge
        /// can never promise something the next blow disagrees with.
        /// </summary>
        private InteractionPromptInfo Available(GameObject player, string verb)
        {
            var blow = HarvestBlowResolver.Resolve(_profile, player, element: null);

            if (blow.Immune)
                return new InteractionPromptInfo(
                    InteractionAvailability.Blocked, verb, ToolHint(_profile.material));

            if (blow.WrongTool)
                return new InteractionPromptInfo(
                    InteractionAvailability.Ready, verb,
                    ToolHint(_profile.material) + " — así es muy lento");

            return new InteractionPromptInfo(InteractionAvailability.Ready, verb);
        }

        /// <summary>
        /// What to go and fetch, named after the MATERIAL rather than after any particular
        /// item.
        ///
        /// <para>"Tus herramientas no sirven" tells the player they have failed; "Necesitas un
        /// pico" tells them what to do about it, which is the only version worth the pixels.
        /// It is keyed off the material because that is what the resistance matrix is keyed
        /// off — naming a specific item here would go stale the first time the catalogue grows
        /// a second pick, and the node has never seen the item catalogue anyway.</para>
        /// </summary>
        private static string ToolHint(MaterialClass material)
        {
            switch (material)
            {
                case MaterialClass.Stone:   return "Necesitas un pico";
                case MaterialClass.Wood:    return "Necesitas un hacha";
                case MaterialClass.Metal:   return "Necesitas algo contundente";
                case MaterialClass.Foliage: return "Necesitas algo afilado";
                default:                    return "Necesitas otra herramienta";
            }
        }

        /// <summary>How much is left, phrased for whichever mode this node is in.</summary>
        private string RemainingDetail()
        {
            if (_profile == null) return string.Empty;

            if (_profile.harvestMode == HarvestMode.Deplete)
                return _chargesRemaining == 1
                    ? "Queda 1 carga"
                    : $"Quedan {_chargesRemaining} cargas";

            return $"{Mathf.CeilToInt(RemainingFraction * 100f)}%";
        }

        /// <summary>
        /// <c>m:ss</c> above a minute, plain seconds below it. A regrow is minutes long, and
        /// "148 s" is a number the player has to convert before it means anything.
        /// </summary>
        private static string FormatCountdown(double seconds)
        {
            int total = Mathf.Max(1, Mathf.CeilToInt((float)seconds));
            if (total < 60) return total + " s";
            return (total / 60) + ":" + (total % 60).ToString("00");
        }

        // Spent and regrow -------------------------------------------------------------

        /// <summary>
        /// Mark the node worked out. In Deplete mode the building SURVIVES — it is tinted so
        /// it reads as exhausted and left standing, which is the whole difference between a
        /// seam and a crate.
        /// </summary>
        private void EnterSpentState()
        {
            if (_spent) return;
            _spent = true;
            _chargesRemaining = 0;

            Depleted?.Invoke();

            if (_profile != null && _profile.harvestMode == HarvestMode.Deplete)
                ApplySpentTint(true);

            _regrowAtUnix = _profile != null && _profile.regrowSeconds > 0f
                ? WorldDamageService.UnixNow() + _profile.regrowSeconds
                : 0d;

            RefreshTicking();
        }

        /// <summary>
        /// Refill a spent node once its deadline passes.
        ///
        /// <para>The deadline is CLEARED before the state flips, not after. A listener on
        /// <see cref="Regrown"/> can write straight back into this node — the save layer does,
        /// recording the new charge count — and one that re-entered a still-spent node would
        /// arm a second deadline on top of the one that had just fired. Measured live before
        /// the clear was moved: a mine regrew and was immediately spent again with its full
        /// timer restarted, over and over, so the seam could never actually come back.</para>
        /// </summary>
        private void TickRegrow()
        {
            if (!_spent) return;
            if (_profile == null || _profile.harvestMode != HarvestMode.Deplete) return;
            if (_regrowAtUnix <= 0d) return;
            if (WorldDamageService.UnixNow() < _regrowAtUnix) return;

            _regrowAtUnix = 0d;
            _spent = false;
            _chargesRemaining = Mathf.Max(1, _profile.charges);
            ApplySpentTint(false);
            RefreshTicking();
            Regrown?.Invoke();
        }

        /// <summary>
        /// A Destroy-mode node is gone the moment its durability runs out, so it stops
        /// offering itself: the stump it leaves is not a thing to chop.
        ///
        /// <para>It LEAVES the registry rather than merely answering false to
        /// <see cref="CanInteract"/>, because the registry is walked to find the nearest
        /// target every frame the player is alive, and a felled forest that stayed in it
        /// would be paid for forever to produce nothing.</para>
        /// </summary>
        private void OnBuildingDestroyed(Vector2 contactPoint, DamageClass damageClass)
        {
            CancelInteraction();
            if (_spent) return;

            _spent = true;
            _chargesRemaining = 0;
            RefreshTicking();
            Depleted?.Invoke();

            if (!_registered) return;
            InteractableRegistry.Unregister(this);
            _registered = false;
        }

        /// <summary>
        /// The felled building came back, so the node is workable again and has to RE-ENTER
        /// the registry it left.
        ///
        /// <para>Owning the regrow itself is deliberately <see cref="BuildingDurability"/>'s
        /// job, not this component's: restoring a felled building means re-running the
        /// loader's assembly pass, because <c>ApplyRemainsSprite</c> overwrote the split ratio
        /// and the transform scale it was built with. This side only has to answer the
        /// question the registry asks.</para>
        /// </summary>
        private void OnBuildingRegrown()
        {
            _spent = false;
            _regrowAtUnix = 0d;
            _chargesRemaining = _profile != null ? Mathf.Max(1, _profile.charges) : 0;
            RefreshTicking();

            if (_registered || _profile == null || !_profile.harvestable) return;
            InteractableRegistry.Register(this);
            _registered = true;

            Regrown?.Invoke();
        }

        /// <summary>
        /// Multiply the spent colour over the node, remembering what was there first.
        ///
        /// <para>Buildings have no <c>SpriteTintStack</c> — that lives on entity roots and is
        /// the single owner of an ENTITY body colour. A building renderer has no competing
        /// writer, so tinting it directly is safe; capturing the pristine colour once and
        /// restoring it is what keeps it that way. Capturing on every call instead would
        /// record the SPENT colour as the baseline the second time round and stain the node
        /// permanently, which is precisely the bug the tint stack exists to prevent.</para>
        /// </summary>
        private void ApplySpentTint(bool spent)
        {
            if (_tintedRenderers.Length == 0) CachePristineColors();

            for (int i = 0; i < _tintedRenderers.Length; i++)
            {
                var sr = _tintedRenderers[i];
                if (sr == null) continue;

                sr.color = spent && _profile != null
                    ? _pristineColors[i] * _profile.spentTint
                    : _pristineColors[i];
            }
        }

        private void CachePristineColors()
        {
            _tintedRenderers = GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            _pristineColors = new Color[_tintedRenderers.Length];

            for (int i = 0; i < _tintedRenderers.Length; i++)
                _pristineColors[i] = _tintedRenderers[i] != null
                    ? _tintedRenderers[i].color
                    : Color.white;
        }

        private Bounds WorkableBounds
        {
            get
            {
                var footprint = _building != null ? _building.FootprintRenderer : null;
                if (footprint != null && footprint.sprite != null) return footprint.bounds;

                // No renderer yet (the first frame of a programmatic spawn). A zero-size box
                // at the pivot still passes the range test from touching distance, which is
                // better than a node that can never be reached.
                return new Bounds(transform.position, Vector3.zero);
            }
        }
    }
}
