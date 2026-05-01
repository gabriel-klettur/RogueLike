using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Core.Input;
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

            // Legacy fallback: under Unity 2022.3 in the Editor the new
            // InputSystem package intermittently drops OS event delivery and
            // _moveInput stays at (0,0) even while WASD is held. UnityEngine.Input
            // works as long as activeInputHandler != "Input System Package only".
            if (_moveInput.sqrMagnitude < 0.01f)
            {
                float lx = 0f, ly = 0f;
                if (UnityEngine.Input.GetKey(KeyCode.A) || UnityEngine.Input.GetKey(KeyCode.LeftArrow))  lx -= 1f;
                if (UnityEngine.Input.GetKey(KeyCode.D) || UnityEngine.Input.GetKey(KeyCode.RightArrow)) lx += 1f;
                if (UnityEngine.Input.GetKey(KeyCode.S) || UnityEngine.Input.GetKey(KeyCode.DownArrow))  ly -= 1f;
                if (UnityEngine.Input.GetKey(KeyCode.W) || UnityEngine.Input.GetKey(KeyCode.UpArrow))    ly += 1f;
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
            // Suspend movement and combat while any runtime editor is active,
            // except tools that intentionally allow walking around to test colliders.
            return GameEditorManager.HasInstance &&
                   GameEditorManager.Instance.AnyEditorActive &&
                   !(GameEditorManager.Instance.ActiveEditor is BuildingsRuntimeEditor) &&
                   !(GameEditorManager.Instance.ActiveEditor is Valkur.Gameplay.TileEditor.TileEditorManager);
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
            if ((_primaryAttackAction != null && _primaryAttackAction.IsPressed()) ||
                MouseInputManager.IsLeftMouseButtonPressed())
            {
                if (_spellCaster != null)
                    _spellCaster.TryCastByKey("fireball", _facingDirection);
            }

            // Secondary attack (right click) → slash spell
            // Python parity: M_RIGHT → slash
            if ((_secondaryAttackAction != null && _secondaryAttackAction.WasPerformedThisFrame()) ||
                MouseInputManager.WasRightMouseButtonPressedThisFrame())
            {
                if (_spellCaster != null)
                    _spellCaster.TryCastByKey("slash", _facingDirection);
            }

            // Middle click → laser beam (hold-to-channel)
            // Python parity: M_MIDDLE → laser_beam
            // First press starts the beam through the spell system; subsequent frames
            // refresh the controller directly (TryCastByKey is gated by cooldown so we
            // can't rely on it to keep the beam alive).
            if ((_middleClickAction != null && _middleClickAction.IsPressed()) ||
                MouseInputManager.IsMiddleMouseButtonPressed())
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
            if ((_middleClickAction != null && _middleClickAction.WasReleasedThisFrame()) ||
                MouseInputManager.WasMiddleMouseButtonReleasedThisFrame())
            {
                var beam = _spellCaster != null ? _spellCaster.GetComponent<LaserBeamController>() : null;
                if (beam != null) beam.Stop();
            }

            // Dash (Ctrl) → dash spell through spell system
            // Python parity: K_LCTRL / K_RCTRL → dash spell
            bool dashNew = _dashAction != null && _dashAction.WasPerformedThisFrame();
            bool dashLegacy = UnityEngine.Input.GetKeyDown(KeyCode.LeftControl)
                           || UnityEngine.Input.GetKeyDown(KeyCode.RightControl)
                           || UnityEngine.Input.GetKeyDown(KeyCode.RightShift);
            if (dashNew || dashLegacy)
            {
                if (_spellCaster != null && !_spellCaster.TryCastByKey("dash", _facingDirection))
                {
                    // Fallback: use DashAbility if spell system can't cast (cooldown, no mana, etc.)
                    if (_dashAbility != null)
                        _dashAbility.TryDash(_facingDirection);
                }
            }

            // All spell key bindings (1-0, q, e, r, t, f, g, c, v, x, p, l, u, m)
            // Python parity: full 23 spell key bindings.
            // OR new-system action with legacy KeyCode fallback (see InputCompat XML doc).
            if (_spellCaster != null)
            {
                foreach (var (action, spellKey) in _spellBindings)
                {
                    bool fired = (action != null && action.WasPerformedThisFrame())
                              || LegacyKeyDownForSpell(spellKey);
                    if (fired)
                    {
                        _spellCaster.TryCastByKey(spellKey, _facingDirection);
                        break; // only one spell per frame
                    }
                }
            }
        }

        private static bool LegacyKeyDownForSpell(string spellKey) => spellKey switch
        {
            "darkball"            => UnityEngine.Input.GetKeyDown(KeyCode.Alpha1),
            "iceball"             => UnityEngine.Input.GetKeyDown(KeyCode.Alpha2),
            "lightball"           => UnityEngine.Input.GetKeyDown(KeyCode.Alpha3),
            "puddle_lava"         => UnityEngine.Input.GetKeyDown(KeyCode.Alpha4),
            "mine_basic"          => UnityEngine.Input.GetKeyDown(KeyCode.Alpha5),
            "boomerang"           => UnityEngine.Input.GetKeyDown(KeyCode.Alpha6),
            "chain_lightning"     => UnityEngine.Input.GetKeyDown(KeyCode.Alpha7),
            "vortex_pull"         => UnityEngine.Input.GetKeyDown(KeyCode.Alpha8),
            "vortex_push"         => UnityEngine.Input.GetKeyDown(KeyCode.Alpha9),
            "flame_breath"        => UnityEngine.Input.GetKeyDown(KeyCode.Alpha0),
            "teleport"            => UnityEngine.Input.GetKeyDown(KeyCode.Q),
            "slash"               => UnityEngine.Input.GetKeyDown(KeyCode.E),
            "lightning"           => UnityEngine.Input.GetKeyDown(KeyCode.R),
            "sphere_magic_shield" => UnityEngine.Input.GetKeyDown(KeyCode.T),
            "smoke"               => UnityEngine.Input.GetKeyDown(KeyCode.F),
            "smoke_emitter"       => UnityEngine.Input.GetKeyDown(KeyCode.G),
            "arcane_flame"        => UnityEngine.Input.GetKeyDown(KeyCode.C),
            "firework_launch"     => UnityEngine.Input.GetKeyDown(KeyCode.V),
            "healing_aura"        => UnityEngine.Input.GetKeyDown(KeyCode.X),
            "meteor_shower"       => UnityEngine.Input.GetKeyDown(KeyCode.P),
            "healing_totem"       => UnityEngine.Input.GetKeyDown(KeyCode.L),
            "summon_barbol"       => UnityEngine.Input.GetKeyDown(KeyCode.U),
            "wall_ice"            => UnityEngine.Input.GetKeyDown(KeyCode.M),
            _ => false,
        };

        public void SetMoveSpeed(float speed)
        {
            moveSpeed = speed;
        }
    }
}
