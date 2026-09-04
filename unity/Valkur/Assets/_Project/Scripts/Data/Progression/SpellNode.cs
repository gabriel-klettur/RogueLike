using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// One entry of a grimoire: the act of LEARNING a spell, bought with arcane points.
    ///
    /// Before this existed, <c>EntitySetup</c> registered all 77 shipped
    /// <see cref="SpellDefinition"/> assets on the player's SpellCaster in the frame they
    /// spawned. 46 of them were castable and 46 of those cost no mana, so the player
    /// opened the game with the complete spell list and no decision attached to any of
    /// it. A node is what turns a spell from a menu entry into something earned.
    ///
    /// A grimoire node may also carry <see cref="modifiers"/>. That is deliberate and it
    /// is NOT a second talent tree: the modifiers a school hands out are the ones that
    /// make its spells worth casting (spell power, cooldown, mana cost), so a player who
    /// commits to fire gets a fire caster's numbers as a consequence of the commitment
    /// rather than as a separate purchase.
    /// </summary>
    [CreateAssetMenu(fileName = "NewSpellNode", menuName = "Valkur/Progression/Spell Node")]
    public sealed class SpellNode : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable id used for save persistence. NEVER rename after release.")]
        public string nodeId;

        [Header("Unlock")]
        [Tooltip("The spell this node teaches. A node with no spell is legal — it is then " +
                 "a pure passive of the school, e.g. a mastery that only carries modifiers.")]
        public SpellDefinition spell;

        [Tooltip("Arcane points needed to learn this node.")]
        [Min(1)] public int pointCost = 1;

        [Tooltip("Player level required. 0 = no gate.")]
        [Min(0)] public int levelRequirement;

        [Tooltip("Nodes that must already be learned before this one opens. " +
                 "Empty = a root node of the school.")]
        public SpellNode[] prerequisites = Array.Empty<SpellNode>();

        [Header("Presentation")]
        [Tooltip("Overrides the spell's own displayName in the grimoire view. Leave empty " +
                 "to use the spell's name, which is what a node teaching a spell should do.")]
        public string displayNameOverride;

        [TextArea(2, 5)]
        public string description;

        [Tooltip("Overrides the spell's icon in the grimoire view. Leave empty to use the " +
                 "spell's own.")]
        public Sprite iconOverride;

        [Header("Effects")]
        [Tooltip("Permanent stat modifiers granted alongside the unlock. Land in the " +
                 "Grimoire stat layer, so they are removed cleanly if the node is ever " +
                 "refunded.")]
        public StatModifier[] modifiers = Array.Empty<StatModifier>();

        [Header("Layout")]
        public int row;
        public int column;

        /// <summary>Name to show, resolving the override the way the view should.</summary>
        public string ResolveDisplayName()
        {
            if (!string.IsNullOrWhiteSpace(displayNameOverride)) return displayNameOverride;
            if (spell != null && !string.IsNullOrWhiteSpace(spell.displayName)) return spell.displayName;
            if (spell != null && !string.IsNullOrWhiteSpace(spell.spellKey)) return spell.spellKey;
            return nodeId;
        }

        public Sprite ResolveIcon() => iconOverride != null ? iconOverride : null;

        /// <summary>Generated mechanical summary, for the same reason
        /// <see cref="SkillNode.DescribeRank"/> generates its own.</summary>
        public string DescribeEffects()
        {
            var sb = new System.Text.StringBuilder();
            if (spell != null)
                sb.Append($"Unlocks {ResolveDisplayName()}");

            if (modifiers != null)
            {
                for (int i = 0; i < modifiers.Length; i++)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(modifiers[i].Describe());
                }
            }
            return sb.ToString();
        }
    }
}
