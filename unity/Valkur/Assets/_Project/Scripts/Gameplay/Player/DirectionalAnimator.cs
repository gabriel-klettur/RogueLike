using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// 8-directional sprite animator matching Python's Animator component.
    /// Handles direction resolution from vectors, frame cycling with interval,
    /// idle hold behavior, and walk skip-first-frame behavior.
    /// </summary>
    public partial class DirectionalAnimator : MonoBehaviour
    {
        /// <summary>
        /// The 8 cardinal/intercardinal directions matching Python's naming.
        /// </summary>
        public enum Direction
        {
            South,      // "down" / "s"
            SouthEast,  // "down_right" / "se"
            East,       // "right" / "e"
            NorthEast,  // "up_right" / "ne"
            North,      // "up" / "n"
            NorthWest,  // "up_left" / "nw"
            West,       // "left" / "w"
            SouthWest   // "down_left" / "sw"
        }

        /// <summary>
        /// Animation state matching Python's FSM-driven animation states.
        /// </summary>
        public enum AnimState
        {
            Idle,
            Walk,
            Chase,
            Cast,
            Attack,
            Damage,
            Death,

            /// <summary>
            /// Getting back up. Entered ONLY by <c>DeathSequenceController.ReviveRoutine</c>,
            /// which owns both the entry and the exit.
            ///
            /// This is the eighth value, and CLAUDE.md warns at length that adding one is how
            /// you get a state the player enters and never leaves — <c>PlayerController
            /// .Movement</c> overrides locomotion on an Idle/Walk/Chase whitelist and reverts
            /// on a Cast/Attack one, so a value missing from the second list is a soft lock.
            /// Recover is in that revert list, and unlike Cast/Attack it also carries a hard
            /// deadline in <c>TickCastAnimRevert</c>, because the system that entered it is a
            /// coroutine that a scene change can kill mid-flight.
            ///
            /// It exists rather than being folded into Damage because the two read as opposite
            /// things: Damage is a flinch that interrupts, Recover is a rise that resolves.
            /// </summary>
            Recover
        }

        [Serializable]
        public struct DirectionalSpriteSet
        {
            public Sprite[] south;
            public Sprite[] southEast;
            public Sprite[] east;
            public Sprite[] northEast;
            public Sprite[] north;
            public Sprite[] northWest;
            public Sprite[] west;
            public Sprite[] southWest;

            public Sprite[] GetFrames(Direction dir)
            {
                return dir switch
                {
                    Direction.South => south,
                    Direction.SouthEast => southEast,
                    Direction.East => east,
                    Direction.NorthEast => northEast,
                    Direction.North => north,
                    Direction.NorthWest => northWest,
                    Direction.West => west,
                    Direction.SouthWest => southWest,
                    _ => south
                };
            }
        }

        [Header("Animation Sets")]
        [SerializeField] private DirectionalSpriteSet idleSprites;
        [SerializeField] private DirectionalSpriteSet walkSprites;
        [SerializeField] private DirectionalSpriteSet chaseSprites;
        [SerializeField] private DirectionalSpriteSet castSprites;
        [SerializeField] private DirectionalSpriteSet attackSprites;
        [SerializeField] private DirectionalSpriteSet damageSprites;
        [SerializeField] private DirectionalSpriteSet deathSprites;
        // Installed by SetRecoverSprites rather than by the seven-argument SetSpriteSets,
        // so every existing caller of that method keeps compiling untouched.
        [SerializeField] private DirectionalSpriteSet recoverSprites;

        [Header("Timing")]
        [SerializeField] private float frameInterval = 0.15f;
        [SerializeField] private float idleHoldTime = 1.0f;

        [Header("References")]
        [SerializeField] private SpriteRenderer targetRenderer;

        private AnimState _currentState = AnimState.Idle;
        private Direction _currentDirection = Direction.South;
        private int _frameIndex;
        private float _frameTimer;
        private float _stateStartTime;
        private AnimState _prevState;
        private Direction _prevDirection;
        private bool _preferCardinalDirectionSampling;

        // Per-entity animation speed. Kept OFF the serialized frameInterval field so its
        // authored 0.15s value (identical across the whole bestiary today) stays the
        // single source of truth; this is a runtime multiplier applied on top of it. See
        // EntityAssetConfig.AnimationScaleConfig.animationSpeedMultiplier for the authoring
        // side and SetAnimationSpeedMultiplier below for the <=0 "unset" sentinel.
        private const float MinAnimationSpeedMultiplier = 0.01f;
        private float _animationSpeedMultiplier = 1f;

        // Alternative animations, per state. Deliberately NOT a serialized field per state
        // per variant: see EntityAssetConfig.AttackVariant for why the vocabulary lives in
        // data rather than in the enum.
        //
        // This started as an Attack-only array. It is indexed by AnimState now because the
        // elven character ships three spellcasting animations, and a second parallel
        // cast-only array would have paid the same positional tax a second time — the tax
        // AttackVariant's own doc-comment exists to complain about. A state with no entry
        // resolves to its single set exactly as before.
        private DirectionalSpriteSet[][] _variantsByState;
        /// <summary>Per state, per variant, the spell keys that variant is reserved for.
        /// Index-aligned with <see cref="_variantsByState"/> — installed in the same call,
        /// because the binder DROPS variants that resolved to no frames and an index built
        /// from the unfiltered authored list would point one variant off.</summary>
        private string[][][] _variantSpellKeysByState;
        /// <summary>Per state, per variant: playback speed and whether the last frame holds.
        /// Index-aligned with <see cref="_variantsByState"/> for the same reason the spell
        /// keys are — installed in the same call, after the binder has dropped the empties.
        /// </summary>
        private VariantPacing[][] _variantPacingByState;

        /// <summary>How one variant is paced. A struct so an uninstalled table costs nothing
        /// and a missing row reads as the neutral 1x, non-holding default.</summary>
        public struct VariantPacing
        {
            public float SpeedMultiplier;
            public bool HoldLastFrame;

            public static VariantPacing Default => new VariantPacing { SpeedMultiplier = 1f };
        }
        private int _activeVariant = -1;

        public AnimState CurrentState => _currentState;
        public Direction CurrentDirection => _currentDirection;

        // Read-only accessors — used by the Spells Editor View panel to clone the
        // player's sprite sets onto the synthetic preview character so the user
        // sees the actual cast pose for the selected direction.
        public DirectionalSpriteSet IdleSprites   => idleSprites;
        public DirectionalSpriteSet WalkSprites   => walkSprites;
        public DirectionalSpriteSet ChaseSprites  => chaseSprites;
        public DirectionalSpriteSet CastSprites   => castSprites;
        public DirectionalSpriteSet AttackSprites => attackSprites;
        public DirectionalSpriteSet DamageSprites => damageSprites;
        public DirectionalSpriteSet DeathSprites  => deathSprites;
        public DirectionalSpriteSet RecoverSprites => recoverSprites;
        public bool PrefersCardinalDirectionSampling => _preferCardinalDirectionSampling;

        /// <summary>How many alternative animations this entity carries for one state. 0 = one.</summary>
        public int VariantCount(AnimState state)
        {
            DirectionalSpriteSet[] variants = VariantsFor(state);
            return variants?.Length ?? 0;
        }

        /// <summary>How many alternative attack animations this entity carries. 0 = one attack.</summary>
        public int AttackVariantCount => VariantCount(AnimState.Attack);

        /// <summary>Variant currently selected for the active state; -1 = the base set.</summary>
        public int ActiveVariant => _activeVariant;

        /// <summary>Kept for the monster path, which only ever varies its attack.</summary>
        public int ActiveAttackVariant => _activeVariant;

        /// <summary>
        /// One variant's sprite set, or the empty set when the index is out of range.
        /// Read-only, in the same spirit as <see cref="AttackSprites"/>: it lets a caller
        /// name the frames a variant should be showing without reaching into the array.
        /// </summary>
        public DirectionalSpriteSet VariantSet(AnimState state, int index)
        {
            DirectionalSpriteSet[] variants = VariantsFor(state);
            return variants != null && index >= 0 && index < variants.Length
                ? variants[index]
                : default;
        }

        public DirectionalSpriteSet AttackVariantSet(int index)
            => VariantSet(AnimState.Attack, index);

        private DirectionalSpriteSet[] VariantsFor(AnimState state)
        {
            int i = (int)state;
            return _variantsByState != null && i >= 0 && i < _variantsByState.Length
                ? _variantsByState[i]
                : null;
        }

        /// <summary>
        /// Runtime assignment API for data-driven character definitions.
        /// </summary>
        public void SetSpriteSets(
            DirectionalSpriteSet idle,
            DirectionalSpriteSet walk,
            DirectionalSpriteSet chase,
            DirectionalSpriteSet cast,
            DirectionalSpriteSet attack,
            DirectionalSpriteSet damage,
            DirectionalSpriteSet death,
            bool preferCardinalDirectionSampling = false)
        {
            idleSprites = idle;
            walkSprites = walk;
            chaseSprites = chase;
            castSprites = cast;
            attackSprites = attack;
            damageSprites = damage;
            deathSprites = death;
            _preferCardinalDirectionSampling = preferCardinalDirectionSampling;

            _frameIndex = 0;
            _frameTimer = 0f;
            _stateStartTime = Time.time;
        }

        /// <summary>
        /// Installs the alternative attack animations. Additive to
        /// <see cref="SetSpriteSets"/> on purpose — every existing caller of that
        /// seven-argument method keeps compiling and behaving identically, and an entity
        /// that never calls this one resolves Attack to its single attack set as before.
        /// </summary>
        public void SetAttackVariants(IReadOnlyList<DirectionalSpriteSet> variants)
            => SetVariants(AnimState.Attack, variants);

        /// <summary>
        /// Installs the alternative animations for one state. Additive to
        /// <see cref="SetSpriteSets"/> on purpose — every existing caller of that
        /// seven-argument method keeps compiling and behaving identically, and a state that
        /// never gets variants resolves to its single set as before.
        /// </summary>
        public void SetVariants(AnimState state, IReadOnlyList<DirectionalSpriteSet> variants,
                                IReadOnlyList<IReadOnlyList<string>> variantSpellKeys = null,
                                IReadOnlyList<VariantPacing> variantPacing = null)
        {
            int stateCount = Enum.GetValues(typeof(AnimState)).Length;
            if (_variantsByState == null || _variantsByState.Length != stateCount)
                _variantsByState = new DirectionalSpriteSet[stateCount][];
            if (_variantSpellKeysByState == null || _variantSpellKeysByState.Length != stateCount)
                _variantSpellKeysByState = new string[stateCount][][];
            if (_variantPacingByState == null || _variantPacingByState.Length != stateCount)
                _variantPacingByState = new VariantPacing[stateCount][];

            int index = (int)state;
            if (index < 0 || index >= stateCount)
                return;

            if (variants == null || variants.Count == 0)
            {
                _variantsByState[index] = null;
                _variantSpellKeysByState[index] = null;
                _variantPacingByState[index] = null;
            }
            else
            {
                var copy = new DirectionalSpriteSet[variants.Count];
                for (int i = 0; i < variants.Count; i++)
                    copy[i] = variants[i];
                _variantsByState[index] = copy;

                _variantSpellKeysByState[index] = CopySpellKeys(variants.Count, variantSpellKeys);
                _variantPacingByState[index] = CopyPacing(variants.Count, variantPacing);
            }

            _activeVariant = -1;
        }

        /// <summary>
        /// Defensive copy of the reservation table, padded or truncated to the variant count
        /// so a caller that supplies a shorter list cannot make the two arrays disagree —
        /// every lookup below indexes both with the same integer.
        /// </summary>
        private static string[][] CopySpellKeys(int variantCount,
                                                IReadOnlyList<IReadOnlyList<string>> source)
        {
            if (source == null) return null;

            string[][] copy = null;
            for (int i = 0; i < variantCount && i < source.Count; i++)
            {
                IReadOnlyList<string> keys = source[i];
                if (keys == null || keys.Count == 0) continue;

                copy ??= new string[variantCount][];
                var row = new string[keys.Count];
                for (int k = 0; k < keys.Count; k++)
                    row[k] = keys[k];
                copy[i] = row;
            }
            return copy;
        }

        /// <summary>
        /// Defensive copy of the pacing table, padded to the variant count. A row left at
        /// its default is the neutral 1x non-holding pacing, so a caller may supply a shorter
        /// list or none at all and every variant still answers.
        /// </summary>
        private static VariantPacing[] CopyPacing(int variantCount,
                                                  IReadOnlyList<VariantPacing> source)
        {
            if (source == null) return null;

            var copy = new VariantPacing[variantCount];
            for (int i = 0; i < variantCount; i++)
            {
                copy[i] = i < source.Count && source[i].SpeedMultiplier > 0f
                    ? source[i]
                    : VariantPacing.Default;
            }
            return copy;
        }

        /// <summary>
        /// How <paramref name="index"/> of <paramref name="state"/> is paced, or the neutral
        /// default. Public so a caller sizing an action's window can ask rather than assume.
        /// </summary>
        public VariantPacing PacingOf(AnimState state, int index)
        {
            int i = (int)state;
            VariantPacing[] table = _variantPacingByState != null && i >= 0 &&
                                    i < _variantPacingByState.Length
                ? _variantPacingByState[i]
                : null;
            return table != null && index >= 0 && index < table.Length
                ? table[index]
                : VariantPacing.Default;
        }

        /// <summary>
        /// Copies every variant table from <paramref name="source"/> — the sets, the spell
        /// reservations and the pacing — plus the recover set.
        ///
        /// For a rig that mirrors a live character rather than being bound from a definition.
        /// The Spells Editor's preview is the case: it hand-copies the seven base sets, which
        /// is lossy in exactly the way that matters to it. Without the variants,
        /// <c>VariantForSpell</c> answers -1 for everything, so every spell previewed the
        /// character's BASE cast pose and the pinning of an animation to a spell was
        /// invisible in the one screen built for looking at spells.
        /// </summary>
        public void CopyVariantsFrom(DirectionalAnimator source)
        {
            if (source == null) return;

            int stateCount = Enum.GetValues(typeof(AnimState)).Length;
            for (int i = 0; i < stateCount; i++)
            {
                var state = (AnimState)i;
                DirectionalSpriteSet[] sets = source.VariantsFor(state);
                if (sets == null || sets.Length == 0)
                {
                    SetVariants(state, null);
                    continue;
                }

                var keys = new List<IReadOnlyList<string>>(sets.Length);
                var pacing = new List<VariantPacing>(sets.Length);
                for (int v = 0; v < sets.Length; v++)
                {
                    string[] row = source.SpellKeysFor(state) != null && v < source.SpellKeysFor(state).Length
                        ? source.SpellKeysFor(state)[v]
                        : null;
                    keys.Add(row);
                    pacing.Add(source.PacingOf(state, v));
                }
                SetVariants(state, sets, keys, pacing);
            }

            SetRecoverSprites(source.RecoverSprites);
            SetAnimationSpeedMultiplier(source.AnimationSpeedMultiplier);
        }

        /// <summary>
        /// Index of the variant reserved for <paramref name="spellKey"/>, or -1 when no
        /// variant claims it and the caller should fall back to its own rotation.
        ///
        /// First match wins: two variants claiming the same spell is an authoring mistake,
        /// and picking the earlier one deterministically beats alternating between them.
        /// </summary>
        public int VariantForSpell(AnimState state, string spellKey)
        {
            if (string.IsNullOrEmpty(spellKey)) return -1;

            string[][] table = SpellKeysFor(state);
            if (table == null) return -1;

            for (int i = 0; i < table.Length; i++)
            {
                string[] keys = table[i];
                if (keys == null) continue;
                for (int k = 0; k < keys.Length; k++)
                {
                    if (string.Equals(keys[k], spellKey, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// True when this variant is claimed by at least one spell, and therefore must NOT be
        /// handed out by a generic rotation — see <c>CastVariant.IsReservedForSpell</c>.
        /// </summary>
        public bool IsVariantReserved(AnimState state, int index)
        {
            string[][] table = SpellKeysFor(state);
            return table != null && index >= 0 && index < table.Length && table[index] != null;
        }

        private string[][] SpellKeysFor(AnimState state)
        {
            int i = (int)state;
            return _variantSpellKeysByState != null && i >= 0 && i < _variantSpellKeysByState.Length
                ? _variantSpellKeysByState[i]
                : null;
        }

        /// <summary>
        /// Installs the "getting back up" set. Separate from <see cref="SetSpriteSets"/> for
        /// the same reason <see cref="SetAttackVariants"/> is: widening that method to eight
        /// arguments would break every existing caller for a state most entities do not have.
        /// </summary>
        public void SetRecoverSprites(DirectionalSpriteSet recover)
        {
            recoverSprites = recover;
        }

        /// <summary>
        /// Per-entity playback speed set from <c>EntityAssetConfig.AnimationScaleConfig
        /// .animationSpeedMultiplier</c> via <c>EntityAnimationBinder.ApplyVisuals</c>.
        ///
        /// A value &lt;= 0 collapses to 1 (identity, i.e. today's flat 0.15s/frame for
        /// every monster). That is deliberate, not just a clamp: a struct field with no
        /// matching key in an asset serialized before this field existed deserializes to
        /// its CLR default, 0 — not the C# line's absent initializer — so every shipped
        /// monster keeps its exact current timing until an author sets this explicitly.
        /// </summary>
        public void SetAnimationSpeedMultiplier(float multiplier)
        {
            _animationSpeedMultiplier = multiplier <= 0f
                ? 1f
                : Mathf.Max(MinAnimationSpeedMultiplier, multiplier);
        }

        /// <summary>Read-only, for tests and inspection — the value <see cref="Update"/> and <see cref="GetStateLength"/> actually run at.</summary>
        public float AnimationSpeedMultiplier => _animationSpeedMultiplier;

        /// <summary>
        /// The authored <see cref="frameInterval"/> divided by <see cref="_animationSpeedMultiplier"/>.
        /// Every reader of the per-frame timing (the Update loop and GetStateLength) goes
        /// through this single accessor so the two can never disagree about how fast a
        /// state is actually playing.
        /// </summary>
        private float EffectiveFrameInterval => frameInterval / _animationSpeedMultiplier;

        /// <summary>
        /// The interval one frame of <paramref name="variant"/> of <paramref name="state"/>
        /// runs for — the entity's rate, divided again by the variant's own multiplier.
        ///
        /// Two multipliers rather than one because they answer different questions: the
        /// entity's says how fast THIS CREATURE moves, and is tuned once per monster; the
        /// variant's says how long THIS ANIMATION may take, and exists because an action can
        /// be shorter than the art drawn for it. The dash is the case that forced it — the
        /// body teleports in a single physics step and its wake lasts 0.14 s, against eight
        /// charge frames that read for 1.2 s at the normal rate.
        /// </summary>
        private float FrameIntervalFor(AnimState state, int variant)
        {
            float variantSpeed = PacingOf(state, variant).SpeedMultiplier;
            if (variantSpeed <= 0f) variantSpeed = 1f;
            return EffectiveFrameInterval / variantSpeed;
        }

        /// <summary>
        /// How long one full pass of a state's animation takes, in seconds, AT THIS
        /// ENTITY'S current animation speed.
        ///
        /// Exists because <c>AttackState</c> hardcodes its swing at windup + 0.3 s while
        /// every frame runs for <see cref="EffectiveFrameInterval"/> — an eight-frame swing
        /// needs 1.2 s at the default speed and was being cut at frame four, mid-arc.
        /// Returns 0 when the state has no frames, so a caller can take the larger of the
        /// two and never SHORTEN a swing that the rest of the bestiary depends on.
        /// </summary>
        public float GetStateLength(AnimState state, int attackVariant = -1)
        {
            Sprite[] frames = ResolveFrames(state, _currentDirection, attackVariant);
            return frames == null || frames.Length == 0
                ? 0f
                : frames.Length * FrameIntervalFor(state, attackVariant);
        }

        /// <summary>
        /// Replays the current state from frame 0 without changing state or direction.
        ///
        /// <see cref="SetState"/> early-returns when neither changed, and
        /// <c>AttackState</c>'s re-swing path resets only its own timers — so back-to-back
        /// swings at a player who never leaves melee range ride one free-running sprite
        /// loop, and the second swing starts wherever the first happened to be.
        /// </summary>
        public void RestartCurrentState()
        {
            _frameIndex = 0;
            _frameTimer = 0f;
            _stateStartTime = Time.time;
            AdvanceFrame();
        }

        private void Awake()
        {
            if (targetRenderer == null)
                targetRenderer = GetComponentInChildren<SpriteRenderer>();
            _prevState = _currentState;
            _prevDirection = _currentDirection;
            _stateStartTime = Time.time;
        }

        private void Update()
        {
            _frameTimer += Time.deltaTime;
            float interval = FrameIntervalFor(_currentState, _activeVariant);
            if (_frameTimer < interval) return;
            _frameTimer -= interval;

            AdvanceFrame();
        }

        /// <summary>
        /// Set animation state and direction.
        /// Resets frame counter only on state change — direction changes preserve the
        /// current frame index so walk/idle animations don't stutter when the mouse
        /// crosses an 8-direction sector boundary.
        /// Maps to Python's set_mapped_anim / Animator.current_state assignment.
        /// </summary>
        public void SetState(AnimState state, Direction direction)
            => SetState(state, direction, _activeVariant);

        /// <summary>
        /// True while the active playback is running its frames back to front. Read by
        /// <c>AdvanceFrame</c> and <c>RefreshCurrentFrame</c>, which map the cursor rather
        /// than counting down — so the loop, the hold and the frame clock all keep working
        /// unchanged and only the sprite they land on differs.
        /// </summary>
        private bool _playReversed;

        /// <summary>Which frame the cursor is pointing at, once playback direction is
        /// applied. Forward playback is the identity.</summary>
        private int FrameAt(int cursor, int length)
        {
            int clamped = Mathf.Clamp(cursor, 0, length - 1);
            return _playReversed ? length - 1 - clamped : clamped;
        }

        /// <summary>
        /// Same, choosing which attack animation plays. <paramref name="attackVariant"/> is
        /// an index into <see cref="SetAttackVariants"/>; -1 (or an out-of-range value)
        /// falls back to the single attack set.
        ///
        /// A CHANGED VARIANT counts as a state change. Without that the guard below sees
        /// Attack-to-Attack in the same direction, returns early, and the second kick
        /// silently keeps playing the first one's frames.
        /// </summary>
        public void SetState(AnimState state, Direction direction, int attackVariant)
            => SetState(state, direction, attackVariant, reversed: false);

        /// <summary>
        /// Same, choosing playback direction. <paramref name="reversed"/> plays the frames
        /// back to front, for a move that is literally the undo of another one: the dwarf's
        /// sheathe is his draw run backwards, and there is one sheet because there is one
        /// motion. Forward is the default, so every existing caller is unchanged.
        ///
        /// A CHANGED DIRECTION OF PLAY counts as a state change, for the same reason a
        /// changed variant does: drawing and then stowing is Cast-to-Cast on the same
        /// variant and the same facing, so without this the guard below returns early and
        /// the sheathe silently keeps playing the draw.
        /// </summary>
        public void SetState(AnimState state, Direction direction, int attackVariant, bool reversed)
        {
            bool stateChanged = state != _currentState;
            bool directionChanged = direction != _currentDirection;
            // Any state that carries variants, not just Attack: the elven character casts
            // three different ways, and without this a second cast in the same direction
            // with a different animation returns early and keeps playing the first one.
            bool variantChanged = VariantsFor(state) != null && attackVariant != _activeVariant;
            bool reversedChanged = reversed != _playReversed;

            _activeVariant = attackVariant;
            _playReversed = reversed;

            if (!stateChanged && !directionChanged && !variantChanged && !reversedChanged)
                return;

            if ((variantChanged || reversedChanged) && !stateChanged)
            {
                // Same state, different animation: the frame cursor has to go back to 0 or
                // the new variant starts mid-cycle. Handled here because the block below
                // only resets on a state change.
                _currentDirection = direction;
                _prevDirection = direction;
                _frameIndex = 0;
                _frameTimer = 0f;
                _stateStartTime = Time.time;
                AdvanceFrame();
                return;
            }

            _currentDirection = direction;
            _prevDirection = direction;

            if (stateChanged)
            {
                _currentState = state;
                _prevState = state;
                _frameIndex = 0;
                _frameTimer = 0f;
                _stateStartTime = Time.time;
                // Apply immediately so the new state is visible without frame-interval lag.
                AdvanceFrame();
            }
            else
            {
                // Direction-only change: show same frame index in new direction without
                // resetting the counter, preventing walk animation stutter.
                RefreshCurrentFrame();
            }
        }

        /// <summary>
        /// Applies the current frame from the new direction's sprite set without
        /// advancing <see cref="_frameIndex"/>. Called on direction-only changes.
        /// </summary>
        private void RefreshCurrentFrame()
        {
            // The active variant, not the default -1: this runs on a direction-only change,
            // which during an attack means the player is strafing. Resolving the BASE attack
            // set here flashes one frame of the sword swing into the middle of a kick every
            // time the facing sector changes, and the next AdvanceFrame tick hides it again.
            var spriteSet = GetSpriteSet(_currentState, _activeVariant);
            Sprite[] frames = spriteSet.GetFrames(_currentDirection);

            if (frames == null || frames.Length == 0)
            {
                // Mirror AdvanceFrame's death fallback so a direction-only change while
                // dead renders the corpse pose, not the idle pose.
                if (_currentState == AnimState.Death)
                    frames = FindFirstNonEmptyDirection(spriteSet);

                if (frames == null || frames.Length == 0)
                {
                    frames = idleSprites.GetFrames(_currentDirection);
                    if (frames == null || frames.Length == 0) return;
                }
            }

            if (frames.Length == 1)
            {
                ApplyFrame(frames[0]);
                return;
            }

            int idx = Mathf.Clamp(_frameIndex, 0, frames.Length - 1);
            // Walk/Chase skip frame 0 (standing pose).
            if ((_currentState == AnimState.Walk || _currentState == AnimState.Chase) && idx < 1)
                idx = 1;
            // Mapped like AdvanceFrame does, or turning mid-sheathe would jump the character
            // to the mirror-image frame of the one it is on — visible as the draw snapping
            // back to its start every time the facing sector changes.
            ApplyFrame(frames[FrameAt(idx, frames.Length)]);
        }

        /// <summary>
        /// Set direction from a movement/facing vector.
        /// Maps to Python's get_direction_name(dx, dy) using atan2.
        /// </summary>
        public void SetDirectionFromVector(Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.01f) return;
            _currentDirection = ResolveDirectionFromVector(dir);
        }

        public Direction ResolveDirectionFromVector(Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.01f)
                return _currentDirection;

            return _preferCardinalDirectionSampling
                ? VectorToPrimaryDirection(dir)
                : VectorToDirection(dir);
        }
    }
}
