using System.Collections.Generic;
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
    /// All 27+ spell key bindings from Python are mapped here via TryCastByKey.
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
        private InputAction _middleClickAction;
        private InputAction _dashAction;

        // Spell key bindings — each entry maps an InputAction to a spellKey string
        private readonly List<(InputAction action, string spellKey)> _spellBindings = new List<(InputAction, string)>();

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
            _middleClickAction = new InputAction("MiddleClick", InputActionType.Button, "<Mouse>/middleButton");
            _dashAction = new InputAction("Dash", InputActionType.Button, "<Keyboard>/rightCtrl");
            _dashAction.AddBinding("<Keyboard>/rightShift");
            _dashAction.AddBinding("<Keyboard>/leftCtrl");

            // Python parity: full spell key bindings
            // Number keys
            AddSpellBinding("<Keyboard>/1", "darkball");
            AddSpellBinding("<Keyboard>/2", "iceball");
            AddSpellBinding("<Keyboard>/3", "lightball");
            AddSpellBinding("<Keyboard>/4", "puddle_lava");
            AddSpellBinding("<Keyboard>/5", "mine_basic");
            AddSpellBinding("<Keyboard>/6", "boomerang");
            AddSpellBinding("<Keyboard>/7", "chain_lightning");
            AddSpellBinding("<Keyboard>/8", "vortex_pull");
            AddSpellBinding("<Keyboard>/9", "vortex_push");
            AddSpellBinding("<Keyboard>/0", "flame_breath");

            // Letter keys
            AddSpellBinding("<Keyboard>/q", "teleport");
            AddSpellBinding("<Keyboard>/e", "slash");
            AddSpellBinding("<Keyboard>/r", "lightning");
            AddSpellBinding("<Keyboard>/t", "sphere_magic_shield");
            AddSpellBinding("<Keyboard>/f", "smoke");
            AddSpellBinding("<Keyboard>/g", "smoke_emitter");
            AddSpellBinding("<Keyboard>/c", "arcane_flame");
            AddSpellBinding("<Keyboard>/v", "firework_launch");
            AddSpellBinding("<Keyboard>/x", "healing_aura");
            AddSpellBinding("<Keyboard>/p", "meteor_shower");
            AddSpellBinding("<Keyboard>/l", "healing_totem");
            AddSpellBinding("<Keyboard>/u", "summon_barbol");
            AddSpellBinding("<Keyboard>/m", "wall_ice");

            // Enable all
            _moveAction.Enable();
            _lookAction.Enable();
            _primaryAttackAction.Enable();
            _secondaryAttackAction.Enable();
            _middleClickAction.Enable();
            _dashAction.Enable();
            foreach (var (action, _) in _spellBindings)
                action.Enable();

            Debug.Log($"[PlayerController] Input actions created: {_spellBindings.Count} spell bindings + move/look/attack/dash.");
        }

        private void AddSpellBinding(string binding, string spellKey)
        {
            var action = new InputAction($"Spell_{spellKey}", InputActionType.Button, binding);
            _spellBindings.Add((action, spellKey));
        }

        private void OnDisable()
        {
            _moveAction?.Disable();
            _lookAction?.Disable();
            _primaryAttackAction?.Disable();
            _secondaryAttackAction?.Disable();
            _middleClickAction?.Disable();
            _dashAction?.Disable();
            foreach (var (action, _) in _spellBindings)
                action?.Disable();
        }

        private void OnDestroy()
        {
            _moveAction?.Dispose();
            _lookAction?.Dispose();
            _primaryAttackAction?.Dispose();
            _secondaryAttackAction?.Dispose();
            _middleClickAction?.Dispose();
            _dashAction?.Dispose();
            foreach (var (action, _) in _spellBindings)
                action?.Dispose();
            _spellBindings.Clear();
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
            if (isDashing) return;

            // Primary attack (left click) → fireball (spell slot 0)
            // Python parity: M_LEFT → fireball
            if (_primaryAttackAction != null && _primaryAttackAction.WasPerformedThisFrame())
            {
                if (_spellCaster != null)
                    _spellCaster.TryCastByKey("fireball", _facingDirection);
            }

            // Secondary attack (right click) → slash spell
            // Python parity: M_RIGHT → slash
            if (_secondaryAttackAction != null && _secondaryAttackAction.WasPerformedThisFrame())
            {
                if (_spellCaster != null)
                    _spellCaster.TryCastByKey("slash", _facingDirection);
            }

            // Middle click → laser beam
            // Python parity: M_MIDDLE → laser_beam
            if (_middleClickAction != null && _middleClickAction.WasPerformedThisFrame())
            {
                if (_spellCaster != null)
                    _spellCaster.TryCastByKey("laser_beam", _facingDirection);
            }

            // Dash (Ctrl) → dash spell through spell system
            // Python parity: K_LCTRL / K_RCTRL → dash spell
            if (_dashAction != null && _dashAction.WasPerformedThisFrame())
            {
                if (_spellCaster != null && !_spellCaster.TryCastByKey("dash", _facingDirection))
                {
                    // Fallback: use DashAbility if spell system can't cast (cooldown, no mana, etc.)
                    if (_dashAbility != null)
                        _dashAbility.TryDash(_facingDirection);
                }
            }

            // All spell key bindings (1-0, q, e, r, t, f, g, c, v, x, p, l, u, m)
            // Python parity: full 23 spell key bindings
            if (_spellCaster != null)
            {
                foreach (var (action, spellKey) in _spellBindings)
                {
                    if (action != null && action.WasPerformedThisFrame())
                    {
                        _spellCaster.TryCastByKey(spellKey, _facingDirection);
                        break; // only one spell per frame
                    }
                }
            }
        }

        public void SetMoveSpeed(float speed)
        {
            moveSpeed = speed;
        }
    }
}
