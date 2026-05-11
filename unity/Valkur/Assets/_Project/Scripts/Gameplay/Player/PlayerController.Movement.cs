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
        // ── Cast animation timing ─────────────────────────────────────────────
        // How long the player's DirectionalAnimator stays in the Cast state after
        // a successful TryCastByKey before locomotion (Idle/Walk) takes over again.
        // Each Cast sprite-set frame plays at DirectionalAnimator.frameInterval (0.15 s
        // by default), so 0.35 s reliably plays at least 2 frames of the cast pose
        // before reverting — enough to read as "casting" without delaying gameplay.
        private const float CAST_ANIMATION_DURATION = 0.35f;

        // Time.time at which the cast animation should end and locomotion can
        // resume. 0 = no cast animation pending. Refreshed on every successful
        // cast (including held casts like the laser beam), so the animation
        // stays alive for as long as the player is channeling.
        private float _castAnimEndTime;

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
            // Spirit-mode trumps editor suspension: the player has to be able
            // to walk to the altar even if a runtime editor (F3 spawner, F5
            // entities, etc.) was left open at the moment of death. Treat
            // spirit form as an implicit "allows player movement" flag.
            bool inputSuspended = !isSpirit && IsGameplayInputSuspended();

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

            // Movement-only editors (Tile Editor) keep WASD walking but
            // suppress every combat input so left-click paint doesn't also
            // fire fireball, right-click doesn't slash, etc.
            if (IsPlayerCombatSuspended()) return;

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

        private static bool IsPlayerCombatSuspended()
        {
            if (!GameEditorManager.HasInstance) return false;
            return ShouldSuspendCombatFor(GameEditorManager.Instance.ActiveEditor);
        }

        /// <summary>
        /// Pure predicate: returns whether the given active editor should
        /// suspend the player's combat actions (attacks / dash / spell casts)
        /// while keeping movement enabled. Editors opt in via
        /// <see cref="ISuspendsPlayerCombat"/>. Internal so EditMode tests can
        /// drive the gate without bringing up a full <see cref="GameEditorManager"/>
        /// in the scene.
        /// </summary>
        internal static bool ShouldSuspendCombatFor(GameEditorManager.IGameEditor active)
        {
            if (active == null) return false;
            return active is ISuspendsPlayerCombat;
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
                // If a cast animation has expired since last frame, hand control
                // back to locomotion BEFORE the override checks the current state.
                TickCastAnimRevert();

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

        // ── Cast animation helpers ────────────────────────────────────────────

        /// <summary>
        /// Pushes the player's <see cref="DirectionalAnimator"/> into the Cast state
        /// for the current facing direction and refreshes the revert timer. Invoked
        /// from <see cref="PollCombatActions"/> on every successful TryCastByKey
        /// (fireball / slash / hotkey-bound spells / laser-beam refresh). Dash is
        /// intentionally excluded — it owns its own movement animation.
        /// </summary>
        private void TriggerCastAnimation()
        {
            if (_animator == null) return;
            var dir = _animator.ResolveDirectionFromVector(_facingDirection);
            _animator.SetState(DirectionalAnimator.AnimState.Cast, dir);
            _castAnimEndTime = Time.time + CAST_ANIMATION_DURATION;
        }

        /// <summary>
        /// Reverts the animator out of <see cref="DirectionalAnimator.AnimState.Cast"/>
        /// once <see cref="_castAnimEndTime"/> has elapsed, choosing Walk or Idle to
        /// match the current movement input. No-op while the timer is in the future
        /// (held casts like the laser beam keep refreshing it).
        /// </summary>
        private void TickCastAnimRevert()
        {
            if (_animator == null || _castAnimEndTime <= 0f) return;
            if (Time.time < _castAnimEndTime) return;
            if (_animator.CurrentState == DirectionalAnimator.AnimState.Cast)
            {
                var dir = _animator.ResolveDirectionFromVector(_facingDirection);
                var state = IsMoving ? DirectionalAnimator.AnimState.Walk : DirectionalAnimator.AnimState.Idle;
                _animator.SetState(state, dir);
            }
            _castAnimEndTime = 0f;
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
                if (_spellCaster != null && _spellCaster.TryCastByKey("fireball", _facingDirection))
                    TriggerCastAnimation();
            }

            // Secondary attack (right click) → slash spell
            // Python parity: M_RIGHT → slash
            if (MouseInputManager.WasRightMouseButtonPressedThisFrame())
            {
                if (_spellCaster != null && _spellCaster.TryCastByKey("slash", _facingDirection))
                    TriggerCastAnimation();
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
                    // Beam is hold-to-channel — keep refreshing the cast animation
                    // each frame so the pose persists for as long as the player
                    // holds the trigger.
                    TriggerCastAnimation();
                }
            }
            if (MouseInputManager.WasMiddleMouseButtonReleasedThisFrame())
            {
                var beam = _spellCaster != null ? _spellCaster.GetComponent<LaserBeamController>() : null;
                if (beam != null) beam.Stop();
            }

            // Dash — fired by any of:
            //   • Space  (canonical InputService.Gameplay.Dash action)
            //   • RightShift  (legacy Python-parity fallback)
            //   • LeftCtrl   (per user request)
            //   • RightCtrl  (per user request)
            //
            // Ctrl-as-dash is safe during gameplay because no gameplay system
            // reads Ctrl-modified input — every IsCtrlHeld() callsite outside
            // this file lives in a runtime editor (Tile / Buildings / Items /
            // Lighting / Boss), and those editors gate gameplay via
            // InputBlocker.IsGameplayBlocked, which short-circuits this entire
            // PollCombatActions method. So Ctrl+S, Ctrl+Z, Ctrl-drag, etc. in
            // editors do NOT also fire a dash, and during pure gameplay there
            // are no Ctrl combos for the dash to interfere with.
            //
            // Direction always tracks the MOUSE CURSOR — _facingDirection is
            // already updated to point at the mouse world position whenever
            // the cursor is inside the viewport (see PlayerFacingResolver),
            // so reusing it gives the user "dash toward where the mouse is"
            // for every trigger uniformly. When the cursor is offscreen the
            // resolver falls back to movement direction, which is the only
            // sensible alternative.
            //
            // The Ctrl press detection reads BOTH input backends directly
            // (legacy UnityEngine.Input.GetKeyDown + Keyboard.current.*Ctrl
            // wasPressedThisFrame) instead of going through
            // KeyboardInputManager. The InputActions asset binds leftCtrl to
            // the "CtrlModifier" action which the new InputSystem can flag as
            // "consumed" in some scenarios, masking subsequent reads. The
            // direct OR-fallback survives that and matches the same pattern
            // the rest of the file uses for arrow keys.
            var dashAction = DashAction;
            bool dashNew = dashAction != null && dashAction.WasPerformedThisFrame();
            bool dashLegacy = KeyboardInputManager.WasKeyPressedThisFrame(Key.RightShift, KeyCode.RightShift);

            // Route through KeyboardInputManager so the InputCentralizationGuard
            // test passes — direct Keyboard.current reads outside the helper class
            // are forbidden (see CLAUDE.md "Input pipeline" section).
            bool leftCtrlPressed  = KeyboardInputManager.WasKeyPressedThisFrame(Key.LeftCtrl,  KeyCode.LeftControl);
            bool rightCtrlPressed = KeyboardInputManager.WasKeyPressedThisFrame(Key.RightCtrl, KeyCode.RightControl);
            bool dashCtrl = leftCtrlPressed || rightCtrlPressed;

            // Single source of truth: the spell-based dash is the only path.
            // The legacy DashAbility fallback was removed because it has zero
            // visuals (no ghost trail, no particle wake, no light streak), so
            // when the user pressed dash twice quickly the spell would be on
            // cooldown and the silent DashAbility would fire instead — making
            // the second dash *look* like an instant teleport even though the
            // entity was actually moving via velocity. Now a cooldown-blocked
            // dash simply does nothing, matching the UX of every other spell
            // on cooldown. _dashAbility is still kept on the player so its
            // IsDashing flag can gate other systems if needed.
            if ((dashNew || dashLegacy || dashCtrl) && _spellCaster != null)
            {
                if (_spellCaster.TryCastByKey("dash", _facingDirection))
                    TriggerCastAnimation();
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
                        if (_spellCaster.TryCastByKey(spellKey, _facingDirection))
                            TriggerCastAnimation();
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
