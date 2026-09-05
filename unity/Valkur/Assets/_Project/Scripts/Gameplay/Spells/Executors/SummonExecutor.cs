using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Summons an ALLY — a real creature, built from a <see cref="MonsterDefinition"/> through
    /// the same pipeline every monster in the game comes out of.
    ///
    /// <para>WHAT THIS USED TO DO. It built a bare GameObject with a SpriteRenderer, a
    /// Rigidbody2D, a CircleCollider2D, <c>Health.Initialize(50)</c> and a despawn timer, and
    /// when the spell authored no sprite it generated a 24 px white disc and tinted it green.
    /// No <c>DirectionalAnimator</c>, no <c>MeleeCombat</c>, no FSM brain, no
    /// <c>AlliedUnit</c>: the thing could not attack, could not be attacked usefully, and
    /// followed its caster on a hardcoded 4-unit leash. Every summon in the game was
    /// cosmetic — the shipped <c>summon_barbol</c> was a green blob that stood still.</para>
    ///
    /// <para>THE FIX IS NOT A SECOND ENTITY PIPELINE. <see cref="AlliedSummonService"/>
    /// already exists and already does the whole job: it spawns through <c>MonsterSpawner</c>
    /// (which owns the monster prefab and calls <c>EntitySetup.ConfigureMonster</c>), attaches
    /// the <c>AlliedUnit</c> marker <c>FactionTargeting</c> reads, flips the melee mask from
    /// the Player layer to the NPC layer, recolours the minimap dot and the health bar, and
    /// wires the timeout to <c>AllyDismissFX</c>. All this executor has to do is decide WHICH
    /// definition and WHERE, and hand the arrival to <see cref="SummonRiseFX"/>.</para>
    ///
    /// <para>A summon whose <c>summonTemplate</c> names nothing is REFUSED and says so, rather
    /// than falling back to a proxy. A proxy that cannot fight is worse than a loud failure:
    /// the player spends the mana and the cooldown, watches something appear, and only finds
    /// out several seconds later that it was never a creature.</para>
    /// </summary>
    public class SummonExecutor : ISpellExecutor
    {
        /// <summary>Reach for an aimed summon whose definition authors no <c>range</c>.</summary>
        private const float AIMED_FALLBACK_RANGE = 4f;

        /// <summary>How far in front a non-aimed summon lands when it authors no distance.</summary>
        private const float PLACED_FALLBACK_DISTANCE = 2.5f;

        /// <summary>Scatter radius when one cast produces several bodies.</summary>
        private const float CLUSTER_SPREAD = 1.2f;

        private const float DEFAULT_DURATION = 20f;

        /// <summary>Health a summoned ally is worth, whatever creature the template names.</summary>
        private const float PET_HP_BUDGET = 90f;

        public void Execute(SpellContext ctx)
        {
            if (ctx.Caster == null || ctx.Spell == null) return;

            var definition = ResolveDefinition(ctx.Spell);
            if (definition == null) return;      // ResolveDefinition has already said why.

            int count = Mathf.Max(1, ctx.Spell.summonCount);
            // A summon's real exit is death; the timer only recalls a survivor.
            float duration = ctx.Spell.infinite
                ? float.PositiveInfinity
                : (ctx.Spell.summonDuration > 0f ? ctx.Spell.summonDuration : DEFAULT_DURATION);

            Vector2 ground = ResolveGroundPoint(ctx);
            GameObject caster = ctx.Caster.gameObject;

            for (int i = 0; i < count; i++)
            {
                Vector2 offset = count > 1 ? Random.insideUnitCircle * CLUSTER_SPREAD : Vector2.zero;
                // Only the first body applies maxInstances: the cap counts CASTS, not bodies,
                // or a 3-unit summon capped at 1 would destroy two of its own on the way out
                // of this loop.
                SummonRiseFX.Play(definition, ground + offset, duration, ctx.Spell, caster,
                                  enforceCap: i == 0,
                                  healthScale: ResolvePetHealthScale(definition));
            }

            // Gated: AudioCatalog.asset holds no spell_* id at all, so an ungated call is one
            // guaranteed console warning on the first cast of every session. The catalog path
            // stays the better answer the day a recorded set is authored.
            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null && audio.HasSfx("spell_summon_create"))
                audio.PlaySfxById("spell_summon_create");
        }

        /// <summary>
        /// How much of its own health a summoned creature keeps.
        ///
        /// <para>A monster's HP is balanced against a whole encounter; a pet is balanced against
        /// twenty seconds and one mana cost. Handing the player the raw pool means the spell's
        /// strength is decided by whichever monsterKey happens to be authored in
        /// <c>summonTemplate</c> — <c>barbol_musgo</c>, the current stand-in, has 300, which is
        /// more than the player. This normalises to a BUDGET instead, so swapping the template
        /// (for an actual wolf, when one is drawn) changes the creature's look and behaviour
        /// and not its durability.</para>
        ///
        /// <para>Capped at 1: a genuinely fragile creature is never made tougher by being
        /// summoned, or the spell would be a buff applied to weak monsters.</para>
        /// </summary>
        private static float ResolvePetHealthScale(MonsterDefinition definition)
        {
            if (definition == null) return 1f;

            // GetScaledStats rather than the raw block: a monster's shipped hp is its level-1
            // value and the spawner applies the level curve, so measuring the raw number would
            // under-scale exactly the high-level creatures this guard exists for.
            // EntityStats is a STRUCT, so there is no null to test here.
            var stats = definition.GetScaledStats();
            if (stats.hp <= 0) return 1f;

            return Mathf.Min(1f, PET_HP_BUDGET / stats.hp);
        }

        /// <summary>
        /// The creature this spell summons, or null with a reason logged.
        ///
        /// <para><c>summonTemplate</c> is a monsterKey string, exactly as
        /// <c>summon_barbol</c> has always authored it — the resolution to a real definition
        /// is what never existed.</para>
        /// </summary>
        private static MonsterDefinition ResolveDefinition(SpellDefinition spell)
        {
            string key = spell.summonTemplate;
            if (string.IsNullOrWhiteSpace(key))
            {
                Debug.LogWarning($"[Summon] '{spell.spellKey}' authors no summonTemplate, so " +
                                 "there is nothing to summon. Author the monsterKey of a " +
                                 "MonsterDefinition in the F4 Spells Editor.");
                return null;
            }

            if (!ServiceLocator.TryGet<MonsterCatalog>(out var catalog) || catalog == null)
            {
                Debug.LogWarning($"[Summon] '{spell.spellKey}' cannot resolve '{key}': no " +
                                 "MonsterCatalog is registered with the ServiceLocator. " +
                                 "GameplaySceneSetup registers it during bootstrap.");
                return null;
            }

            var definition = catalog.GetByKey(key);
            if (definition == null)
            {
                Debug.LogWarning($"[Summon] '{spell.spellKey}' names monsterKey '{key}', which " +
                                 "is not in the MonsterCatalog.");
                return null;
            }

            // A boss carries phase choreography, its own music and a health pool balanced
            // against a whole fight. Handing one to the player as a pet is not a tuning
            // problem, it is a different game — the same refusal raise_thrall makes.
            if (AlliedSummonService.IsForbiddenAsAlly(definition, null))
            {
                Debug.LogWarning($"[Summon] '{key}' is forbidden as an ally and was refused.");
                return null;
            }

            return definition;
        }

        /// <summary>
        /// Where the creature stands.
        ///
        /// <para>The cursor is read through <see cref="SpellTargeting"/>, which is the single
        /// owner of what <c>spawnAtMouse</c> means — duplicating the projection here is how
        /// two executors end up clamping to different ranges.</para>
        ///
        /// <para>The REBASE is the half that is easy to miss. <c>ResolveGroundTarget</c>
        /// measures from <c>ResolveCastStart</c>: hand height plus forward clearance, which is
        /// where a PROJECTILE leaves from. A creature stands on the FLOOR, so the same aim is
        /// pulled back down onto the caster's own ground plane — without it every summon
        /// arrives half a body above the tile the player clicked and the rise sequence starts
        /// in mid-air.</para>
        /// </summary>
        private static Vector2 ResolveGroundPoint(SpellContext ctx)
        {
            Vector2 aimed = SpellTargeting.ResolveGroundTarget(
                ctx, AIMED_FALLBACK_RANGE, PLACED_FALLBACK_DISTANCE);

            Vector2 lift = (Vector2)ProjectileExecutor.ResolveCastStart(
                               ctx.Caster, ctx.Direction, ctx.Spell)
                         - (Vector2)ctx.Caster.position;

            return aimed - lift;
        }
    }
}
