using UnityEngine;
using UnityEngine.InputSystem;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Player movement and facing direction controller.
    /// Maps to Python's player movement system with 8-directional support.
    /// Uses the new Input System with ValkurInputActions.
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
        private Vector2 _moveInput;
        private Vector2 _facingDirection = Vector2.down;
        private Camera _mainCamera;

        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _primaryAttackAction;
        private InputAction _dashAction;
        private PlayerInput _playerInput;

        public Vector2 FacingDirection => _facingDirection;
        public Vector2 MoveInput => _moveInput;
        public bool IsMoving => _moveInput.sqrMagnitude > 0.01f;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _health = GetComponent<Health>();
            _animator = GetComponent<DirectionalAnimator>();
            _mainCamera = Camera.main;

            if (spriteRenderer == null)
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();

            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            _playerInput = GetComponent<PlayerInput>();
            if (_playerInput != null)
            {
                _moveAction = _playerInput.actions["Move"];
                _lookAction = _playerInput.actions["Look"];
                _primaryAttackAction = _playerInput.actions["PrimaryAttack"];
                _dashAction = _playerInput.actions["Dash"];
            }
        }

        private void OnEnable()
        {
            if (_primaryAttackAction != null)
                _primaryAttackAction.performed += OnPrimaryAttack;
            if (_dashAction != null)
                _dashAction.performed += OnDash;
        }

        private void OnDisable()
        {
            if (_primaryAttackAction != null)
                _primaryAttackAction.performed -= OnPrimaryAttack;
            if (_dashAction != null)
                _dashAction.performed -= OnDash;
        }

        private void Update()
        {
            if (_health.IsDead) return;

            ReadInput();
            UpdateFacingDirection();
        }

        private void FixedUpdate()
        {
            if (_health.IsDead)
            {
                _rb.velocity = Vector2.zero;
                return;
            }

            _rb.velocity = _moveInput * moveSpeed;
        }

        private void ReadInput()
        {
            if (_moveAction != null)
                _moveInput = _moveAction.ReadValue<Vector2>();
        }

        private void UpdateFacingDirection()
        {
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

            if (spriteRenderer != null)
                spriteRenderer.flipX = _facingDirection.x < 0;

            if (_animator != null)
            {
                var dir = DirectionalAnimator.VectorToDirection(_facingDirection);
                var state = IsMoving ? DirectionalAnimator.AnimState.Walk : DirectionalAnimator.AnimState.Idle;
                _animator.SetState(state, dir);
            }
        }

        private void OnPrimaryAttack(InputAction.CallbackContext ctx)
        {
            if (_health.IsDead) return;
            var combat = GetComponent<MeleeCombat>();
            if (combat != null)
                combat.TryAttack(_facingDirection);
        }

        private void OnDash(InputAction.CallbackContext ctx)
        {
            // Dash stub — will be implemented in full gameplay port
        }

        public void SetMoveSpeed(float speed)
        {
            moveSpeed = speed;
        }
    }
}
