using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Combat.Death;
using Valkur.Gameplay.Spells;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Player movement, combat, and ability controller.
    /// Maps to Python's player movement + combat + spell casting systems.
    ///
    /// <para>
    /// Every input read goes through <see cref="InputService.Gameplay"/>:
    /// Move / Look / PrimaryAttack / SecondaryAttack / MiddleClick / Dash /
    /// the 23 named spells (<see cref="InputService.GameplayActions.SpellDarkball"/> …
    /// <see cref="InputService.GameplayActions.SpellWallIce"/>). Bindings live in
    /// the canonical <c>Resources/Input/ValkurInputActions.inputactions</c> asset
    /// — no ad-hoc <see cref="InputAction"/> definitions remain in this class.
    /// </para>
    /// <para>
    /// All <c>WasPerformedThisFrame</c> reads on those actions are OR'd with the
    /// legacy <see cref="UnityEngine.Input"/> backend at the call site so the
    /// player keeps responding when the new InputSystem package drops OS events
    /// (recurring Unity 2022.3 Editor bug — see <c>MouseInputManager</c> XML).
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(VisualLayerOccupant))]
    [RequireComponent(typeof(VisualLayerColliderSync))]
    [RequireComponent(typeof(VisualLayerSortingSync))]
    public partial class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 4f;

        [Header("References")]
        [SerializeField] private SpriteRenderer spriteRenderer;

        private Rigidbody2D _rb;
        private Health _health;
        private DirectionalAnimator _animator;
        private MeleeCombat _meleeCombat;
        private DashAbility _dashAbility;
        private SpellCaster _spellCaster;
        /// <summary>The ground aim marker. Told when the player ACTS along the aim — it is
        /// pushed at, and deliberately never asked anything, so it stays ignorant of spells
        /// and harvest nodes alike.</summary>
        private FacingIndicator _facingIndicator;

        /// <summary>The ring at the mouse pointer, fired by the same acts. Pushed at for the
        /// same reason and on the same seam.</summary>
        private CursorImpactFX _cursorImpact;

        /// <summary>
        /// The player did something deliberate along their aim — cast a spell, swung a pick.
        ///
        /// <para>ONE seam for every reaction to that fact, rather than each system being poked
        /// from each call site. There are two call sites today and two listeners, so a
        /// three-line method saves nothing yet; what it buys is that the THIRD reaction is one
        /// line here instead of a hunt for every place an action is raised, which is how a set
        /// of feedback effects ends up firing on different subsets of the same event.</para>
        ///
        /// <para>Both listeners are PUSHED at and neither is asked anything, so the aim marker
        /// and the cursor ring stay ignorant of what a spell or a harvest node is.</para>
        /// </summary>
        private void NotifyPlayerActed()
        {
            if (_facingIndicator != null) _facingIndicator.Pulse();
            if (_cursorImpact != null) _cursorImpact.Fire();
        }
        private StatusEffectManager _statusEffects;
        private PlayerSpiritState _spiritState;
        private Vector2 _moveInput;
        private Vector2 _facingDirection = Vector2.down;
        private Camera _mainCamera;

        // Resolved on demand from InputService.Gameplay — never cached as long-lived
        // references (avoids the zombie-after-hot-reload class of bug).
        private InputAction MoveAction            => InputService.Instance?.Gameplay?.Move;
        private InputAction LookAction            => InputService.Instance?.Gameplay?.Look;
        private InputAction PrimaryAttackAction   => InputService.Instance?.Gameplay?.PrimaryAttack;
        private InputAction SecondaryAttackAction => InputService.Instance?.Gameplay?.SecondaryAttack;
        private InputAction MiddleClickAction     => InputService.Instance?.Gameplay?.MiddleClick;
        private InputAction DashAction            => InputService.Instance?.Gameplay?.Dash;

        public Vector2 FacingDirection => _facingDirection;
        public Vector2 MoveInput => _moveInput;
        public bool IsMoving => _moveInput.sqrMagnitude > 0.01f;

        /// <summary>
        /// True iff a <see cref="PlayerSpiritState"/> component is attached AND
        /// reports IsSpirit. The lookup is lazy because EntitySetup adds the
        /// state component AFTER PlayerController.Awake has already cached
        /// references — caching only in Awake would leave _spiritState null
        /// for the lifetime of the run and silently disable spirit movement.
        /// </summary>
        public bool IsSpirit
        {
            get
            {
                if (_spiritState == null) _spiritState = GetComponent<PlayerSpiritState>();
                return _spiritState != null && _spiritState.IsSpirit;
            }
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _health = GetComponent<Health>();
            _animator = GetComponent<DirectionalAnimator>();
            _meleeCombat = GetComponent<MeleeCombat>();
            _dashAbility = GetComponent<DashAbility>();
            _spellCaster = GetComponent<SpellCaster>();
            _facingIndicator = GetComponent<FacingIndicator>();
            _cursorImpact = GetComponent<CursorImpactFX>();
            _statusEffects = GetComponent<StatusEffectManager>();
            _spiritState = GetComponent<PlayerSpiritState>();
            _mainCamera = Camera.main;

            if (spriteRenderer == null)
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();

            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            EnableGameplayMap();
        }

        private void EnableGameplayMap()
        {
            // InputService leaves the Gameplay map disabled by default —
            // pause / menu flows toggle it, and the player's existence
            // implies gameplay is active.
            var gp = InputService.Instance?.Gameplay?.Map;
            if (gp != null && !gp.enabled) gp.Enable();
        }

        /// <summary>
        /// Re-enables the canonical Gameplay map every frame. With Domain Reload
        /// off the map state can drift if a pause / menu flow disables it and a
        /// hot-recompile interleaves; touching it every Update is cheap and
        /// guarantees the player's input never silently dies mid-Play. Replaces
        /// the previous EnsureInputActionsLive zombie-revival logic — there are
        /// no ad-hoc <see cref="InputAction"/> fields left to zombify.
        /// </summary>
        private void EnsureInputActionsLive()
        {
            EnableGameplayMap();
        }

        private void OnEnable()
        {
            EnableGameplayMap();
            PlayerStance.OnChanged += OnStanceChanged;
            // Being hit cancels a spell still winding up, for the spells whose data says so.
            // OnDamagedBy rather than OnHpChanged: a heal must not interrupt a cast, and a blow
            // armour reduced to nothing still counts as being hit.
            if (_health != null) _health.OnDamagedBy += HandleCastInterruption;
        }

        // The Gameplay map itself is owned by InputService and the player creates no
        // per-instance actions, so there is nothing to dispose there -- but the stance is a
        // STATIC event and Domain Reload is off, so failing to unhook here leaves a delegate
        // pointing at a destroyed player for the rest of the session.
        private void OnDisable()
        {
            PlayerStance.OnChanged -= OnStanceChanged;
            if (_health != null) _health.OnDamagedBy -= HandleCastInterruption;
        }
    }
}
