using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Player;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay
{
    public partial class PlayerController : MonoBehaviour
    {
        // ── Cast animation timing ─────────────────────────────────────────────
        // How long the player's DirectionalAnimator stays in the Cast state after
        // a successful TryCastByKey before locomotion (Idle/Walk) takes over again.
        /// <summary>Floor for a cast's animation window. The real length is measured off the
        /// frames — see <see cref="TriggerCastAnimation"/> — and this is what a character
        /// whose cast art is one static pose gets instead.</summary>
        private const float CAST_ANIMATION_DURATION = 0.35f;
        private const float REGULAR_SLASH_ANIMATION_DURATION = 0.42f;

        // Time.time at which the cast animation should end and locomotion can
        // resume. 0 = no cast animation pending. Refreshed on every successful
        // cast (including held casts like the laser beam), so the animation
        // stays alive for as long as the player is channeling.
        private float _castAnimEndTime;

        /// <summary>When the current cast's prepare + channel end. A repeat variant may close the
        /// pose early (<see cref="TickCastAnimRevert"/>), but never inside the spell itself.</summary>
        private float _castPhaseEndTime;

        /// <summary>Slowest cadence (prepare + channel + cooldown, seconds) that still counts as
        /// one sustained cast rather than separate ones. A held fireball is 0.43.</summary>
        private const float REPEAT_MAX_CADENCE = 0.9f;

        // Which spell the window above belongs to. Only used to tell a HELD channel's
        // per-frame refresh apart from a fresh cast, so the former does not re-roll the
        // animation variant on every frame. Null is a legitimate value — the slash, dash and
        // beam paths pass no key — and null matches null, which is what makes the beam's own
        // refresh count as "the same cast".
        private string _castAnimSpellKey;

        // The state the open window pushed the animator into. Needed because a spell can now
        // name any of the eight, and the revert has to hand control back from whichever one
        // it actually entered.
        private DirectionalAnimator.AnimState _castAnimState;

        /// <summary>True while a cast animation still owns the animator. Locomotion holds off
        /// for as long as this is true, which is what lets a spell play a LOCOMOTION state
        /// without the movement code overwriting it on the next frame.</summary>
        private bool IsCastAnimWindowOpen => _castAnimEndTime > 0f && Time.time < _castAnimEndTime;

        // Resolved lazily rather than in Awake: EntitySetup only attaches the component to a
        // character that declares a loadout, and it does so after the visuals bind.
        private PlayerLoadoutController _loadouts;

        // Next animation variant to play, per AnimState. Sized lazily from the enum rather
        // than from a literal, so an added state cannot silently index past the end.
        private int[] _nextVariantByState;

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

            if (isStunned) return;

            if (inputSuspended)
            {
                // An editor that redirects left click (Spells F4) still wants THAT one
                // gesture while everything else stays frozen. Deliberately not solved by
                // marking the editor IAllowsPlayerMovement, which is the obvious one-word
                // fix and is wrong twice over: ReadInput OR-reads raw WASD with no
                // focused-field guard, so typing in the editor's search box would walk the
                // player; and WorldDropInteractor gates its left-click drag on the same
                // interface, so world drops would fight the cast for the same click.
                // Every editor that does not opt in answers null below and stays frozen.
                if (!isSpirit) PollRedirectedPrimaryCast();
                return;
            }

            // Spirits can move but cannot attack, dash, or cast.
            if (isSpirit) return;

            // Movement-only editors (Tile Editor) keep WASD walking but
            // suppress every combat input so left-click paint doesn't also
            // fire fireball, right-click doesn't slash, etc.
            if (IsPlayerCombatSuspended()) return;

            // Traversal first, and OUTSIDE the stance gate. Everything below this line is
            // combat; the dash is not, and Peace must not take it away.
            PollTraversal();

            // Peace is the third owner of "no combat right now", after the stun check above
            // and IsPlayerCombatSuspended. They COMPOSE as a chain of early returns rather
            // than one of them writing a flag the others read -- which is the arrangement
            // SetInvincible got wrong twice in this project, each owner clearing what another
            // was holding. Consulted HERE, at the reader, and never by disabling the action
            // map: InputBlocker's own comment records that Map.Disable silences bound actions
            // and leaves every MouseInputManager / KeyboardInputManager callsite untouched,
            // so a map-based stance would leak through the legacy OR-gate in silence.
            //
            // The F4 Spells Editor's redirected left click is deliberately NOT gated: it is
            // reached from PollRedirectedPrimaryCast inside the inputSuspended branch far
            // above, so it never arrives here. That is by construction rather than by
            // intent, which is exactly why StanceGateTests pins it -- the obvious tidy-up is
            // to hoist this check to the top of Update, and that would silently break
            // authoring a spell while in Peace.
            if (PlayerStance.IsPeace) return;

            PollCombatActions();
        }

        private void FixedUpdate()
        {
            bool isSpirit = IsSpirit;
            if (_health.IsDead && !isSpirit)
            {
                _rb.velocity = Vector2.zero;
                BreakLocomotion(GaitBreak.Disabled);
                return;
            }

            // Stun zeroes velocity (StunEffect.Tick also handles this, double-safe)
            if (_statusEffects != null && _statusEffects.IsStunned)
            {
                _rb.velocity = Vector2.zero;
                BreakLocomotion(GaitBreak.Disabled);
                return;
            }

            // A root holds the feet and nothing else, so it returns HERE — after Update has
            // already run ReadInput, UpdateFacingDirection and PollCombatActions for the
            // frame. Folding it into the isStunned branch above would have cost the player
            // their attacks and their aim, which is what separates the two effects.
            if (_statusEffects != null && _statusEffects.IsRooted)
            {
                _rb.velocity = Vector2.zero;
                BreakLocomotion(GaitBreak.Disabled);
                return;
            }

            // Dash overrides normal movement. The gait is not stepped and not broken: a dash out
            // of a run lands still running. The measured position is forgotten, though, or the
            // next step would read the teleport as a sprint.
            if (_dashAbility != null && _dashAbility.IsDashing)
            {
                _hasLastFixedPosition = false;
                return;
            }

            // A spell that does not allow movement plants the caster for as long as it is
            // WINDING UP or CHANNELLING — never for its cooldown, which is when it may be cast
            // again rather than time the caster is busy.
            //
            // `allowMovement` was authored on all 104 spells and read by nobody for the life of
            // the project, which was harmless while the longest wind-up in the catalogue was
            // half a second. It stops being harmless the moment a summon takes 1.2 s: without
            // this the player walks away mid-cast and the spell lands from wherever they got to.
            if (IsPlantedByCast())
            {
                _rb.velocity = Vector2.zero;
                BreakLocomotion(GaitBreak.Cast);
                return;
            }

            // M1.9 — hard-stop on void cells. When the predicted next cell has
            // zero tiles in any visible layer it's a "you can't walk here"
            // wall, even if the cell has no Collision tile. Axis-split clamp
            // so the player still slides along the edge instead of freezing
            // on diagonal input.
            Vector2 clampedInput = ClampInputAgainstVoid(_moveInput);

            // A ghost walks; it does not run (DeathTuning owns its speed).
            if (isSpirit)
            {
                BreakLocomotion(GaitBreak.Disabled);
                _rb.velocity = clampedInput * CurrentMoveSpeed;
                return;
            }

            // Walking into a run — see PlayerController.Locomotion.
            _rb.velocity = StepLocomotion(clampedInput);
        }

        /// <summary>
        /// Predict the player's next position under <paramref name="rawInput"/>
        /// and zero out any axis whose component lands the player inside a
        /// void cell (no tiles in <see cref="World.Layering.VisualLayerProbe"/>
        /// at that point). Each axis is tested independently so diagonal
        /// motion can still slide along the edge of a void instead of
        /// freezing entirely — matches the feel of walking into a regular
        /// Unity collider.
        ///
        /// Returns <paramref name="rawInput"/> unchanged when the
        /// <see cref="WorldGridBuilder"/> isn't available (boot-time race,
        /// EditMode tests without a grid). Cero impacto en mapas legacy:
        /// every cell inside a zone has Ground painted, so the probe sample
        /// always returns ≥ 1 layer and the clamp is a no-op.
        /// </summary>
        internal Vector2 ClampInputAgainstVoid(Vector2 rawInput)
        {
            if (rawInput.sqrMagnitude < 0.0001f) return rawInput;
            if (_voidProbeGrid == null)
                _voidProbeGrid = FindObjectOfType<World.WorldGridBuilder>();
            if (_voidProbeGrid == null) return rawInput;

            Vector2 origin = transform.position;
            float step = CurrentMoveSpeed * Time.fixedDeltaTime;

            // Predicted full-vector position. If it's in painted territory,
            // no clamp needed (fast path — the common case for legacy maps).
            Vector2 next = origin + rawInput * step;
            if (!IsVoidCell(next)) return rawInput;

            // The combined input lands in a void — test each axis alone.
            Vector2 result = rawInput;
            if (result.x != 0f)
            {
                Vector2 xOnly = origin + new Vector2(result.x, 0f) * step;
                if (IsVoidCell(xOnly)) result.x = 0f;
            }
            if (result.y != 0f)
            {
                Vector2 yOnly = origin + new Vector2(0f, result.y) * step;
                if (IsVoidCell(yOnly)) result.y = 0f;
            }
            return result;
        }

        // Reused 9-element sample buffer for the void probe. VisualLayerProbe.Sample
        // takes a caller-allocated bool[] so we never GC on the hot path.
        // Sized from the ladder, never a literal. VisualLayerProbe.Sample REFUSES a buffer
        // shorter than the layer count and answers 0 without filling it — so a stale 9 does not
        // throw, it reports "no tile on any layer" for every cell, and ClampInputAgainstVoid
        // then reads the whole world as void and zeroes the player's input. Measured exactly
        // that the day the ladder grew to sixteen: the character could not move at all.
        private readonly bool[] _voidSampleBuf = new bool[SortingConfig.VISUAL_LAYER_COUNT];

        // Cached WorldGridBuilder ref. Resolved lazily on first void probe so
        // the field never carries a stale reference across scene reloads.
        private World.WorldGridBuilder _voidProbeGrid;

        private bool IsVoidCell(Vector2 worldPos)
        {
            // VisualLayerProbe.Sample returns the COUNT of populated layers; the
            // bool[] is filled side-effect-y. Zero means no tile in any of the
            // 9 visual layers including Collision — a true "void" wall cell.
            return World.Layering.VisualLayerProbe.Sample(worldPos, _voidProbeGrid, _voidSampleBuf) == 0;
        }

        private void ReadInput()
        {
            // Read from the canonical InputService.Gameplay.Move action.
            var move = MoveAction;
            if (move != null) _moveInput = move.ReadValue<Vector2>();

            // The autowalk console verb (PlayerController.Locomotion): the keys are ignored while it runs.
            if (HasDebugMove) { _moveInput = _debugMove; return; }

            // Legacy fallback: under Unity 2022.3 in the Editor the new InputSystem package
            // intermittently drops OS event delivery and _moveInput stays at (0,0) even while
            // the movement keys are held.
            //
            // The eight Key/KeyCode literals this used to be — WASD plus the arrows — were the
            // reason movement could not be rebound: the override moved the composite and the
            // fallback went on walking the player with W. InputBindingResolver reads the SAME
            // composite the action declares, using its part names to know which way each
            // control points, so both halves move together.
            if (_moveInput.sqrMagnitude < 0.01f)
            {
                var legacy = InputBindingResolver.ReadVectorFallback(move);
                if (legacy.sqrMagnitude > 0.01f) _moveInput = legacy;
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

        /// <summary>
        /// Which spell key left click casts this frame.
        ///
        /// Pure so EditMode tests can drive it without a scene, matching
        /// <see cref="ShouldSuspendCombatFor"/>. With no active editor, or an editor that does
        /// not opt in, or one that opts in with nothing selected, the answer is
        /// <paramref name="defaultKey"/> — which is what keeps normal gameplay untouched.
        /// </summary>
        internal static string ResolvePrimaryCastKey(GameEditorManager.IGameEditor active, string defaultKey)
        {
            var chooser = active as IChoosesPrimaryCastSpell;
            string key = chooser?.PrimaryCastSpellKey;
            return string.IsNullOrEmpty(key) ? defaultKey : key;
        }

        private string ResolvePrimaryCastKey()
        {
            var active = GameEditorManager.HasInstance ? GameEditorManager.Instance.ActiveEditor : null;
            return ResolvePrimaryCastKey(active, DEFAULT_PRIMARY_SPELL_KEY);
        }

        /// <summary>What left click casts during ordinary play. Python parity: M_LEFT.</summary>
        private const string DEFAULT_PRIMARY_SPELL_KEY = "fireball";

        /// <summary>What right click casts. Python parity: M_RIGHT. Public so the player panel's
        /// mouse slots show the spell the button actually casts — they used to read SpellCaster
        /// slots 0-2 under the labels 1, 2 and 3, and slot 0 is the LEFT CLICK.</summary>
        public const string SecondarySpellKey = "slash";

        /// <summary>What middle click channels. Python parity: M_MIDDLE.</summary>
        public const string MiddleSpellKey = "laser_beam";

        /// <summary>What left click casts right now: the default, or the spell an open editor
        /// that opts in (<see cref="IChoosesPrimaryCastSpell"/>) has selected.</summary>
        public string PrimarySpellKeyNow => ResolvePrimaryCastKey();

        /// <summary>Beam started by LEFT click, so its release does not stop a middle-click one.</summary>
        private LaserBeamController _leftHeldBeam;

        /// <summary>
        /// Minimum seconds between REPEATS of a held left click while a redirecting editor
        /// owns the primary cast.
        ///
        /// <para>The redirected poll reads the button as HELD, so a spell whose own
        /// <c>cooldownDuration</c> is 0 -- which is every <see cref="SpellType.AnimationProbe"/>
        /// and several authoring-time spells -- fires once per frame for as long as the button
        /// is down, i.e. sixty casts a second. That is not a cooldown bug: the editor is where
        /// a spell is watched, and stacking sixty copies of it makes the one thing the author
        /// is looking at unreadable. Ordinary gameplay is untouched -- this gate lives only in
        /// <see cref="PollRedirectedPrimaryCast"/>.</para>
        /// </summary>
        private const float REDIRECTED_CAST_REPEAT_INTERVAL = 1f;

        /// <summary>Earliest <see cref="Time.time"/> a HELD redirected click may cast again.</summary>
        private float _nextRedirectedCastTime;

        /// <summary>
        /// Whether a redirected left click may cast on this frame.
        ///
        /// <para>Pure so EditMode tests can drive it without a scene, the same shape as
        /// <see cref="ResolvePrimaryCastKey"/> and <see cref="ShouldSuspendCombatFor"/>.</para>
        ///
        /// <para>Two carve-outs, and each is the difference between a throttle and a defect.
        /// A CHANNELLED spell is refreshed every frame and ended by the release, so gating it
        /// would chop the beam into one-second pulses rather than limit anything. And a FRESH
        /// PRESS always casts -- the gate exists for the HOLD, and making a deliberate click
        /// wait out an interval left over from the previous one reads as the editor having
        /// ignored it.</para>
        /// </summary>
        internal static bool AllowsRedirectedCast(
            bool channelled, bool pressedThisFrame, float now, float nextAllowedTime)
            => channelled || pressedThisFrame || now >= nextAllowedTime;

        /// <summary>
        /// Casts <paramref name="key"/> for a held trigger, using channel semantics when the
        /// spell is one.
        ///
        /// A beam is hold-to-channel: Begin once, Refresh every frame, Stop on release. Firing
        /// one through the ordinary path instead would start it and never refresh it, so it
        /// would die after <see cref="LaserBeamController.AUTO_STOP_GRACE"/> — about a sixth of
        /// a second — and read as a beam that flickers rather than one the player is holding.
        /// This is the same shape the middle-click branch already uses; it lives here because
        /// left click can now be pointed at ANY spell, beams included.
        /// </summary>
        /// <summary>
        /// True when the active editor is asking left click to cast something specific.
        /// Pure so EditMode tests can drive it without a scene, matching
        /// <see cref="ShouldSuspendInputFor"/>.
        /// </summary>
        internal static bool EditorRedirectsPrimaryCast(GameEditorManager.IGameEditor active)
            => !string.IsNullOrEmpty((active as IChoosesPrimaryCastSpell)?.PrimaryCastSpellKey);

        /// <summary>
        /// Whether the active editor's redirected left-click cast is exempt from mana.
        /// Requiring an active editor and a live selection prevents either the waiver or
        /// the redirected key from leaking into ordinary gameplay after the editor closes.
        /// </summary>
        internal static bool PrimaryCastIgnoresManaCost(GameEditorManager.IGameEditor active)
        {
            var chooser = active as IChoosesPrimaryCastSpell;
            return active != null
                && active.IsActive
                && chooser != null
                && chooser.PrimaryCastIgnoresManaCost
                && !string.IsNullOrEmpty(chooser.PrimaryCastSpellKey);
        }

        /// <summary>
        /// The left-click primary on its own — the only combat gesture allowed through while
        /// a redirecting editor has gameplay input suspended. No dash, no right-click slash,
        /// no number-key casts, no movement.
        /// </summary>
        private void PollRedirectedPrimaryCast()
        {
            var active = GameEditorManager.HasInstance ? GameEditorManager.Instance.ActiveEditor : null;
            string key = ResolvePrimaryCastKey(active, null);

            // Handled before anything else so a beam is always released, whether the editor
            // closed under it, the selection was cleared, or the pointer wandered onto a panel
            // mid-hold. Otherwise the reference dangles and the next click stops a stale beam.
            if (MouseInputManager.WasLeftMouseButtonReleasedThisFrame() || string.IsNullOrEmpty(key))
            {
                StopLeftHeldBeam();
                return;
            }

            // A click that lands on the editor's own panels belongs to the editor. Without
            // this, every click on a spell tile in the picker would also cast into the world
            // behind it.
            if (IsPointerOverInteractiveUI()) return;

            if (!MouseInputManager.IsLeftMouseButtonPressed()) return;

            // A beam is hold-to-channel -- CastHeldPrimary refreshes it every frame and the
            // release is what ends it -- so throttling it would chop the laser into one-second
            // pulses instead of limiting anything. Only discrete casts repeat.
            var spell = _spellCaster != null ? _spellCaster.GetSpellByKey(key) : null;
            bool channelled = spell != null && spell.type == SpellType.Beam;

            if (!AllowsRedirectedCast(
                    channelled,
                    MouseInputManager.WasLeftMouseButtonPressedThisFrame(),
                    Time.time,
                    _nextRedirectedCastTime))
                return;

            if (!channelled)
                _nextRedirectedCastTime = Time.time + REDIRECTED_CAST_REPEAT_INTERVAL;

            CastHeldPrimary(key, PrimaryCastIgnoresManaCost(active));
        }

        /// <summary>
        /// Cut anything still in flight when the player drops into Peace.
        ///
        /// <para>This is the hole the PollTraversal split opens, and it is silent without a
        /// handler. Three things in this file are HELD rather than fired: a left-held primary
        /// beam, the middle-click laser, and a charging spell. Every one of them is ended by a
        /// line that lives inside <see cref="PollCombatActions"/> -- the release check, the
        /// button-up branch, the charge poll -- so the moment that method stops being called
        /// the ending never arrives. A player who holds the laser and hits Tab would keep
        /// channelling it, in a stance whose whole promise is that they cannot attack.</para>
        ///
        /// <para>Driven off the transition rather than polled beside the gate, because a poll
        /// would re-run these three every frame of a stance that is meant to be doing nothing
        /// at all.</para>
        /// </summary>
        private void OnStanceChanged(Stance stance)
        {
            if (stance != Stance.Peace) return;

            StopLeftHeldBeam();

            if (_spellCaster == null) return;

            var beam = _spellCaster.GetComponent<LaserBeamController>();
            if (beam != null) beam.Stop();

            // A charge that is never released holds the spell poll hostage on the way back:
            // the loop returns early for as long as ChargingKey is set, so re-entering War
            // with one still held would refuse every other spell until that key is pressed
            // and let go again.
            if (!string.IsNullOrEmpty(_spellCaster.ChargingKey))
                _spellCaster.CancelCharge();
        }

        private void StopLeftHeldBeam()
        {
            if (_leftHeldBeam != null) _leftHeldBeam.Stop();
            _leftHeldBeam = null;
        }

        private void CastHeldPrimary(string key, bool ignoreManaCost = false)
        {
            if (_spellCaster == null) return;

            var spell = _spellCaster.GetSpellByKey(key);
            if (spell != null && spell.type == SpellType.Beam)
            {
                var beam = _spellCaster.GetComponent<LaserBeamController>();
                if (beam != null)
                {
                    beam.Refresh();
                }
                else if (_spellCaster.TryCastByKey(key, _facingDirection, ignoreManaCost))
                {
                    // Remembered so releasing left click stops only the beam left click
                    // started. There is one controller per caster, so a blind Stop() on
                    // release would also kill a beam the player is channelling on middle
                    // click — releasing the left button would cut the laser short.
                    _leftHeldBeam = _spellCaster.GetComponent<LaserBeamController>();
                }
                TriggerCastAnimation(key);
                return;
            }

            if (_spellCaster.TryCastByKey(key, _facingDirection, ignoreManaCost))
                TriggerCastAnimation(key);
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
                //
                // An OPEN CAST WINDOW is the fourth owner. A spell may name a locomotion
                // state — that is how the idle, walk and run animations are reachable at all —
                // and without this guard the very next frame would overwrite it with whatever
                // the player's movement implies, so the animation would never render for even
                // one frame. Normal casts are unaffected: they enter Cast or Attack, which
                // this branch never matched anyway.
                if (!IsCastAnimWindowOpen &&
                    (currentState == DirectionalAnimator.AnimState.Idle ||
                     currentState == DirectionalAnimator.AnimState.Walk ||
                     currentState == DirectionalAnimator.AnimState.Chase))
                {
                    _animator.SetLocomotionState(ResolveLocomotionAnimState(), dir, LocomotionReversed);
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
        private void TriggerCastAnimation(string spellKey = null)
        {
            if (_animator == null) return;
            var dir = _animator.ResolveDirectionFromVector(_facingDirection);
            bool isRegularSlash = UsesAttackAnimation(spellKey);
            var state = ResolveCastAnimState(spellKey, isRegularSlash);

            // A channelled spell re-enters this EVERY FRAME while the trigger is held — the
            // laser beam does, and so does any Beam-typed primary. Advancing the rotation
            // there hands SetState a different variant sixty times a second, and a changed
            // variant counts as a state change, so the pose restarted at frame 0 on every
            // one of them: the character flickered through all five casting animations
            // instead of playing one. Reuse the variant already on screen for as long as
            // this same cast's window is open.
            bool sameCastStillPlaying = _animator.CurrentState == state
                                        && _castAnimEndTime > 0f
                                        && Time.time < _castAnimEndTime
                                        && string.Equals(spellKey, _castAnimSpellKey,
                                            System.StringComparison.OrdinalIgnoreCase);
            int variant = sameCastStillPlaying
                ? _animator.ActiveVariant
                : ResolveCastVariant(state, spellKey);

            // Acknowledge the cast. Gated on the SAME flag the variant is, and that gate is the
            // whole reason this line is here rather than at the call sites: a channelled spell
            // re-enters this method every frame while the trigger is held, so an ungated
            // notification would hold every reaction lit for the length of a beam instead of
            // marking the moment it started.
            if (!sameCastStillPlaying) NotifyPlayerActed();

            // An authored timeline takes over the frames, and only on a FRESH cast: reinstalling
            // it on every frame of a held channel would restart the plan sixty times a second,
            // which is the same failure the variant reuse above exists to prevent.
            if (!sameCastStillPlaying && TryPlayCastTimeline(state, dir, variant, spellKey))
            {
                // PlayTimeline poses the animator itself.
            }
            else
            {
                _animator.SetState(state, dir, variant, ShouldPlayCastReversed());
            }
            // The same spell cast AGAIN while its pose is up — a held fireball, a beam refreshed
            // every frame. A variant with an authored repeat stretch holds the gesture at its
            // climax instead of cycling back through the rest pose between shots. Only a
            // repetition arms it: a single cast still plays the whole animation.
            if (sameCastStillPlaying)
                _animator.SustainRepeat(ResolveRepeatHold(spellKey, state, variant));

            _castAnimSpellKey = spellKey;
            // Remembered so the revert below can hand back control from WHATEVER state this
            // cast entered, not just from the three it used to be able to reach.
            _castAnimState = state;

            // Measured, not assumed, and measured AFTER SetState has turned the animator to
            // `dir`: GetStateLength reports the frame count of the CURRENT direction, so
            // asking before the turn sizes the animation against whichever way the character
            // happened to already be facing. The constants stay as a FLOOR rather than being
            // replaced, so a character whose cast art is a single pose is paced exactly as
            // before; what changes is that an eight-frame spellcast now runs all eight frames
            // instead of being cut at frame three by a 0.35 s deadline.
            float floor = isRegularSlash
                ? REGULAR_SLASH_ANIMATION_DURATION
                : CAST_ANIMATION_DURATION;
            // The window has to cover the CAST, not just the art.
            //
            // It used to be sized from the animation alone, which was invisible while every
            // shipped spell's wind-up was a rounding error: the longest prepareDuration in the
            // whole catalogue was 0.5 s and most were 0.1. Author a real one — a 1.2 s summon,
            // a 0.9 s war cry — and the animation runs out first, locomotion takes the animator
            // back mid-charge, and the character walks away while the spell is still counting
            // down and then fires it from wherever they got to. The spell's own phases are the
            // floor now; the art is what may be LONGER than them.
            float castPhases = ResolveCastPhaseDuration(spellKey);
            _castPhaseEndTime = Time.time + castPhases;
            _castAnimEndTime = Time.time + Mathf.Max(
                Mathf.Max(floor, castPhases),
                _animator.GetStateLength(state, variant));

            // A stow commits on the cast frame but its ART is deferred to the END of the
            // sheathe — the sword has to still be in hand for the animation to be putting it
            // away. This window is the only measurement of how long that takes: it depends on
            // the variant the animator just resolved and on that variant's own speed
            // multiplier, neither of which the executor can see. A no-op unless a stow was
            // committed THIS frame, so an unrelated spell cast mid-sheathe cannot push the
            // swap out by its own window.
            if (_loadouts != null && _loadouts.StowPending && _loadouts.SwappedThisFrame)
                _loadouts.ScheduleStow(_castAnimEndTime - Time.time);
        }

        /// <summary>
        /// Which animation state <paramref name="spellKey"/> plays.
        ///
        /// <c>SpellDefinition.animState</c> wins when the spell names one. That is the only
        /// way a spell can reach idle, walk, chase, damage, death or recover — states that
        /// locomotion and the damage and death flows own, so nothing that casts ever entered
        /// them. Before this the state came from <c>usesAttackAnimation</c> alone, so a spell
        /// asking for `death` silently played CAST and, having no cast variant reserved, took
        /// whatever the generic rotation handed it.
        ///
        /// Two things make that safe and both live elsewhere in this file, because entering a
        /// state is only half of it: <see cref="TickCastAnimRevert"/> now hands control back
        /// from whatever state was entered (a state locomotion refuses to override and
        /// nothing reverts is a soft lock — see <c>AnimState.Recover</c>'s own doc), and the
        /// locomotion override holds off while a cast window is open, or a spell asking for
        /// Idle/Walk/Chase would be overwritten on the very next frame and never render.
        /// </summary>
        private DirectionalAnimator.AnimState ResolveCastAnimState(string spellKey, bool attackRouted)
        {
            SpellDefinition spell = _spellCaster != null ? _spellCaster.GetSpellByKey(spellKey) : null;
            if (spell != null && !string.IsNullOrEmpty(spell.animState) &&
                TryParseAnimState(spell.animState, out var named))
                return named;

            return attackRouted
                ? DirectionalAnimator.AnimState.Attack
                : DirectionalAnimator.AnimState.Cast;
        }

        /// <summary>
        /// The manifest's state names, which is the vocabulary a designer sees everywhere
        /// else. Kept as a string on <see cref="SpellDefinition"/> because <c>AnimState</c>
        /// lives in <c>Valkur.Gameplay</c> and <c>Valkur.Data</c> may not reference it — the
        /// same constraint <c>LoadoutStateSheets.state</c> answers the same way.
        /// </summary>
        internal static bool TryParseAnimState(string name, out DirectionalAnimator.AnimState state)
        {
            switch ((name ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "idle":    state = DirectionalAnimator.AnimState.Idle;    return true;
                case "walk":    state = DirectionalAnimator.AnimState.Walk;    return true;
                case "chase":   state = DirectionalAnimator.AnimState.Chase;   return true;
                case "cast":    state = DirectionalAnimator.AnimState.Cast;    return true;
                case "attack":  state = DirectionalAnimator.AnimState.Attack;  return true;
                case "damage":  state = DirectionalAnimator.AnimState.Damage;  return true;
                case "death":   state = DirectionalAnimator.AnimState.Death;   return true;
                case "recover": state = DirectionalAnimator.AnimState.Recover; return true;
                default:        state = DirectionalAnimator.AnimState.Cast;    return false;
            }
        }

        /// <summary>
        /// Whether <paramref name="spellKey"/> plays through the ATTACK animation rather than
        /// the CAST one — a swing instead of a conjuring.
        ///
        /// This used to be a hard-coded comparison against <c>slash_regular</c>, which was
        /// true of exactly one spell and made every other swing-shaped spell unable to reach
        /// the attack animations at all. On the dwarf that showed up as `punch` and `kick`
        /// being UNREACHABLE: nothing but the regular slash ever entered Attack, and the
        /// regular slash is reserved for `armed_slash`, so `NextVariant(Attack)` was never
        /// called and two authored animations rendered no frame in the whole game.
        ///
        /// The key comparison survives as a fallback for the case where the spell cannot be
        /// resolved — a caster that has not registered yet, or a synthetic cast from a
        /// preview — so the historical behaviour is what a missing lookup falls back to
        /// rather than something new.
        /// </summary>
        /// <summary>
        /// True while a spell in flight forbids movement.
        ///
        /// <para>Asked of the CASTER rather than of the animation window: the window is sized by
        /// art and can outlast the cast, and planting a character for the tail of an animation
        /// would be a different rule than the one the field is named after.</para>
        /// </summary>
        private bool IsPlantedByCast()
        {
            if (_spellCaster == null) return false;

            var phase = _spellCaster.CurrentPhase;
            if (phase != SpellCaster.CastPhase.Prepare && phase != SpellCaster.CastPhase.Channel)
                return false;

            var spell = _spellCaster.CurrentSpell;
            return spell != null && !spell.allowMovement;
        }

        /// <summary>
        /// Cancels a spell still winding up when its caster is hit, for the spells whose data
        /// says so. The other half of the pair above, and dead for the same reason until
        /// wind-ups were long enough to be interrupted at all.
        ///
        /// <para>Subscribed to <c>OnDamagedBy</c> rather than to <c>OnHpChanged</c> so a heal
        /// cannot interrupt a cast, and so a blow that armour reduced to nothing still counts —
        /// being hit is the event, not losing health.</para>
        /// </summary>
        private void HandleCastInterruption(int amount, GameObject attacker)
        {
            if (_spellCaster == null) return;
            if (_spellCaster.CurrentPhase != SpellCaster.CastPhase.Prepare) return;

            var spell = _spellCaster.CurrentSpell;
            if (spell == null || !spell.interruptible) return;

            if (_spellCaster.CancelPreparingCast())
            {
                // The animation window is what holds locomotion off; a cancelled cast has to
                // hand it straight back or the character stands in a pose they are no longer
                // performing.
                _castAnimEndTime = 0f;
                _castAnimSpellKey = null;
            }
        }

        /// <summary>
        /// Installs the variant's authored timeline, resolved against THIS spell's phases.
        ///
        /// <para>This is the seam where the two clocks finally meet. The spell owns the absolute
        /// times — <c>prepareDuration</c> is when it fires — and the timeline owns which frame is
        /// on screen at each of them; <see cref="CastTimelineResolver"/> turns the pair into a
        /// flat list of frames and seconds. A variant with no timeline returns false and the
        /// caller poses the animator exactly as it always did, which is every animation in the
        /// game until somebody draws one.</para>
        /// </summary>
        private bool TryPlayCastTimeline(DirectionalAnimator.AnimState state,
                                         DirectionalAnimator.Direction dir,
                                         int variant, string spellKey)
        {
            if (_animator == null || variant < 0) return false;

            AnimationTimeline timeline = _animator.TimelineFor(state, variant);
            if (timeline == null || !timeline.HasSteps) return false;

            SpellDefinition spell = _spellCaster != null ? _spellCaster.GetSpellByKey(spellKey) : null;
            float prepare = spell != null ? Mathf.Max(0f, spell.prepareDuration) : 0f;
            float channel = spell != null ? Mathf.Max(0f, spell.channelDuration) : 0f;

            var steps = CastTimelineResolver.Resolve(timeline, prepare, channel);
            if (steps.Count == 0) return false;

            _animator.PlayTimeline(state, dir, variant, steps, ShouldPlayCastReversed());
            return true;
        }

        /// <summary>
        /// How long the spell itself occupies the caster: wind-up plus channel.
        ///
        /// <para>Deliberately NOT including the cooldown. A cooldown is when the spell may be
        /// cast AGAIN — <c>war_cry</c> authors twenty seconds of it — and holding the casting
        /// pose for that long would freeze the character out of their own turn. Prepare and
        /// channel are the phases during which the cast is happening.</para>
        ///
        /// <para>Zero for an unknown key, which is the honest answer for the paths that pass
        /// none (the slash, the dash and the beam refresh) and leaves the historical floor in
        /// charge exactly as before.</para>
        /// </summary>
        /// <summary>
        /// How long one repetition keeps a repeat stretch armed: the spell's own cadence plus one
        /// frame of slack, so a steady stream of casts never lets the tail start between two of
        /// them.
        ///
        /// <para>A beam's cadence is zero — it is refreshed every frame it is held — so its
        /// stretch lapses one frame after the button is let go. A spell slower than
        /// <see cref="REPEAT_MAX_CADENCE"/> is not armed at all: a repetition that far apart is
        /// two casts, and the tail playing between them is correct. Its re-cast still jumps
        /// back from the tail into the stretch.</para>
        /// </summary>
        private float ResolveRepeatHold(string spellKey, DirectionalAnimator.AnimState state, int variant)
        {
            SpellDefinition spell = _spellCaster != null ? _spellCaster.GetSpellByKey(spellKey) : null;
            float cadence = spell == null || spell.type == SpellType.Beam
                ? 0f
                : Mathf.Max(0f, spell.prepareDuration) + Mathf.Max(0f, spell.channelDuration)
                  + Mathf.Max(0f, spell.cooldownDuration);
            if (cadence > REPEAT_MAX_CADENCE) return 0f;
            return cadence + _animator.FrameIntervalFor(state, variant);
        }

        private float ResolveCastPhaseDuration(string spellKey)
        {
            SpellDefinition spell = _spellCaster != null ? _spellCaster.GetSpellByKey(spellKey) : null;
            if (spell == null) return 0f;
            return Mathf.Max(0f, spell.prepareDuration) + Mathf.Max(0f, spell.channelDuration);
        }

        private bool UsesAttackAnimation(string spellKey)
        {
            SpellDefinition spell = _spellCaster != null ? _spellCaster.GetSpellByKey(spellKey) : null;
            if (spell != null) return spell.usesAttackAnimation;

            return string.Equals(spellKey, RegularSlashAttack.SpellKey,
                System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether the cast that just fired should run its frames back to front.
        ///
        /// True for exactly one case today: a loadout swap that STOWED. The dwarf's sheathe
        /// is his draw run backwards — one sheet, one motion, read either way — and playing
        /// the draw forwards to put a weapon away reads as drawing it a second time.
        ///
        /// Asked of the loadout controller rather than keyed off the spell, because the spell
        /// is the same one in both directions: <c>weapon_toggle</c> cannot tell you which way
        /// it went, and only the thing that performed the swap can. The one-frame window is
        /// what makes that safe — the executor runs inside <c>TryCastByKey</c> and this runs
        /// immediately after it, in the same frame.
        /// </summary>
        private bool ShouldPlayCastReversed()
        {
            if (_loadouts == null)
                _loadouts = GetComponent<PlayerLoadoutController>();
            return _loadouts != null && _loadouts.SwappedThisFrame && _loadouts.LastSwapStowed;
        }

        /// <summary>
        /// The variant this cast should play: the one reserved for <paramref name="spellKey"/>
        /// if a <c>CastVariant</c> claims it, otherwise the next step of the generic rotation.
        ///
        /// The reservation is authored on the character, not on the spell, because the poses
        /// are the character's: <c>spell_3</c> means a different animation on the dwarf than
        /// it would on the elven, and a spell that names an index would be asserting something
        /// about art it has never seen.
        /// </summary>
        private int ResolveCastVariant(DirectionalAnimator.AnimState state, string spellKey)
        {
            int reserved = _animator.VariantForSpell(state, spellKey);
            return reserved >= 0 ? reserved : NextVariant(state);
        }

        /// <summary>
        /// Advances this state's animation variant one step and returns the new index, or -1
        /// when the character carries only the single set.
        ///
        /// Before this, a PLAYER never picked a variant at all: only <c>FSMMonsterBrain</c>
        /// set one, through the monster FSM's <c>AttackState</c>. The two-argument
        /// <c>SetState</c> reuses whatever index is already active, and for a player that was
        /// -1 forever — so every alternative animation authored on a PlayerDefinition was
        /// dead data that could not render. The elven character ships three punches and three
        /// spellcasts; the dwarf four unarmed attacks; the barbarian two axe swings.
        ///
        /// Rotating rather than randomising, because a cycle is what reads as a combo: a
        /// random pick repeats the same swing back to back about one time in N and looks like
        /// the animation failed to change.
        /// </summary>
        private int NextVariant(DirectionalAnimator.AnimState state)
        {
            int count = _animator.VariantCount(state);
            if (count <= 0) return -1;

            int index = (int)state;
            if (_nextVariantByState == null ||
                _nextVariantByState.Length <= index)
            {
                System.Array.Resize(ref _nextVariantByState,
                    System.Enum.GetValues(typeof(DirectionalAnimator.AnimState)).Length);
            }

            // A variant a spell has reserved is out of the rotation, so the pose drawn for
            // that one spell never turns up under another. The cursor still ADVANCES past it
            // — skipping without advancing would stall on a reserved slot forever.
            for (int step = 0; step < count; step++)
            {
                int variant = _nextVariantByState[index] % count;
                _nextVariantByState[index] = (variant + 1) % count;
                if (!_animator.IsVariantReserved(state, variant))
                    return variant;
            }

            // Every variant is spoken for. The base set is the honest answer: it is the one
            // animation no spell has claimed.
            return -1;
        }

        /// <summary>
        /// Plays the "getting back up" animation, holding locomotion off for its duration.
        /// Called by <c>DeathSequenceController.ReviveRoutine</c> once the player is out of
        /// spirit form. Safe to call on a character with no recover art —
        /// <c>DirectionalAnimator.GetSpriteSet</c> falls Recover back to idle.
        /// </summary>
        public void PlayRecoverAnimation(float duration)
        {
            if (_animator == null) return;
            var dir = _animator.ResolveDirectionFromVector(_facingDirection);
            _animator.SetState(DirectionalAnimator.AnimState.Recover, dir);
            _castAnimSpellKey = null;
            _castAnimState = DirectionalAnimator.AnimState.Recover;
            // Shares the cast timer on purpose: it is the one deadline TickCastAnimRevert
            // already checks every frame, so Recover cannot outlive its own animation even
            // if the coroutine that started it is killed by a scene change mid-rise.
            _castAnimEndTime = Time.time + Mathf.Max(0.05f, duration);
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
            // A repeat variant ends its own cast: once nothing sustains it and its tail has
            // played out, waiting for a window sized for the whole animation would hold the
            // last frame for however long the stretch was repeated. Never before the spell's
            // own phases are over, which the window still guarantees.
            bool repeatDone = _animator.RepeatTailFinished && Time.time >= _castPhaseEndTime;
            if (Time.time < _castAnimEndTime && !repeatDone) return;
            // Recover is in this list for the reason AnimState.Recover's doc gives: a state
            // that locomotion refuses to override and nothing reverts is a soft lock, and
            // the coroutine that entered Recover can be killed by a scene change.
            //
            // `_castAnimState` is what makes this general: a spell may now enter ANY state,
            // and one that locomotion refuses to override with nothing to revert it is a soft
            // lock — the character would hold the death pose forever. The three literals stay
            // as a safety net for windows opened before that field was tracked, and for
            // Recover, whose coroutine can be killed by a scene change mid-rise.
            if (_animator.CurrentState == _castAnimState ||
                _animator.CurrentState == DirectionalAnimator.AnimState.Cast ||
                _animator.CurrentState == DirectionalAnimator.AnimState.Attack ||
                _animator.CurrentState == DirectionalAnimator.AnimState.Recover)
            {
                var dir = _animator.ResolveDirectionFromVector(_facingDirection);
                _animator.SetLocomotionState(ResolveLocomotionAnimState(), dir, LocomotionReversed);
            }
            _castAnimEndTime = 0f;
            _castAnimSpellKey = null;
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

        private static bool IsPointerOverInteractiveUI()
        {
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }

        /// <summary>
        /// Traversal — the dash, and nothing else. Split out of
        /// <see cref="PollCombatActions"/> so it survives Peace stance: a dash is how the
        /// player gets out of the way, and a stance that takes it away is a stance that gets
        /// them killed. The method name always claimed the separation; the code did not have it.
        ///
        /// <para>Both guards below are duplicated from <see cref="PollCombatActions"/> ON
        /// PURPOSE, to keep War behaviour byte-identical. The pointer guard in particular is
        /// load-bearing: before the split, a dash pressed with the cursor over the HUD did
        /// nothing, because the mouse check sat above it. Whether a keyboard action should
        /// care where the cursor is, is a real question and a separate change — introducing
        /// the answer here would surface as "the dash changed" with nothing pointing back at
        /// the stance work.</para>
        /// </summary>
        private void PollTraversal()
        {
            bool isDashing = _dashAbility != null && _dashAbility.IsDashing;
            if (isDashing) return;

            if (IsPointerOverInteractiveUI()) return;

            // Dash. Every trigger it has is a BINDING on InputService.Gameplay.Dash — space,
            // right shift, and both Ctrl keys — and nothing about which keys those are lives
            // here any more.
            //
            // It used to: three of the four were literal Key/KeyCode pairs in this method,
            // written when there was no way to express "this action has four bindings" that
            // the legacy OR-gate could also see. The cost was that the dash was the one
            // gameplay verb a Controls editor could never fully move — rebinding the action
            // changed space and left the other three exactly where they were.
            //
            // Ctrl-as-dash stays safe for the same reason it always was: every IsCtrlHeld()
            // callsite outside this file lives in a runtime editor, and those gate gameplay
            // through InputBlocker.IsGameplayBlocked, which short-circuits this whole method.
            // So Ctrl+S and Ctrl-drag in an editor do not also fire a dash.
            //
            // Direction always tracks the MOUSE CURSOR — _facingDirection already points at
            // the mouse world position whenever the cursor is inside the viewport (see
            // PlayerFacingResolver), so every trigger reads as "dash toward the cursor". When
            // the cursor is offscreen the resolver falls back to movement direction.
            bool dashFired = InputBindingResolver.WasPerformedThisFrame(DashAction);

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
            if (dashFired && _spellCaster != null)
            {
                if (_spellCaster.TryCastByKey("dash", _facingDirection))
                    TriggerCastAnimation("dash");
            }
        }

        // Catalog descriptors for the three mouse-driven combat actions. Resolved per read
        // rather than cached in a static field: the lookup is a dictionary hit against an
        // immutable table, and a static holding catalog objects is exactly the shape the
        // Domain-Reload ratchet exists to refuse.
        private static InputActionDescriptor _descPrimaryAttack =>
            InputActionCatalog.Find(InputActionCatalog.MapGameplay, "PrimaryAttack");

        private static InputActionDescriptor _descSecondaryAttack =>
            InputActionCatalog.Find(InputActionCatalog.MapGameplay, "SecondaryAttack");

        private static InputActionDescriptor _descMiddleClick =>
            InputActionCatalog.Find(InputActionCatalog.MapGameplay, "MiddleClick");

        private void PollCombatActions()
        {
            bool isDashing = _dashAbility != null && _dashAbility.IsDashing;
            if (isDashing) return;

            // A click that lands on interactive UI belongs to the UI, not to the
            // world: without this, double-clicking the HUD ability row to open the
            // character sheet would also throw two fireballs. Decorative HUD
            // graphics set raycastTarget = false, and panels that hide themselves
            // clear blocksRaycasts, so only live UI blocks here.
            if (IsPointerOverInteractiveUI()) return;

            // Primary attack → fireball (spell slot 0). Python parity: M_LEFT → fireball.
            // IsPressed allows hold-to-fire; SpellCaster cooldown (0.4 s) gates the rate.
            //
            // Read through InputBindingResolver rather than MouseInputManager's left-button
            // helper. Both OR the two backends, and only one of them asks the ACTION what it
            // is bound to — which is what makes the button rebindable at all. The literal
            // helper call hardcoded "the primary attack is the left mouse button" in the one
            // place a Controls editor cannot reach.
            //
            // The spell key is resolved per frame rather than hardcoded: a runtime editor that
            // implements IChoosesPrimaryCastSpell redirects this click to whatever it has
            // selected, so the Spells Editor can be used to try a spell out in the world. With
            // no such editor open the resolver returns "fireball" and this is unchanged.
            var primaryAction = PrimaryAttackAction;
            if (InputContextPolicy.IsLive(_descPrimaryAttack) &&
                InputBindingResolver.IsPressed(primaryAction))
                CastHeldPrimary(ResolvePrimaryCastKey());

            // Releasing the primary ends a channelled beam. Deliberately NOT gated on the
            // stance mask: an ending must arrive even for an action that has just stopped
            // being live, which is the same reason OnStanceChanged cuts the three held things
            // off the TRANSITION rather than beside the gate.
            if (InputBindingResolver.WasReleasedThisFrame(primaryAction))
                StopLeftHeldBeam();

            // Secondary attack → slash spell. Python parity: M_RIGHT → slash.
            if (InputContextPolicy.IsLive(_descSecondaryAttack) &&
                InputBindingResolver.WasPerformedThisFrame(SecondaryAttackAction))
            {
                if (_spellCaster != null && _spellCaster.TryCastByKey(SecondarySpellKey, _facingDirection))
                    TriggerCastAnimation(SecondarySpellKey);
            }

            // Middle click → laser beam (hold-to-channel)
            // Python parity: M_MIDDLE → laser_beam
            // First press starts the beam through the spell system; subsequent frames
            // refresh the controller directly (TryCastByKey is gated by cooldown so we
            // can't rely on it to keep the beam alive).
            var middleAction = MiddleClickAction;
            if (InputContextPolicy.IsLive(_descMiddleClick) &&
                InputBindingResolver.IsPressed(middleAction))
            {
                if (_spellCaster != null)
                {
                    var existingBeam = _spellCaster.GetComponent<LaserBeamController>();
                    if (existingBeam != null)
                        existingBeam.Refresh();
                    else
                        _spellCaster.TryCastByKey(MiddleSpellKey, _facingDirection);
                    // Beam is hold-to-channel — keep refreshing the cast animation
                    // each frame so the pose persists for as long as the player
                    // holds the trigger. The key is passed so the refresh is recognised
                    // as the SAME cast and does not re-roll the variant every frame.
                    TriggerCastAnimation(MiddleSpellKey);
                }
            }
            if (InputBindingResolver.WasReleasedThisFrame(middleAction))
            {
                var beam = _spellCaster != null ? _spellCaster.GetComponent<LaserBeamController>() : null;
                if (beam != null) beam.Stop();
            }

            // The War keyboard's spell slots — every grimoire spell, half of them Shift chords
            // that InputBindingResolver keeps apart from the bare key under them (see
            // InputChord and tools/input/build_war_keyboard.py). The (action, descriptor) pairs come from
            // InputService.Gameplay.EnumerateSpellBindings, which reads InputActionCatalog —
            // single source of truth for the slot list, the spellKey each casts, and the
            // stance each is live in. The legacy KeyCode column that used to ride along here
            // is gone: it was a literal that did not move when the slot was rebound.
            var gp = InputService.Instance?.Gameplay;
            if (_spellCaster != null && gp != null)
            {
                // A charge in progress owns the whole poll: nothing else may be cast while
                // the key is down, and letting go is what fires it. Checked BEFORE the loop
                // so a player holding one spell cannot start another with a second key.
                string charging = _spellCaster.ChargingKey;
                if (!string.IsNullOrEmpty(charging))
                {
                    bool stillHeld = false;
                    foreach (var (action, descriptor) in gp.EnumerateSpellBindings())
                    {
                        if (descriptor.PayloadKey != charging) continue;
                        stillHeld = InputBindingResolver.IsPressed(action);
                        break;
                    }

                    if (!stillHeld)
                    {
                        if (_spellCaster.ReleaseCharge(_facingDirection))
                            TriggerCastAnimation(charging);
                    }
                    return;   // charging or releasing, either way the frame is spent
                }

                foreach (var (action, descriptor) in gp.EnumerateSpellBindings())
                {
                    // Per-slot stance mask on top of the coarse gate above. The gate answers
                    // "is any combat live"; this answers "is THIS slot live", which is what
                    // lets a player silence one spell without leaving War.
                    if (!InputContextPolicy.IsLive(descriptor)) continue;

                    // Both backends, and the legacy half is derived from the slot's own live
                    // binding — so a rebind moves it. It also goes through
                    // KeyboardInputManager rather than raw UnityEngine.Input: disabling the
                    // Gameplay map silences the action, nothing silenced the raw fallback, and
                    // every letter typed into the chat that happened to be bound cast a spell.
                    if (InputBindingResolver.WasPerformedThisFrame(action))
                    {
                        string spellKey = descriptor.PayloadKey;
                        // A chargeable spell starts building instead of firing. The animation
                        // is deliberately NOT triggered here: the cast pose belongs to the
                        // release, and playing it on the press would show the character
                        // finishing a spell they have not thrown yet.
                        if (_spellCaster.BeginCharge(spellKey, _facingDirection))
                            break;

                        if (_spellCaster.TryCastByKey(spellKey, _facingDirection))
                            TriggerCastAnimation(spellKey);
                        break; // only one spell per frame
                    }
                }
            }
        }

        /// <summary>
        /// Casts a spell because the player CLICKED it on the action bar, through every gate its
        /// key goes through: dead, spirit, stunned, an open editor, a blocked input (chat, console),
        /// the Peace posture, the per-slot stance mask, a dash in flight, a charge in progress.
        ///
        /// <para>The bar used to call <c>SpellCaster.TryCast</c> itself and so cast damage spells
        /// in Peace — the one guarantee the posture exists for, broken by a HUD button. Keeping
        /// the decision here, beside <see cref="PollCombatActions"/>, is what keeps a click and a
        /// key press from ever disagreeing about whether a cast is allowed.</para>
        /// </summary>
        public bool TryCastFromHud(string spellKey)
        {
            if (string.IsNullOrEmpty(spellKey) || _spellCaster == null) return false;
            if (_health == null || _health.IsDead || IsSpirit) return false;
            if (_statusEffects != null && _statusEffects.IsStunned) return false;
            if (InputBlocker.IsGameplayBlocked) return false;
            if (IsGameplayInputSuspended() || IsPlayerCombatSuspended()) return false;
            if (PlayerStance.IsPeace) return false;
            if (_dashAbility != null && _dashAbility.IsDashing) return false;
            if (!string.IsNullOrEmpty(_spellCaster.ChargingKey)) return false;

            InputActionDescriptor descriptor = null;
            foreach (var d in InputActionCatalog.Spells())
                if (d.PayloadKey == spellKey) { descriptor = d; break; }
            if (descriptor == null || !InputContextPolicy.IsLive(descriptor)) return false;

            if (!_spellCaster.TryCastByKey(spellKey, _facingDirection)) return false;
            TriggerCastAnimation(spellKey);
            return true;
        }

        public void SetMoveSpeed(float speed)
        {
            moveSpeed = speed;
        }

        /// <summary>
        /// The speed actually applied this frame: the tuned <c>moveSpeed</c>, multiplied while in
        /// spirit form by <c>DeathTuning.spiritSpeedMultiplier</c>.
        ///
        /// <para>It is a multiplier rather than a second authored speed because the base one is
        /// already composed from the class, the level curve, talents, equipment and buffs
        /// (<c>PlayerStats</c> pushes it through <see cref="SetMoveSpeed"/>): a flat spirit speed
        /// would make a fast character and a slow one identical the moment they died, which throws
        /// away the one thing the player has spent the run building.</para>
        ///
        /// <para>Read at the point of use rather than cached on the stance transition, so an
        /// author dragging the slider in the Death Editor sees it on the next step instead of on
        /// the next death.</para>
        /// </summary>
        private float CurrentMoveSpeed =>
            IsSpirit ? moveSpeed * Mathf.Max(0.05f, Valkur.Data.DeathTuning.Active.spiritSpeedMultiplier)
                     : moveSpeed * LocomotionSpeedMultiplier;
    }
}
