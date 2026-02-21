using UnityEngine;
using UnityEngine.InputSystem;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Player movement and facing direction controller.
    /// Maps to Python's player movement system with 8-directional support.
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
        private Vector2 _moveInput;
        private Vector2 _facingDirection = Vector2.down;
        private Camera _mainCamera;

        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _primaryAttackAction;
        private InputAction _dashAction;

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

            _lookAction = new InputAction("Look", InputActionType.Value, "<Mouse>/position");
            _primaryAttackAction = new InputAction("PrimaryAttack", InputActionType.Button, "<Mouse>/leftButton");
            _dashAction = new InputAction("Dash", InputActionType.Button, "<Keyboard>/space");

            _primaryAttackAction.performed += OnPrimaryAttack;
            _dashAction.performed += OnDash;

            _moveAction.Enable();
            _lookAction.Enable();
            _primaryAttackAction.Enable();
            _dashAction.Enable();

            Debug.Log("[PlayerController] Standalone input actions created and enabled.");
        }

        private void OnDisable()
        {
            if (_primaryAttackAction != null)
                _primaryAttackAction.performed -= OnPrimaryAttack;
            if (_dashAction != null)
                _dashAction.performed -= OnDash;

            _moveAction?.Disable();
            _lookAction?.Disable();
            _primaryAttackAction?.Disable();
            _dashAction?.Disable();
        }

        private void OnDestroy()
        {
            _moveAction?.Dispose();
            _lookAction?.Dispose();
            _primaryAttackAction?.Dispose();
            _dashAction?.Dispose();
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
