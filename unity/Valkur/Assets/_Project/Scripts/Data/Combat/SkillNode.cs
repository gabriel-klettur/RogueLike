using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// One node of a skill tree. Carries the player-facing presentation
    /// (id, display name, icon, description), the cost in skill points,
    /// the prerequisite chain, and a list of stat / spell / passive
    /// effects expressed as <see cref="SkillEffect"/> tuples. The actual
    /// effect application is the consumer's job — this SO is pure data.
    ///
    /// Why an enum + value tuple instead of an inheritance hierarchy: a
    /// SO graph with 100+ nodes that uses inheritance balloons into 100
    /// separate asset files, each tracked by GUID. The flat (kind, key,
    /// value) shape keeps every skill a single asset and makes copy /
    /// rename / data-driven mod overlays straightforward.
    /// </summary>
    [CreateAssetMenu(fileName = "NewSkillNode", menuName = "Valkur/Data/Skill Node")]
    public sealed class SkillNode : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable id used for save persistence. NEVER rename after release " +
                 "or every save that learned this node breaks.")]
        public string skillId;

        [Tooltip("UI label.")]
        public string displayName;

        [TextArea(2, 5)]
        public string description;

        public Sprite icon;

        [Header("Cost")]
        [Tooltip("Skill points needed to learn this node.")]
        [Min(0)] public int pointCost = 1;

        [Tooltip("Player level required to even see / learn this node. 0 = no gate.")]
        [Min(0)] public int levelRequirement = 0;

        [Header("Prerequisites")]
        [Tooltip("Skill nodes that must already be learned before this one is " +
                 "available. Empty = a root node (always available once levelRequirement is met).")]
        public SkillNode[] prerequisites = Array.Empty<SkillNode>();

        [Header("Effects")]
        [Tooltip("List of effects this node applies once learned. Consumed by " +
                 "PlayerStats / SpellCaster / status systems on Learn().")]
        public SkillEffect[] effects = Array.Empty<SkillEffect>();
    }

    /// <summary>
    /// One unit of effect applied by a learned skill. Each consumer (stat
    /// system, spell unlocker, passive aura registry) recognises its own
    /// <see cref="kind"/> values and ignores the rest.
    /// </summary>
    [Serializable]
    public struct SkillEffect
    {
        public SkillEffectKind kind;
        [Tooltip("Effect-specific identifier. For StatBoost: stat name (e.g. 'maxHp', 'damage'). " +
                 "For UnlockSpell: spell key. For PassiveAura: aura id.")]
        public string key;
        [Tooltip("Effect-specific magnitude. For StatBoost: additive amount. " +
                 "For UnlockSpell: ignored. For PassiveAura: stack count.")]
        public float value;
    }

    public enum SkillEffectKind
    {
        StatBoost,
        UnlockSpell,
        PassiveAura,
    }
}
