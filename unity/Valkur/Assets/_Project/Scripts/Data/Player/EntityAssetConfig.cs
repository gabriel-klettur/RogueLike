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
    /// </summary>
    [Serializable]
    public class Loadout
    {
        [Tooltip("Identifier used by the loadout-toggle spell and in logs, e.g. \"armed\".")]
        public string key;

        [Tooltip("The states this loadout replaces. Any state not listed keeps the base art.")]
        public List<LoadoutStateSheets> states = new List<LoadoutStateSheets>();

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

        [Header("Scale & Tint")]
        public AnimationScaleConfig scaleConfig;
    }
}
