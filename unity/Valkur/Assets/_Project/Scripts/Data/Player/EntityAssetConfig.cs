using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Directional sprite references for an animation state.
    /// Maps to Python's directional dict: {s, se, e, ne, n, nw, w, sw}.
    /// </summary>
    [Serializable]
    public struct DirectionalSprites
    {
        public Sprite south;
        public Sprite southEast;
        public Sprite east;
        public Sprite northEast;
        public Sprite north;
        public Sprite northWest;
        public Sprite west;
        public Sprite southWest;
    }

    /// <summary>
    /// Scale values per animation state.
    /// Maps to Python's sprites_data_set: {scale_idle, scale_walk, ...}.
    ///
    /// Only <see cref="scaleIdle"/> is read (<c>EntityAnimationBinder.ApplyEntityScale</c>).
    /// This struct used to also carry <c>scaleWalk/Chase/Cast/Attack/Damage/Death</c> — one
    /// float per Python's per-state scale table, none of them ever wired to a consumer.
    /// Deleted rather than wired: consuming them would mean re-applying
    /// <c>transform.localScale</c> on every FSM state transition, which lives in
    /// <c>Gameplay/Enemies/FSM/**</c> and would need reviewing against every authored value
    /// first — several assets set a LARGER scale on <c>scaleDeath</c> than
    /// <c>scaleIdle</c>, which would visibly resize a monster the instant it died, a
    /// behaviour change no test currently pins either way. The six deleted fields had zero
    /// runtime readers anywhere in the project (verified by project-wide search before
    /// deletion) and their authored values remain in git history and in the shipped
    /// <c>.asset</c> YAML — deleting the C# fields does not touch those files; Unity simply
    /// ignores the now-unmatched keys on load, so nothing is destroyed, only unreachable
    /// from code until someone re-adds the field.
    /// </summary>
    [Serializable]
    public struct AnimationScaleConfig
    {
        public float scaleIdle;
        // HDR enabled so designers can push channel values above 1.0 to overcome
        // the multiplicative nature of SpriteRenderer.color. Multiplying a brown
        // sprite by (1, 0.84, 0) flattens to "dark yellow-brown"; multiplying by
        // (2.5, 2.1, 0) clips back to (1, ~0.63, 0) which reads as vibrant yellow.
        [ColorUsage(true, true)]
        public Color tint;

        [Tooltip("Per-entity playback speed multiplier applied to DirectionalAnimator's " +
                 "frameInterval (0.15s/frame = 6.67fps today, identical for every monster " +
                 "in the game). 1 = unchanged. <=0 is treated as 1 by EntityAnimationBinder " +
                 "— that is the value every asset serialized before this field existed reads " +
                 "back as (a struct field with no matching YAML key deserializes to the CLR " +
                 "default, 0, not this line's absent initializer), so no shipped monster's " +
                 "timing changes until an author explicitly sets it. This is the ONLY way to " +
                 "retime a swing WITHOUT retiming its damage window: AttackState sizes its " +
                 "hit window off DirectionalAnimator.GetStateLength, which is frame COUNT × " +
                 "frameInterval, so shortening a swing by deleting frames also shortens (and " +
                 "re-times) the hit — see CLAUDE.md 'Retiming an attack animation retimes " +
                 "its DAMAGE'. 2 = twice as fast; 0.5 = half speed.")]
        [Min(0f)]
        public float animationSpeedMultiplier;
    }

    /// <summary>
    /// How a linear "Sprite Sheet Mode" frame list (<c>idleSheets</c>, <c>walkSheets</c>, …)
    /// is sliced into per-direction buckets.
    ///
    /// <c>EntityAnimationBinder</c> used to GUESS this purely from frame count —
    /// <c>count % 4 == 0 &amp;&amp; count / 8 &lt; 3 &amp;&amp; count / 4 &gt;= 3</c> — which cannot tell a
    /// genuine 16-frame 4x4 sheet from an 8x2 one, and silently assumes an undocumented
    /// South, West, East, North strip order (<c>DirectionalAnimator.TryBuildFourDirectionalSet</c>)
    /// with no way to opt out. <see cref="Auto"/> keeps that heuristic as the
    /// default-resolution path, so every asset authored before this field existed — which
    /// deserializes to <see cref="Auto"/> = 0, the CLR default for an unset enum — renders
    /// exactly as it did before this field was added.
    /// </summary>
    public enum EntitySheetDirectionLayout
    {
        /// <summary>Resolve via EntityAnimationBinder's historical frame-count heuristic.</summary>
        Auto = 0,

        /// <summary>Force the 8-contiguous-bucket layout regardless of frame count.</summary>
        EightDirectional,

        /// <summary>
        /// Force the 4-direction layout, strip order South, West, East, North
        /// (each intercardinal shares its nearest cardinal's frames).
        /// </summary>
        FourDirectional_S_W_E_N,
    }

    /// <summary>
    /// One alternative attack animation, beyond the single <c>attack</c> slot.
    ///
    /// A LIST, not three more slots. The seven animation states are enumerated
    /// positionally in four independent places — this class's own fields,
    /// <c>DirectionalAnimator</c>'s seven serialized sets plus its seven accessors and
    /// its seven-argument <c>SetSpriteSets</c>, the <c>GetSpriteSet</c> switch, and
    /// <c>EntityAnimationBinder</c>'s build-and-fallback chain. Adding an eighth state
    /// pays that tax four times over and again for the ninth; a list pays it once.
    ///
    /// It also keeps <c>AnimState</c> untouched, which matters more than it looks:
    /// <c>PlayerController.Movement</c> gates locomotion on an Idle/Walk/Chase whitelist
    /// and reverts on a Cast/Attack whitelist. A new enum value missing from the second
    /// list is entered and never left. A variant INDEX under the existing Attack state
    /// inherits both whitelists by construction.
    /// </summary>
    [Serializable]
    public class AttackVariant
    {
        [Tooltip("Identifier used in logs and by the selection rule below.")]
        public string key;

        [Header("Combat")]
        [Tooltip("Scales this entity's meleeDamage for this move. 1 = unchanged. " +
                 "knight_red shipped five visually distinct attacks — slash, shieldbash, " +
                 "punch, kick, jumpkick — that were mechanically identical because the " +
                 "variant carried no combat data at all.")]
        public float damageMultiplier = 1f;

        [Tooltip("Scales meleeRange for this move. 1 = unchanged. The DRAWN arc scales with " +
                 "it, so reach and its tell never disagree.")]
        public float rangeMultiplier = 1f;

        [Tooltip("Scales meleeCooldown after this move. 1 = unchanged. A heavy swing that " +
                 "hits harder should also leave a longer opening.")]
        public float cooldownMultiplier = 1f;

        [Tooltip("Relative odds of picking this move among those whose distance gate passes. " +
                 "0 = never chosen. All variants ship at 1, i.e. uniform, which is what the " +
                 "old Random.Range did.")]
        [Min(0)] public int weight = 1;

        [Tooltip("Closest distance at which this move may be chosen. 0 = no lower bound.")]
        [Min(0f)] public float minDistance;

        [Tooltip("Furthest distance at which this move may be chosen. 0 = no upper bound. " +
                 "This is what lets a jump kick close a gap while a punch stays a " +
                 "point-blank answer.")]
        [Min(0f)] public float maxDistance;

        /// <summary>
        /// True when this move is legal at <paramref name="distance"/>. An unset bound is
        /// not a bound — a variant authored with neither is available everywhere, which is
        /// how every shipped variant behaves today.
        /// </summary>
        public bool AllowedAt(float distance)
        {
            if (minDistance > 0f && distance < minDistance) return false;
            if (maxDistance > 0f && distance > maxDistance) return false;
            return true;
        }

        [Tooltip("Directional sprites for this variant. Takes precedence over sheets, " +
                 "exactly as the seven base slots do.")]
        public DirectionalSprites directional;

        [Tooltip("Linear frame list for this variant: eight contiguous per-direction " +
                 "buckets in the order S, SE, E, NE, N, NW, W, SW.")]
        public List<Sprite> sheets;

        [Tooltip("Spell keys that ALWAYS play this animation. Leave empty to stay in the " +
                 "generic per-swing rotation.")]
        public List<string> spellKeys = new List<string>();

        [Header("Pacing")]
        [Tooltip("Scales this variant's playback speed. 1 = the entity's normal frame rate. " +
                 "Above 1 plays faster, which is how an animation is fitted to an action " +
                 "shorter than itself.")]
        [Min(0.05f)] public float animationSpeedMultiplier = 1f;

        [Tooltip("Play the frames once and hold the last one, instead of looping. Use for a " +
                 "move that ENDS in a pose rather than returning to where it started.")]
        public bool holdLastFrame;


        /// <summary>
        /// True when this variant is spoken for by at least one spell — see
        /// <see cref="CastVariant.IsReservedForSpell"/>, which this mirrors.
        ///
        /// It exists on the ATTACK side too because <c>slash_regular</c> is the one slash
        /// that runs through <c>AnimState.Attack</c> rather than <c>AnimState.Cast</c>
        /// (<c>RegularSlashAttack</c> keeps its own authored implementation). Without a
        /// reservation here, "every slash draws the weapon" would be true of four slashes
        /// out of five and the fifth would swing a bare fist.
        /// </summary>
        public bool IsReservedForSpell => spellKeys != null && spellKeys.Count > 0;

        /// <summary>True when <paramref name="spellKey"/> is one this variant claims.</summary>
        public bool ClaimsSpell(string spellKey)
        {
            if (string.IsNullOrEmpty(spellKey) || spellKeys == null) return false;
            for (int i = 0; i < spellKeys.Count; i++)
            {
                if (string.Equals(spellKeys[i], spellKey, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// One alternative CAST animation, beyond the single <c>cast</c> slot.
    ///
    /// A separate class from <see cref="AttackVariant"/> rather than a reuse of it, for two
    /// reasons. Unity serializes a <c>List&lt;T&gt;</c> by value, so a shared base class with
    /// <c>AttackVariant</c> deriving from it would need <c>[SerializeReference]</c> and would
    /// change how every already-authored attack variant round-trips. And the fields would
    /// lie: <c>damageMultiplier</c>, <c>rangeMultiplier</c> and the distance gates are melee
    /// concepts, and a cast's damage comes from its <c>SpellDefinition</c>, not from the
    /// animation that plays over it.
    ///
    /// Selection is by index, exactly as for attacks — see <c>DirectionalAnimator
    /// .SetVariants</c>. Index 0 is what a picker falls back to.
    /// </summary>
    [Serializable]
    public class CastVariant
    {
        [Tooltip("Identifier used in logs and by the selection rule.")]
        public string key;

        [Tooltip("Directional sprites for this variant. Takes precedence over sheets, " +
                 "exactly as the seven base slots do.")]
        public DirectionalSprites directional;

        [Tooltip("Linear frame list for this variant: eight contiguous per-direction " +
                 "buckets in the order S, SE, E, NE, N, NW, W, SW.")]
        public List<Sprite> sheets;

        [Tooltip("Spell keys that ALWAYS play this animation — e.g. \"fireball\". Leave " +
                 "empty to stay in the generic per-cast rotation.")]
        public List<string> spellKeys = new List<string>();

        [Header("Pacing")]
        [Tooltip("Scales this variant's playback speed. 1 = the entity's normal frame rate. " +
                 "Above 1 plays faster, which is how an animation is fitted to an action " +
                 "shorter than itself.")]
        [Min(0.05f)] public float animationSpeedMultiplier = 1f;

        [Tooltip("Play the frames once and hold the last one, instead of looping. Use for a " +
                 "move that ENDS in a pose rather than returning to where it started.")]
        public bool holdLastFrame;


        /// <summary>
        /// True when this variant is spoken for by at least one spell.
        ///
        /// A reserved variant leaves the rotation <c>PlayerController.NextVariant</c> walks.
        /// Both halves of that are needed and they are not the same statement: claiming a
        /// spell is what makes the pose ALWAYS play for it, and leaving the rotation is what
        /// stops every OTHER spell borrowing a pose drawn for one particular thing. Without
        /// the second half a five-variant character shows the fireball wind-up on one cast in
        /// five of everything else, which reads as the animation picker being broken.
        /// </summary>
        public bool IsReservedForSpell => spellKeys != null && spellKeys.Count > 0;

        /// <summary>
        /// True when <paramref name="spellKey"/> is one this variant claims. Compared
        /// <see cref="StringComparison.OrdinalIgnoreCase"/> because a spell key is typed by
        /// hand in five places — the Inspector, the DevConsole, the spell asset, the HUD
        /// binding and here — and a casing slip would fail silently by falling back to the
        /// rotation, which looks exactly like the feature not being wired at all.
        /// </summary>
        public bool ClaimsSpell(string spellKey)
        {
            if (string.IsNullOrEmpty(spellKey) || spellKeys == null) return false;
            for (int i = 0; i < spellKeys.Count; i++)
            {
                if (string.Equals(spellKeys[i], spellKey, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// One alternative animation for a state that is neither Attack nor Cast — a second
    /// walk cycle, a third way to fall over.
    ///
    /// Those two states already have <see cref="AttackVariant"/> and
    /// <see cref="CastVariant"/>, and this is deliberately NOT a third copy of either.
    /// What separates it is that both of those are SELECTED BY AN ACTION: a swing picks
    /// its variant, a spell reserves one by key. A locomotion variant has no action to
    /// hang off — nothing "casts" a walk — so it carries no <c>spellKeys</c>, no distance
    /// gate and no combat multipliers, and it is chosen on ENTRY to the state instead
    /// (<c>DirectionalAnimator.SetState</c>'s two-argument overload).
    ///
    /// Giving it those fields anyway would be the authored-and-inert shape this project has
    /// paid for repeatedly: a <c>damageMultiplier</c> on a walk cycle round-trips, shows in
    /// the Inspector and reaches no code.
    /// </summary>
    [Serializable]
    public class StateVariant
    {
        [Tooltip("Identifier used in logs, e.g. \"walk_2\".")]
        public string key;

        [Tooltip("Directional sprites for this variant. Takes precedence over sheets, " +
                 "exactly as the base slots do.")]
        public DirectionalSprites directional;

        [Tooltip("Linear frame list: eight contiguous per-direction buckets in the order " +
                 "S, SE, E, NE, N, NW, W, SW.")]
        public List<Sprite> sheets;

        [Header("Pacing")]
        [Tooltip("Scales this variant's playback speed. 1 = the state's normal frame rate. " +
                 "Multiplies with the entity-wide and per-state multipliers rather than " +
                 "replacing them.")]
        [Min(0.05f)] public float animationSpeedMultiplier = 1f;

        [Tooltip("Play the frames once and hold the last one, instead of looping. A second " +
                 "death animation wants this; a second walk cycle does not.")]
        public bool holdLastFrame;
    }

    /// <summary>
    /// The alternative animations for ONE state, grouped under the state they belong to.
    ///
    /// Grouped rather than flat because the alternative — a <c>state</c> field on every
    /// <see cref="StateVariant"/> — is a field that means something at the top level and
    /// nothing at all inside a <see cref="Loadout"/>, where the state is already named by
    /// the entry that owns the list. One redundant field on every variant of every
    /// character is exactly the kind of thing that later gets set wrong and ignored.
    ///
    /// Keyed by a STRING for the same two reasons <see cref="LoadoutStateSheets"/> is:
    /// the base slots are already enumerated positionally in four places, and
    /// <c>AnimState</c> lives in <c>Valkur.Gameplay</c>, which <c>Valkur.Data</c> may not
    /// reference.
    /// </summary>
    [Serializable]
    public class StateVariantGroup
    {
        [Tooltip("Which state these vary: idle, walk, chase, damage, death or recover. " +
                 "attack and cast are REFUSED here — they have attackVariants and " +
                 "castVariants, whose selection rules are different.")]
        public string state;

        [Tooltip("The alternatives, in authored order. Index 0 is where the rotation " +
                 "starts, so put the character's default cycle first.")]
        public List<StateVariant> variants = new List<StateVariant>();
    }

    /// <summary>
    /// One state's art inside a <see cref="Loadout"/>.
    ///
    /// Keyed by a STRING rather than by a position or an enum, for two reasons. The seven
    /// base slots are enumerated positionally in four places and this class exists partly to
    /// stop a loadout becoming a fifth; and <c>AnimState</c> lives on
    /// <c>DirectionalAnimator</c> in <c>Valkur.Gameplay</c>, which <c>Valkur.Data</c> is not
    /// allowed to reference. The names are the same ones the frame manifest uses —
    /// <c>idle</c>, <c>walk</c>, <c>chase</c>, <c>cast</c>, <c>attack</c>, <c>damage</c>,
    /// <c>death</c>, <c>recover</c> — so the pipeline and the runtime agree by construction.
    /// </summary>
    [Serializable]
    public class LoadoutStateSheets
    {
        [Tooltip("Which base state this overrides: idle, walk, chase, cast, attack, " +
                 "damage, death or recover.")]
        public string state;

        [Tooltip("Directional sprites for this state. Takes precedence over sheets, " +
                 "exactly as the base slots do.")]
        public DirectionalSprites directional;

        [Tooltip("Linear frame list: eight contiguous per-direction buckets in the order " +
                 "S, SE, E, NE, N, NW, W, SW.")]
        public List<Sprite> sheets;

        [Tooltip("Alternative animations for THIS state while THIS loadout is worn. " +
                 "Empty means the single set above is all there is.")]
        public List<StateVariant> variants = new List<StateVariant>();
    }

    /// <summary>
    /// How fast ONE state plays, for an entity whose states are not all paced alike.
    ///
    /// The entity-wide <see cref="AnimationScaleConfig.animationSpeedMultiplier"/> cannot say
    /// this: it moves every state at once, so slowing an idle to a drowsy breath would slow
    /// the walk with it and the character would appear to wade. A per-VARIANT multiplier
    /// cannot say it either — <c>DirectionalAnimator.PacingOf</c> answers the neutral default
    /// for variant -1, and idle/walk/chase have no variants at all, so there was no way to
    /// pace them separately before this.
    ///
    /// Keyed by a STRING for the same two reasons <see cref="LoadoutStateSheets"/> is: the
    /// seven base slots are already enumerated positionally in four places, and
    /// <c>AnimState</c> lives in <c>Valkur.Gameplay</c>, which <c>Valkur.Data</c> may not
    /// reference. The names match the frame manifest's — <c>idle</c>, <c>walk</c>,
    /// <c>chase</c>, <c>cast</c>, <c>attack</c>, <c>damage</c>, <c>death</c>,
    /// <c>recover</c>.
    ///
    /// Carries a speed and nothing else on purpose. A per-state <c>holdLastFrame</c> would be
    /// authorable, round-trip, and reach no code — <c>DeathState</c> already owns the one
    /// state that stops on its last frame — and this project has paid for authored-but-inert
    /// fields more than once.
    /// </summary>
    [Serializable]
    public class StatePacing
    {
        [Tooltip("Which state this paces: idle, walk, chase, cast, attack, damage, death " +
                 "or recover.")]
        public string state;

        [Tooltip("Scales this state's playback speed. 1 = the entity's normal frame rate; " +
                 "below 1 is slower. Multiplies with the entity-wide multiplier rather than " +
                 "replacing it.")]
        [Min(0.05f)] public float animationSpeedMultiplier = 1f;
    }

    /// <summary>
    /// A named alternative look for the SAME character — the dwarf with his sword drawn.
    ///
    /// An OVERRIDE LIST, not a second <see cref="EntityAssetConfig"/>. The armed dwarf has
    /// art for four states and will never have art for the other six: nobody is going to draw
    /// a second death, a second hurt and five more spellcasts so the character can be hit
    /// while holding a sword. A second config would have to either duplicate those six
    /// (two copies of the same frames, drifting the moment one is re-imported) or leave them
    /// empty and fall back to a neighbour, which puts the character in the wrong POSE rather
    /// than merely the wrong hands. Overriding the four that exist keeps the other six
    /// shared, and shared is also correct: getting hurt looks the same either way.
    ///
    /// A loadout is a LOOK, not a stat block. It carries no damage, range or speed — those
    /// belong to the spell or the variant that uses them, and a loadout that quietly changed
    /// combat numbers would make the same swing hit differently depending on an animation
    /// toggle.
    ///
    /// It DOES carry its own <see cref="attackVariants"/> and <see cref="castVariants"/>,
    /// and that is not a stat block either — it is the same "which art plays" question the
    /// state overrides answer, asked for the two states whose art is chosen by an ACTION.
    /// Without them a rotation is global while the look is not, which is visibly wrong in
    /// both directions: the mague's five bare-handed casts would keep rotating while he is
    /// holding his staff, and his three staff casts would put it back in his empty hands.
    /// They REPLACE rather than extend, for the same reason
    /// <see cref="LoadoutStateSheets.variants"/> does — a rotation mixing the two looks is
    /// the exact pop this whole mechanism exists to avoid — which is why a loadout that
    /// overrides casting must re-declare any reservation it still needs, the
    /// <c>weapon_toggle</c> sheathe above all: it is cast FROM inside the loadout, so a
    /// loadout whose cast list forgets it stows the weapon to a spellcasting pose.
    /// </summary>
    [Serializable]
    public class Loadout
    {
        [Tooltip("Identifier used by the loadout-toggle spell and in logs, e.g. \"armed\".")]
        public string key;

        [Tooltip("The states this loadout replaces. Any state not listed keeps the base art.")]
        public List<LoadoutStateSheets> states = new List<LoadoutStateSheets>();

        [Tooltip("The swings this loadout uses instead of the base attackVariants. Empty " +
                 "means the base rotation stands.")]
        public List<AttackVariant> attackVariants = new List<AttackVariant>();

        [Tooltip("The casting animations this loadout uses instead of the base " +
                 "castVariants. Empty means the base rotation stands.")]
        public List<CastVariant> castVariants = new List<CastVariant>();

        /// <summary>The override for <paramref name="state"/>, or null when this loadout
        /// does not replace it and the base art stands.</summary>
        public LoadoutStateSheets Find(string state)
        {
            if (states == null || string.IsNullOrEmpty(state)) return null;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i] != null &&
                    string.Equals(states[i].state, state, StringComparison.OrdinalIgnoreCase))
                    return states[i];
            }
            return null;
        }
    }

    /// <summary>
    /// The muzzle for ONE drawn frame, keyed by that frame's sprite name.
    ///
    /// <para>Per FRAME rather than per entity because the head MOVES. Measured on the red
    /// dragon's eight cast frames, the mouth travels from (2.82, 1.31) to (4.09, 3.98) world
    /// units as it rears — 1.3 units of sweep forward and 2.7 up — so a single pair lands on
    /// the mouth in four frames and in open air in the other four. It is cheap because the
    /// mirrors are baked: an entity drawn in one direction has two halves and its eight
    /// direction buckets are filled from them, so a full sheet is a couple of dozen rows.</para>
    /// </summary>
    [Serializable]
    public class CastMuzzleFrame
    {
        [Tooltip("Sprite name this measurement belongs to, e.g. red_dragon_cast_e3.")]
        public string frame;

        [Tooltip("X is FORWARD along the drawn facing and is always positive here — the " +
                 "sign comes from which half is being rendered, so the two mirrored halves " +
                 "share one number. Y is height above the sprite's vertical centre. Both " +
                 "are fractions of that frame's own sprite bounds.")]
        public Vector2 offset;

        [Tooltip("Set when a human placed this row by looking at the frame. The baker " +
                 "KEEPS it and re-measures everything else, because the measurement is a " +
                 "silhouette heuristic and a few poses defeat it — on the red dragon's " +
                 "cast_e6 the foreleg reaches further forward than the snout, so the " +
                 "automatic answer follows the leg.")]
        public bool handTuned;
    }

    /// <summary>
    /// Complete asset configuration for an entity.
    /// Maps to Python's "assets" block in new_hostiles/new_players.
    /// </summary>
    [Serializable]
    public class EntityAssetConfig
    {
        [Header("Directional Sprites (no-sets mode)")]
        public DirectionalSprites idle;
        public DirectionalSprites walk;
        public DirectionalSprites chase;
        public DirectionalSprites cast;
        public DirectionalSprites attack;
        public DirectionalSprites damage;
        public DirectionalSprites death;

        [Tooltip("Getting back up, played by DeathSequenceController on revive. Optional: " +
                 "an entity without it falls back to idle, so leaving it empty is a valid " +
                 "authoring state rather than a hole.")]
        public DirectionalSprites recover;

        [Header("Sprite Sheet Mode (sets)")]
        public List<Sprite> idleSheets;
        public List<Sprite> walkSheets;
        public List<Sprite> chaseSheets;
        public List<Sprite> castSheets;
        public List<Sprite> attackSheets;
        public List<Sprite> damageSheets;
        public List<Sprite> deathSheets;
        public List<Sprite> recoverSheets;

        [Tooltip("How the *Sheets lists above (and attackVariants' sheets) are sliced into " +
                 "directions. Auto reproduces EntityAnimationBinder's historical frame-count " +
                 "heuristic and is the safe default for every asset authored before this " +
                 "field existed. Set explicitly when a sheet's frame count is ambiguous " +
                 "under that heuristic — e.g. a 24-frame sheet could be a genuine 8x3 strip " +
                 "or a stretched 4-direction one.")]
        public EntitySheetDirectionLayout directionLayout = EntitySheetDirectionLayout.Auto;

        [Header("Attack Variants")]
        // Empty for every entity that has one attack, which is all of them but the knight.
        // When it is non-empty it REPLACES the single attack set for selection purposes:
        // index 0 is what a picker falls back to, so put the entity's default swing first.
        // `attack`/`attackSheets` stay authoritative for callers that know nothing about
        // variants (the Spells Editor preview reads AttackSprites directly).
        public List<AttackVariant> attackVariants = new List<AttackVariant>();

        [Header("Cast Variants")]
        // Empty for every entity with one casting animation, which is all of them but elven.
        // Unlike attackVariants these carry no combat data — a spell's damage lives on its
        // SpellDefinition — so this is purely which animation plays.
        public List<CastVariant> castVariants = new List<CastVariant>();

        [Header("State Variants")]
        // Alternative animations for the states that are neither Attack nor Cast: a second
        // walk cycle, a third death. Empty for every entity that draws each state once,
        // which is all of them but the mague.
        //
        // A loadout that overrides a state REPLACES that state's variants with its own
        // (LoadoutStateSheets.variants), rather than adding to these. It has to: the mague's
        // base walk carries four staff cycles, and rotating those while the `unarmed`
        // loadout is worn would put the staff back in his hands one step in four.
        public List<StateVariantGroup> stateVariants = new List<StateVariantGroup>();

        /// <summary>
        /// The alternatives authored for <paramref name="state"/>, or null when this entity
        /// draws it once — which is the answer for every state of nearly every entity.
        /// </summary>
        public List<StateVariant> FindStateVariants(string state)
        {
            if (stateVariants == null || string.IsNullOrEmpty(state)) return null;
            for (int i = 0; i < stateVariants.Count; i++)
            {
                if (stateVariants[i] != null &&
                    string.Equals(stateVariants[i].state, state, StringComparison.OrdinalIgnoreCase))
                    return stateVariants[i].variants;
            }
            return null;
        }

        [Header("Per-State Pacing")]
        // Empty for every entity that plays all its states at one speed, which is nearly all
        // of them. Gatita is the case it exists for: her idle is a slow breath and her walk
        // is a normal stride, and the entity-wide multiplier can only move both together.
        public List<StatePacing> statePacing = new List<StatePacing>();

        /// <summary>
        /// The authored speed for <paramref name="state"/>, or 1 when this entity paces that
        /// state normally — which is the answer for every state of nearly every entity.
        /// </summary>
        public float StateSpeedMultiplier(string state)
        {
            if (statePacing == null || string.IsNullOrEmpty(state)) return 1f;
            for (int i = 0; i < statePacing.Count; i++)
            {
                if (statePacing[i] != null &&
                    string.Equals(statePacing[i].state, state, StringComparison.OrdinalIgnoreCase))
                    return Mathf.Max(0.05f, statePacing[i].animationSpeedMultiplier);
            }
            return 1f;
        }

        [Header("Loadouts")]
        // Alternative LOOKS for this same character, each overriding only the states it has
        // art for. Empty for every entity but the dwarf, who ships an `armed` loadout.
        // Swapped at runtime by PlayerLoadoutController; the base slots above stay the
        // character's unarmed self and are what an entity with no loadout ever shows.
        public List<Loadout> loadouts = new List<Loadout>();

        /// <summary>The loadout named <paramref name="key"/>, or null — including for the
        /// null/empty key, which is how "no loadout, use the base art" is spelled.</summary>
        public Loadout FindLoadout(string key)
        {
            if (loadouts == null || string.IsNullOrEmpty(key)) return null;
            for (int i = 0; i < loadouts.Count; i++)
            {
                if (loadouts[i] != null &&
                    string.Equals(loadouts[i].key, key, StringComparison.OrdinalIgnoreCase))
                    return loadouts[i];
            }
            return null;
        }

        [Header("Cast Muzzle")]
        [Tooltip("Where a cast leaves this body, as a fraction of the CURRENT frame's own " +
                 "sprite bounds. X is forward along the drawn facing (1 = the leading edge " +
                 "of the sprite), Y is height above the sprite's vertical centre " +
                 "(0 = centre, 1 = the top). Leave at (0,0) — the default — and the entity " +
                 "keeps the shared SpellCastAnchor behaviour.")]
        public Vector2 castMuzzle = Vector2.zero;

        [Tooltip("Per-frame muzzle measurements, which OVERRIDE castMuzzle for any frame " +
                 "they name. Baked by Valkur > Monsters > Bake Cast Muzzles; castMuzzle " +
                 "stays as the answer for a frame the bake did not cover.")]
        public List<CastMuzzleFrame> castMuzzleFrames = new List<CastMuzzleFrame>();

        /// <summary>
        /// True when this entity declares its own muzzle. (0,0) is the "nobody authored one"
        /// sentinel, the same shape as <c>scaleConfig.tint</c>'s alpha-zero and
        /// <c>particleColor</c>'s opaque white — and it is safe here because the sprite's own
        /// centre is never a mouth: an entity that really wanted to cast from its navel
        /// authors <c>SpellCastAnchor.Center</c>, which is what that enum is for.
        /// </summary>
        public bool HasCastMuzzle =>
            !Mathf.Approximately(castMuzzle.x, 0f) || !Mathf.Approximately(castMuzzle.y, 0f);

        [Header("Scale & Tint")]
        public AnimationScaleConfig scaleConfig;
    }
}
