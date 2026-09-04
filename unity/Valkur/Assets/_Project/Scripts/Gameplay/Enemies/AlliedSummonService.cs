using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Turns a <see cref="MonsterDefinition"/> into something that fights FOR the player.
    ///
    /// <para>WHY THIS EXISTS. <c>SummonExecutor</c> has never produced a creature: it builds a
    /// GameObject with a SpriteRenderer, a Rigidbody2D, a CircleCollider2D,
    /// <c>Health.Initialize(50)</c> and a despawn timer — no FSM brain, no MeleeCombat, no
    /// target acquisition — and when the spell authors no sprite it generates a plain white
    /// circle. The shipped <c>summon_barbol</c> is a green blob that stands still and can be
    /// killed. Every summon in the game was cosmetic.</para>
    ///
    /// <para>The fix is not a second entity pipeline. It is the EXISTING one — the monster
    /// prefab, <c>EntitySetup.ConfigureMonster</c>, <c>FSMMonsterBrain</c> — with two things
    /// changed afterwards: an <see cref="AlliedUnit"/> marker, which is what
    /// <c>FactionTargeting</c> reads, and the melee target mask flipped from the Player
    /// layer to the NPC layer. Everything else about the creature is whatever its definition
    /// says, which is the point: a summoned wolf is a wolf.</para>
    /// </summary>
    public static class AlliedSummonService
    {
        /// <summary>NPC layer, matching <c>EntitySetup</c>'s constant.</summary>
        private const int NPCLayer = 9;

        /// <summary>Tint held on an ally for its whole life, so a summon can never be
        /// mistaken for an enemy. A summon indistinguishable from a monster is a UI failure
        /// wearing a VFX costume.</summary>
        private static readonly Color AllyTint = new Color(0.62f, 0.90f, 0.68f, 1f);

        /// <summary>
        /// Spawn <paramref name="def"/> at <paramref name="position"/> as an ally that lives
        /// for <paramref name="lifetime"/> seconds. Returns null when there is no
        /// <c>MonsterSpawner</c> to build it — the spawner owns the monster prefab and the
        /// entities container, and duplicating either here is how the two drift apart.
        /// </summary>
        public static GameObject Summon(MonsterDefinition def, Vector2 position, float lifetime,
                                        float healthScale = 1f)
        {
            if (def == null) return null;

            var spawner = Object.FindObjectOfType<MonsterSpawner>();
            if (spawner == null)
            {
                Debug.LogWarning("[AlliedSummonService] No MonsterSpawner in the scene, so the " +
                                 "summon produced nothing. The spawner owns the monster prefab.");
                return null;
            }

            var go = spawner.SpawnEntity(def, position);
            if (go == null) return null;

            Adopt(go, lifetime, healthScale);
            return go;
        }

        /// <summary>
        /// Convert an ALREADY-SPAWNED monster into an ally. This is the path
        /// <c>ThrallMarkEffect</c> takes: it does not spawn anything, it re-sides a creature
        /// that already exists.
        /// </summary>
        public static void Adopt(GameObject go, float lifetime, float healthScale = 1f)
        {
            if (go == null) return;

            var ally = go.GetComponent<AlliedUnit>();
            if (ally == null) ally = go.AddComponent<AlliedUnit>();
            ally.SetLifetime(lifetime);

            // The mask flip is the mechanical half of changing sides. Without it the creature
            // would still swing at the Player layer while FactionTargeting sent it after
            // monsters -- it would walk to the right enemy and hit nothing.
            var combat = go.GetComponent<MeleeCombat>();
            if (combat != null)
            {
                combat.SetTargetLayers(1 << NPCLayer);
                combat.SetSlashVfxColor(new Color(0.55f, 0.95f, 0.7f, 0.85f));
            }

            if (healthScale > 0f && !Mathf.Approximately(healthScale, 1f))
            {
                var health = go.GetComponent<Health>();
                if (health != null)
                {
                    // A raised boss must not hand the player a boss. Scaled through the
                    // idempotent absolute setter, never the delta API -- see PlayerStats for
                    // why every push in this project takes that shape.
                    int scaled = Mathf.Max(1, Mathf.RoundToInt(health.MaxHp * healthScale));
                    health.SetMaxHp(scaled);
                }
            }

            var tint = SpriteTintStack.Attach(go);
            tint?.Set(TintLayer.Spirit, AllyTint);

            // Minimap: green, so the player can find their own summon in a fight. Reflection
            // for the same reason ConfigureMonster does it -- Gameplay may not reference UI.
            EntitySetup.ConfigureMinimapDot(go, "Ally", new Color(0.4f, 0.95f, 0.5f, 1f));

            var bar = go.GetComponent<WorldHealthBar>();
            if (bar != null)
                bar.SetBarColors(new Color(0.3f, 0.85f, 0.45f, 1f),
                                 new Color(0.45f, 0.95f, 0.55f, 1f));

            ally.OnExpired += () => AllyDismissFX.Play(go);
        }

        /// <summary>
        /// True when this definition must never be raised or summoned. A boss carries phase
        /// choreography, its own music and a health pool balanced against the whole fight;
        /// handing one to the player as a pet is not a tuning problem, it is a different game.
        /// </summary>
        public static bool IsForbiddenAsAlly(MonsterDefinition def, GameObject instance)
        {
            if (def == null) return true;
            if (instance != null && instance.GetComponent<BossPhaseController>() != null) return true;
            return false;
        }
    }
}
