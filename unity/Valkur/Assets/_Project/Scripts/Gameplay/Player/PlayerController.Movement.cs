using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay
{
    public partial class PlayerController : MonoBehaviour
    {

        private void Update()
        {
            if (_health.IsDead) return;
            if (_statusEffects != null && _statusEffects.IsStunned) return;

            // Suspend all player input while any runtime editor is active,
            // EXCEPT the Buildings Editor and Tile Editor which intentionally allow
            // movement so the developer can walk around and test colliders manually.
            if (GameEditorManager.HasInstance && GameEditorManager.Instance.AnyEditorActive &&
                !(GameEditorManager.Instance.ActiveEditor is BuildingsRuntimeEditor) &&
                !(GameEditorManager.Instance.ActiveEditor is Valkur.Gameplay.TileEditor.TileEditorManager))
            {
                _moveInput = Vector2.zero;
                return;
            }

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

            // Read the mouse directly from the device. The InputAction bound to
            // <Mouse>/position can return (0,0) when the cursor leaves the Game view
            // (focus loss, hovering editor chrome, etc.), which would otherwise yank
            // the player to face the bottom-left corner of the viewport. The pure
            // resolver clamps to viewport and falls back to move input when needed.
            // See PlayerFacingResolverTests for the regression coverage.
            Vector2 mouseScreen = Mouse.current != null
                ? Mouse.current.position.ReadValue()
                : Vector2.zero;
            Vector2 screenSize = new Vector2(Screen.width, Screen.height);
            bool isMouseInView = Mouse.current != null
                && PlayerFacingResolver.IsMouseWithinViewport(mouseScreen, screenSize);

            Vector2 mouseWorld = _mainCamera != null && isMouseInView
                ? (Vector2)_mainCamera.ScreenToWorldPoint(mouseScreen)
                : (Vector2)transform.position;

            _facingDirection = PlayerFacingResolver.ResolveFacingDirection(
                currentFacing: _facingDirection,
                mouseWorld: mouseWorld,
                isMouseInView: isMouseInView && _mainCamera != null,
                playerPos: transform.position,
                moveInput: _moveInput,
                isMoving: IsMoving);

            if (spriteRenderer != null && _animator == null)
                spriteRenderer.flipX = _facingDirection.x < 0;

            if (_animator != null)
            {
                var dir = _animator.ResolveDirectionFromVector(_facingDirection);
                var currentState = _animator.CurrentState;

                // Only override locomotion states (Idle / Walk / Chase).
                // Cast, Attack, Damage, and Death animations are owned by other systems
                // (SpellCaster, MeleeCombat, Health) and must not be interrupted here.
                if (currentState == DirectionalAnimator.AnimState.Idle ||
                    currentState == DirectionalAnimator.AnimState.Walk ||
                    currentState == DirectionalAnimator.AnimState.Chase)
                {
                    var state = IsMoving ? DirectionalAnimator.AnimState.Walk : DirectionalAnimator.AnimState.Idle;
                    _animator.SetState(state, dir);
                }
                else
                {
                    // Non-locomotion state active: update facing direction only so
                    // projectile targeting remains accurate while the animation plays.
                    _animator.SetDirectionFromVector(_facingDirection);
                }
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