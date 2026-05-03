using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay
{
    public partial class PlayerController : MonoBehaviour
    {

        private void Update()
        {
            // Spirit-form players still control movement and facing, but skip
            // combat entirely. A truly-dead player (HP=0 outside the spirit
            // flow — i.e. a future final-death state) early-exits as before.
            bool isSpirit = IsSpirit;
            if (_health.IsDead && !isSpirit) return;

            // Hot-reload guard: with Domain Reload off, Unity serialises private
            // InputAction fields and restores them as zombies (bindings.Count == 0,
            // actionMap == null) — left-click attack and dash silently die. Detect
            // and rebuild before reading so the player's combat input survives any
            // mid-Play recompile.
            EnsureInputActionsLive();

            bool isStunned = _statusEffects != null && _statusEffects.IsStunned;
            bool inputSuspended = IsGameplayInputSuspended();

            if (isStunned || inputSuspended)
            {
                _moveInput = Vector2.zero;
            }
            else
            {
                ReadInput();
            }

            UpdateFacingDirection();

            if (isStunned || inputSuspended) return;

            // Spirits can move but cannot attack, dash, or cast.
            if (isSpirit) return;

            PollCombatActions();
        }

        private void FixedUpdate()
        {
            bool isSpirit = IsSpirit;
            if (_health.IsDead && !isSpirit)
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
            // Read from the canonical InputService.Gameplay.Move action.
            var move = MoveAction;
            if (move != null) _moveInput = move.ReadValue<Vector2>();

            // Legacy fallback: under Unity 2022.3 in the Editor the new
            // InputSystem package intermittently drops OS event delivery and
            // _moveInput stays at (0,0) even while WASD is held. KeyboardInputManager
            // wraps the OR-of-new-and-legacy for each key.
            if (_moveInput.sqrMagnitude < 0.01f)
            {
                float lx = 0f, ly = 0f;
                if (KeyboardInputManager.IsKeyPressed(Key.A, KeyCode.A) ||
                    KeyboardInputManager.IsKeyPressed(Key.LeftArrow, KeyCode.LeftArrow))   lx -= 1f;
                if (KeyboardInputManager.IsKeyPressed(Key.D, KeyCode.D) ||
                    KeyboardInputManager.IsKeyPressed(Key.RightArrow, KeyCode.RightArrow)) lx += 1f;
                if (KeyboardInputManager.IsKeyPressed(Key.S, KeyCode.S) ||
                    KeyboardInputManager.IsKeyPressed(Key.DownArrow, KeyCode.DownArrow))   ly -= 1f;
                if (KeyboardInputManager.IsKeyPressed(Key.W, KeyCode.W) ||
                    KeyboardInputManager.IsKeyPressed(Key.UpArrow, KeyCode.UpArrow))       ly += 1f;
                if (lx != 0f || ly != 0f)
                {
                    var legacy = new Vector2(lx, ly);
                    if (legacy.sqrMagnitude > 1f) legacy = legacy.normalized;
                    _moveInput = legacy;
                }
            }
        }

        private static bool IsGameplayInputSuspended()
        {
            if (!GameEditorManager.HasInstance) return false;
            return ShouldSuspendInputFor(GameEditorManager.Instance.ActiveEditor);
        }

        /// <summary>
        /// Pure predicate: returns whether the given active editor should freeze
        /// the player's gameplay input (movement + combat). Editors that need
        /// the player to keep walking around (collider testing, spawner
        /// placement, tilemap collisions) opt out by implementing
        /// <see cref="IAllowsPlayerMovement"/>. Internal so EditMode tests can
        /// drive the gate without bringing up a full <see cref="GameEditorManager"/>
        /// in the scene.
        /// </summary>
        internal static bool ShouldSuspendInputFor(GameEditorManager.IGameEditor active)
        {
            if (active == null) return false;
            return !(active is IAllowsPlayerMovement);
        }

        private void UpdateFacingDirection()
        {
            if (_mainCamera == null || !_mainCamera.isActiveAndEnabled)
                _mainCamera = Camera.main;

            bool hasMouseWorld = MouseInputManager.TryGetWorldMousePosition(
                out Vector2 mouseWorld,
                _mainCamera,
                requireInView: true,
                requireApplicationFocus: false);
            if (!hasMouseWorld)
                mouseWorld = transform.position;

            _facingDirection = PlayerFacingResolver.ResolveFacingDirection(
                currentFacing: _facingDirection,
                mouseWorld: mouseWorld,
                isMouseInView: hasMouseWorld,
                playerPos: ResolveFacingOrigin(),
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

        private Vector2 ResolveFacingOrigin()
        {
            if (spriteRenderer != null && spriteRenderer.enabled && spriteRenderer.sprite != null)
                return spriteRenderer.bounds.center;

            var collider2D = GetComponent<Collider2D>();
            if (collider2D == null)
                collider2D = GetComponentInChildren<Collider2D>();
            if (collider2D != null)
                return collider2D.bounds.center;

            return transform.position;
        }

        private void PollCombatActions()
        {
            bool isDashing = _dashAbility != null && _dashAbility.IsDashing;
            if (isDashing) return;

            // Primary attack (left click) → fireball (spell slot 0)
            // Python parity: M_LEFT → fireball
            // IsPressed allows hold-to-fire; SpellCaster cooldown (0.4 s) gates the rate.
            // MouseInputManager already ORs new InputSystem with the legacy backend, so
            // we don't need to read PrimaryAttackAction separately — both backends are
            // covered.
            if (MouseInputManager.IsLeftMouseButtonPressed())
            {
                if (_spellCaster != null)
                    _spellCaster.TryCastByKey("fireball", _facingDirection);
            }

            // Secondary attack (right click) → slash spell
            // Python parity: M_RIGHT → slash
            if (MouseInputManager.WasRightMouseButtonPressedThisFrame())
            {
                if (_spellCaster != null)
                    _spellCaster.TryCastByKey("slash", _facingDirection);
            }

            // Middle click → laser beam (hold-to-channel)
            // Python parity: M_MIDDLE → laser_beam
            // First press starts the beam through the spell system; subsequent frames
            // refresh the controller directly (TryCastByKey is gated by cooldown so we
            // can't rely on it to keep the beam alive). MouseInputManager covers both
            // backends, so we don't need to consult MiddleClickAction separately.
            if (MouseInputManager.IsMiddleMouseButtonPressed())
            {
                if (_spellCaster != null)
                {
                    var existingBeam = _spellCaster.GetComponent<LaserBeamController>();
                    if (existingBeam != null)
                        existingBeam.Refresh();
                    else
                        _spellCaster.TryCastByKey("laser_beam", _facingDirection);
                }
            }
            if (MouseInputManager.WasMiddleMouseButtonReleasedThisFrame())
            {
                var beam = _spellCaster != null ? _spellCaster.GetComponent<LaserBeamController>() : null;
                if (beam != null) beam.Stop();
            }

            // Dash (Space) → dash spell through spell system.
            // The canonical InputService.Gameplay.Dash action covers Space; we OR
            // with the legacy RightShift fallback for the Python control scheme.
            //
            // LeftCtrl / RightCtrl are intentionally NOT bound to dash any more:
            // Ctrl is the universal "modifier for combos" (Ctrl+Z undo, Ctrl+S save,
            // Ctrl+C copy, …). Mapping it to a movement spell makes every Ctrl-combo
            // simultaneously fire a dash, teleporting the player and breaking gameplay
            // the moment the user reaches for any standard shortcut.
            var dashAction = DashAction;
            bool dashNew = dashAction != null && dashAction.WasPerformedThisFrame();
            bool dashLegacy = KeyboardInputManager.WasKeyPressedThisFrame(Key.RightShift, KeyCode.RightShift);
            if (dashNew || dashLegacy)
            {
                if (_spellCaster != null && !_spellCaster.TryCastByKey("dash", _facingDirection))
                {
                    // Fallback: use DashAbility if spell system can't cast (cooldown, no mana, etc.)
                    if (_dashAbility != null)
                        _dashAbility.TryDash(_facingDirection);
                }
            }

            // All 23 spell key bindings (1-0, q, e, r, t, f, g, c, v, x, p, l, u, m).
            // Python parity. The (action, spellKey, legacyKey) triples come from
            // InputService.Gameplay.EnumerateSpellBindings — single source of truth
            // for both the InputAction reference and the legacy KeyCode fallback.
            var gp = InputService.Instance?.Gameplay;
            if (_spellCaster != null && gp != null)
            {
                foreach (var (action, spellKey, legacyKey) in gp.EnumerateSpellBindings())
                {
                    bool fired = (action != null && action.WasPerformedThisFrame())
                              || UnityEngine.Input.GetKeyDown(legacyKey);
                    if (fired)
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
