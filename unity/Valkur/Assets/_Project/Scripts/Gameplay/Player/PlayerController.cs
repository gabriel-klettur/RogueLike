using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Player movement, combat, and ability controller.
    /// Maps to Python's player movement + combat + spell casting systems.
    /// Uses standalone InputAction objects to avoid InputSystem 1.7.0 composite resolver bugs.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Health))]
    public class PlayerController : MonoBehaviour
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
        private StatusEffectManager _statusEffects;
        private Vector2 _moveInput;
        private Vector2 _facingDirection = Vector2.down;
        private Camera _mainCamera;

        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _primaryAttackAction;
        private InputAction _secondaryAttackAction;
        private InputAction _dashAction;
        private InputAction _spell1Action;
        private InputAction _spell2Action;
        private InputAction _spell3Action;
        private InputAction _spell4Action;

        public Vector2 FacingDirection => _facingDirection;
        public Vector2 MoveInput => _moveInput;
        public bool IsMoving => _moveInput.sqrMagnitude > 0.01f;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _health = GetComponent<Health>();
            _animator = GetComponent<DirectionalAnimator>();
            _meleeCombat = GetComponent<MeleeCombat>();
            _dashAbility = GetComponent<DashAbility>();
            _spellCaster = GetComponent<SpellCaster>();
            _statusEffects = GetComponent<StatusEffectManager>();
            _mainCamera = Camera.main;

            if (spriteRenderer == null)
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();

            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            CreateInputActions();
        }

        private void CreateInputActions()
        {
            _moveAction = new InputAction("Move", InputActionType.Value);
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");

            _lookAction = new InputAction("Look", InputActionType.Value, "<Mouse>/position");
            _primaryAttackAction = new InputAction("PrimaryAttack", InputActionType.Button, "<Mouse>/leftButton");
            _secondaryAttackAction = new InputAction("SecondaryAttack", InputActionType.Button, "<Mouse>/rightButton");
            _dashAction = new InputAction("Dash", InputActionType.Button, "<Keyboard>/rightCtrl");
            _dashAction.AddBinding("<Keyboard>/rightShift");
            _spell1Action = new InputAction("Spell1", InputActionType.Button, "<Keyboard>/1");
            _spell2Action = new InputAction("Spell2", InputActionType.Button, "<Keyboard>/2");
            _spell3Action = new InputAction("Spell3", InputActionType.Button, "<Keyboard>/3");
            _spell4Action = new InputAction("Spell4", InputActionType.Button, "<Keyboard>/4");
            _moveAction.Enable();
            _lookAction.Enable();
            _primaryAttackAction.Enable();
            _secondaryAttackAction.Enable();
            _dashAction.Enable();
            _spell1Action.Enable();
            _spell2Action.Enable();
            _spell3Action.Enable();
            _spell4Action.Enable();

            Debug.Log("[PlayerController] Input actions created and enabled (WASD+Arrows move, LClick=fireball, RClick=slash, RCtrl=dash, 1-4=spells).");
        }

        private void OnDisable()
        {
            _moveAction?.Disable();
            _lookAction?.Disable();
            _primaryAttackAction?.Disable();
            _secondaryAttackAction?.Disable();
            _dashAction?.Disable();
            _spell1Action?.Disable();
            _spell2Action?.Disable();
            _spell3Action?.Disable();
            _spell4Action?.Disable();
        }

        private void OnDestroy()
        {
            _moveAction?.Dispose();
            _lookAction?.Dispose();
            _primaryAttackAction?.Dispose();
            _secondaryAttackAction?.Dispose();
            _dashAction?.Dispose();
            _spell1Action?.Dispose();
            _spell2Action?.Dispose();
            _spell3Action?.Dispose();
            _spell4Action?.Dispose();
        }

        private void Update()
        {
            if (_health.IsDead) return;
            if (_statusEffects != null && _statusEffects.IsStunned) return;

            ReadInput();
            UpdateFacingDirection();
            PollCombatActions();
        }

        private void FixedUpdate()
        {
            if (_health.IsDead)
            {
                _rb.velocity = Vector2.zero;
                return;
            }

            // Stun zeroes velocity (StunEffect.Tick also handles this, double-safe)
            if (_statusEffects != null && _statusEffects.IsStunned)
            {
                _rb.velocity = Vector2.zero;
                return;
            }

            // Dash overrides normal movement
            if (_dashAbility != null && _dashAbility.IsDashing)
                return;

            _rb.velocity = _moveInput * moveSpeed;
        }

        private void ReadInput()
        {
            if (_moveAction != null)
                _moveInput = _moveAction.ReadValue<Vector2>();
        }

        private void UpdateFacingDirection()
        {
            if (_mainCamera == null || !_mainCamera.isActiveAndEnabled)
                _mainCamera = Camera.main;

            if (_lookAction != null && _mainCamera != null)
            {
                Vector2 mouseScreen = _lookAction.ReadValue<Vector2>();
                Vector3 mouseWorld = _mainCamera.ScreenToWorldPoint(mouseScreen);
                Vector2 dir = ((Vector2)mouseWorld - (Vector2)transform.position).normalized;
                if (dir.sqrMagnitude > 0.01f)
                    _facingDirection = dir;
            }
            else if (IsMoving)
            {
                _facingDirection = _moveInput.normalized;
            }

            if (spriteRenderer != null && _animator == null)
                spriteRenderer.flipX = _facingDirection.x < 0;

            if (_animator != null)
            {
                var dir = _animator.ResolveDirectionFromVector(_facingDirection);
                var state = IsMoving ? DirectionalAnimator.AnimState.Walk : DirectionalAnimator.AnimState.Idle;
                _animator.SetState(state, dir);
            }
        }

        private void PollCombatActions()
        {
            bool isDashing = _dashAbility != null && _dashAbility.IsDashing;

            // Primary attack (left click) — fireball (spell slot 0)
            if (_primaryAttackAction != null && _primaryAttackAction.WasPerformedThisFrame())
            {
                if (!isDashing && _spellCaster != null)
                    _spellCaster.TryCast(0, _facingDirection);
            }

            // Secondary attack (right click) — melee slash
            if (_secondaryAttackAction != null && _secondaryAttackAction.WasPerformedThisFrame())
            {
                if (!isDashing && _meleeCombat != null)
                    _meleeCombat.TryAttack(_facingDirection);
            }

            // Dash (right ctrl) — dash toward mouse facing direction
            if (_dashAction != null && _dashAction.WasPerformedThisFrame())
            {
                if (_dashAbility != null)
                    _dashAbility.TryDash(_facingDirection);
            }

            // Spell slots 1-4
            if (!isDashing && _spellCaster != null)
            {
                if (_spell1Action != null && _spell1Action.WasPerformedThisFrame())
                    _spellCaster.TryCast(0, _facingDirection);
                if (_spell2Action != null && _spell2Action.WasPerformedThisFrame())
                    _spellCaster.TryCast(1, _facingDirection);
                if (_spell3Action != null && _spell3Action.WasPerformedThisFrame())
                    _spellCaster.TryCast(2, _facingDirection);
                if (_spell4Action != null && _spell4Action.WasPerformedThisFrame())
                    _spellCaster.TryCast(3, _facingDirection);
            }
        }

        public void SetMoveSpeed(float speed)
        {
            moveSpeed = speed;
        }
    }
}
