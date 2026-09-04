using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// ScriptableObject defining a monster type.
    /// Maps to Python's new_hostiles.json -> hostiles.classes[className].
    /// One asset per monster class (barbol, barbol_elite, dragon, etc.).
    /// </summary>
    [CreateAssetMenu(fileName = "NewMonster", menuName = "Valkur/Data/Monster Definition")]
    public class MonsterDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string monsterKey;
        public string displayName;

        [Header("Stats")]
        public EntityStats stats;

        [Header("Level & Scaling")]
        [Tooltip("Authoring level — 'the same monster, but for a later zone' without duplicating " +
                 "the asset and retyping every stat. 1 (the default) is the baseline: " +
                 "GetScaledStats() returns 'stats' completely unchanged whenever level <= 1, " +
                 "so every monster shipped before this field existed is untouched regardless of " +
                 "this value. Nothing on this class pushes the scaled result onto a live entity — " +
                 "see EntitySetup.ConfigureMonster / FSMMonsterBrain.Initialize, which must read " +
                 "GetScaledStats() instead of 'stats' for hp/defense/meleeDamage to make a level " +
                 "actually do anything.")]
        [Min(1)] public int level = 1;

        [Tooltip("Optional per-level HP growth. Reuses LevelStatCurve's existing shape (linear " +
                 "hpPerLevel, or an AnimationCurve override — see its class doc) rather than " +
                 "inventing a second curve concept; only the Hp half is used, since monsters " +
                 "have no 'mana' stat to grow. Evaluated CUMULATIVELY from level 2 up to " +
                 "'level' (HpDelta(2) + HpDelta(3) + ... + HpDelta(level)) — the same total a " +
                 "player would accumulate levelling one at a time through this same asset type. " +
                 "Null (the default) = no scaling at all, independent of 'level'. meleeDamage " +
                 "and defense scale by the same ratio hp grows by — a bestiary rarely wants a " +
                 "later-zone monster tougher but not harder-hitting, and a second pair of curves " +
                 "would double the authoring surface for no shipped need. speed, chasingSpeed, " +
                 "meleeRange, meleeCooldown, aggroRange and every timing knob are deliberately " +
                 "left alone — a scaled monster should still move, reach and time exactly like " +
                 "the monster it is a scaled copy of.")]
        public LevelStatCurve levelScaling;

        [Header("AI")]
        public string fsmSet;
        public string patrolType;
        public bool useAttackTelegraph;

        [Tooltip("Per-monster feel knobs — aggro hysteresis, leash, repath cadence, flee " +
                 "and alert timing, re-swing reach. Every field is 0 by default, which " +
                 "means 'use the engine default' (see FSMTuning), so leaving this block " +
                 "untouched reproduces the behaviour these values had as compile-time " +
                 "constants inside the state classes.")]
        public AIBehaviourTuning aiTuning;

        [Header("Phase Boss")]
        [Tooltip("Optional. When set, this monster is a boss: EntitySetup.ConfigureMonster " +
                 "attaches BossPhaseController + BossConfigurator (plus SpellCaster/NPCAutoCast " +
                 "if not already present) and drives phase transitions + spell rotations from " +
                 "this asset. Empty = plain monster, unaffected. Replaces the previous " +
                 "nextPhase/phaseIndex fields, which had zero readers anywhere in the project.")]
        public BossDefinition bossDefinition;

        [Header("Auto Cast")]
        public bool autoCast;
        public string[] autoCastList;

        [Header("Reward")]
        [Tooltip("Explicit XP granted when this monster is killed. " +
                 "0 = fall back to the legacy heuristic (hp/5 + power) so " +
                 "monsters migrated before this field existed keep working. " +
                 "Designers should set this to a positive value for tunable " +
                 "balance.")]
        public int xpReward;

        [Tooltip("Optional weighted drop table rolled on death by " +
                 "DeathDropSystem, in addition to XP and any Inventory " +
                 "the entity happens to carry. Null = no loot-table drop " +
                 "(most monsters, and every vendor/friendly NPC — the " +
                 "roll is gated to stats.faction == \"EVIL\" regardless " +
                 "of whether a table is assigned here).")]
        public LootTable lootTable;

        [Header("Chat / Vendor")]
        [Tooltip("Assign to make this entity chat-capable. EntitySetup.ConfigureChat adds " +
                 "NPCInteractable + NPCChatIdentity when this is set, and ChatSystem reads the " +
                 "persona straight off that component — no name matching, so renaming an entity " +
                 "can no longer silently unhook its dialogue. Null (every hostile) = the entity " +
                 "cannot be talked to and pays for nothing. This is the OWNER of 'who is this " +
                 "character'; ChatAssignmentCatalog remains as the by-name fallback for entities " +
                 "configured by hand rather than spawned from a definition.")]
        public NPCPersonaDefinition chatPersona;

        [Tooltip("Assign to make this entity a vendor. EntitySetup.ConfigureChat adds VendorNPC " +
                 "and hands it this config; the shop is opened from the chat panel's Trade button, " +
                 "which is the only caller of NPCInteractable.Interact(). Null = not a vendor. " +
                 "A vendor should normally also carry a chatPersona — VendorConfigDefinition has " +
                 "its own 'persona' field, and the two must name the same character.")]
        public VendorConfigDefinition vendorConfig;

        [Header("Assets")]
        public EntityAssetConfig assetConfig;

        /// <summary>
        /// Returns <see cref="stats"/> scaled for <see cref="level"/> via
        /// <see cref="levelScaling"/>. Pure (no allocation beyond the one struct copy,
        /// no side effects) — safe to call every spawn or every <c>reconfig</c>.
        ///
        /// Level &lt;= 1, or no curve assigned, returns <c>stats</c> completely
        /// UNCHANGED — same values, same array references (<c>resistances</c>,
        /// <c>statusImmunities</c>) — which is what keeps every monster shipped
        /// before this method existed byte-identical. See the class doc on
        /// <see cref="levelScaling"/> for exactly which fields scale and why.
        /// </summary>
        public EntityStats GetScaledStats()
        {
            if (levelScaling == null || level <= 1) return stats;

            int hpBonus = 0;
            for (int lvl = 2; lvl <= level; lvl++)
                hpBonus += levelScaling.HpDelta(lvl);
            if (hpBonus <= 0) return stats;

            var scaled = stats;
            scaled.hp = stats.hp + hpBonus;

            // meleeDamage/defense track hp's growth ratio. baseHp <= 0 (e.g. an all-zero
            // stub definition) has no ratio to derive from — hp still grows by hpBonus,
            // everything else is left at its authored value rather than dividing by zero.
            if (stats.hp > 0)
            {
                float ratio = scaled.hp / (float)stats.hp;
                scaled.meleeDamage = Mathf.RoundToInt(stats.meleeDamage * ratio);
                scaled.defense     = Mathf.RoundToInt(stats.defense * ratio);
            }
            return scaled;
        }
    }
}
