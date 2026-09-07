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

        [Tooltip("Fraction of BASE hp added per level above 1. 0.12 means a level-5 monster has " +
                 "1 + 4x0.12 = 1.48x its authored hp. 0 (every monster shipped before this " +
                 "field) means no growth, so nothing changes until an author asks for it. \n\n" +
                 "This exists BESIDE levelScaling rather than instead of it because the two " +
                 "answer different questions and this bestiary needs the second one. " +
                 "LevelStatCurve is ABSOLUTE — a flat hpPerLevel, or an explicit table — which " +
                 "is right for the player, who has one hp scale. Monsters span 10 hp " +
                 "(barbol_baby) to 10,000 (barbol_gigante): a shared absolute curve at +18/level " +
                 "nearly triples the baby by level 2 and is a rounding error on the colossus, so " +
                 "one authored number cannot serve the roster. A proportional growth can, which " +
                 "is what makes difficulty authorable at all rather than needing eighteen curve " +
                 "assets that drift apart. \n\n" +
                 "An authored curve WINS, so anything already using levelScaling is untouched.")]
        [Min(0f)] public float levelHpGrowth;

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

        [Tooltip("Coins granted when this monster is killed, before the per-kill variance " +
                 "DeathDropSystem rolls. 0 = fall back to the heuristic (hp/40 + power/4), " +
                 "which is why every monster shipped before this field existed starts paying " +
                 "out without a data edit — the same contract xpReward already uses. " +
                 "Deliberately NOT derived from xpReward: that is a difficulty knob a designer " +
                 "sets to say how much a kill is worth as PROGRESS, and tying wealth to it " +
                 "would make every XP retune a silent economy retune. -1 is the explicit " +
                 "'this one pays nothing' — needed because 0 is already spoken for by the " +
                 "fallback, and a monster that should drop no coins must be sayable without " +
                 "editing its faction, which decides four other things.")]
        [Min(-1)] public int coinReward;

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
        public EntityStats GetScaledStats() => GetScaledStats(level);

        /// <summary>
        /// The same scaling at an ARBITRARY level, so a spawn can be levelled without touching
        /// the asset.
        ///
        /// <para>This overload is the whole reason encounter difficulty is possible at all. A
        /// <c>MonsterDefinition</c> is a ScriptableObject SHARED by every instance of that
        /// monster in the scene, so raising <c>level</c> at spawn time to make one camp harder
        /// would raise it for every barbol in the world — and, because Unity persists a
        /// ScriptableObject edited in Play Mode until the next domain reload, it would follow
        /// the author back into the Editor and quietly rewrite the shipped balance. The level
        /// has to travel WITH the spawn, not on the definition.</para>
        /// </summary>
        public EntityStats GetScaledStats(int atLevel)
        {
            if (atLevel <= 1) return stats;

            int hpBonus;
            if (levelScaling != null)
            {
                // The authored table wins. Cumulative from level 2, exactly as a player
                // levelling one at a time through this same asset type would accumulate.
                hpBonus = 0;
                for (int lvl = 2; lvl <= atLevel; lvl++)
                    hpBonus += levelScaling.HpDelta(lvl);
            }
            else
            {
                // Proportional growth. Linear in levels rather than compounding: compounding
                // makes the last few levels of a long run dwarf every one before them, and the
                // clamp in EncounterDifficulty is a ceiling on the level, not on the curve.
                hpBonus = Mathf.RoundToInt(stats.hp * levelHpGrowth * (atLevel - 1));
            }

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
