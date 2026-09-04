using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// The one asset the runtime loads to find EVERY piece of progression content: the
    /// XP curve, the per-level stat curve, each class's skill tree and every school of
    /// the grimoire.
    ///
    /// It exists because of a failure this project has already paid for twice. A
    /// <c>[SerializeField]</c> on a component that is <c>AddComponent</c>-ed has no way
    /// to be filled — that is exactly how <c>ChatSystem._catalog</c> sat null for the
    /// life of the project and no NPC ever greeted anyone. The player is built the same
    /// way, by <c>EntitySetup</c> on a bare GameObject, so a progression component with
    /// an inspector slot would be null in every build for the same reason.
    ///
    /// So this asset lives under <c>Resources/Progression/</c> and is loaded by path.
    /// The subfolder is mandatory, not tidiness: <c>Resources.LoadAll&lt;T&gt;("")</c> is
    /// a full-tree scan of ~7,400 assets that logs a missing-script error for every
    /// unresolvable one, which is the trap <c>SpawnPlayer</c> fell into.
    /// </summary>
    [CreateAssetMenu(fileName = "ProgressionCatalog", menuName = "Valkur/Progression/Progression Catalog")]
    public sealed class ProgressionCatalog : ScriptableObject
    {
        /// <summary>Path passed to <c>Resources.Load</c>. Kept here so the loader and the
        /// editor seeder cannot disagree about where the asset lives.</summary>
        public const string ResourcePath = "Progression/ProgressionCatalog";

        [Header("Curves")]
        [Tooltip("Drives Experience. Without one the component falls back to its inline " +
                 "baseXp * level^exponent and has no level cap at all.")]
        public XpCurveDefinition xpCurve;

        [Tooltip("Drives the Level stat layer. Without one, levelling grants nothing — " +
                 "which is the state the project shipped in.")]
        public LevelStatCurve levelStatCurve;

        [Header("Trees")]
        [Tooltip("One per playable class, matched on SkillTree.classKey.")]
        public SkillTree[] skillTrees = System.Array.Empty<SkillTree>();

        [Tooltip("Schools of the grimoire, shared by every class.")]
        public SpellTree[] spellTrees = System.Array.Empty<SpellTree>();

        [Header("Currency")]
        [Tooltip("Skill points granted per level-up.")]
        [Min(0)] public int skillPointsPerLevel = 1;

        [Tooltip("Arcane points granted per level-up. Fractional pacing is expressed " +
                 "through arcanePointLevelInterval rather than a float, so the player " +
                 "always receives whole points.")]
        [Min(0)] public int arcanePointsPerGrant = 1;

        [Tooltip("Grant arcane points every N levels. 1 = every level, 2 = every other.")]
        [Min(1)] public int arcanePointLevelInterval = 2;

        [Tooltip("Skill points the character starts with, before any level-up.")]
        [Min(0)] public int startingSkillPoints;

        [Tooltip("Arcane points the character starts with. One is enough to buy a first " +
                 "school root, so the grimoire is never an empty panel on a new run.")]
        [Min(0)] public int startingArcanePoints = 1;

        [Header("Starting kit")]
        [Tooltip("Spell keys every character knows without spending a point. Keep this " +
                 "small — it is the only content the grimoire cannot charge for. The " +
                 "weapon toggle and the basic slash belong here; a nuke does not.")]
        public string[] alwaysKnownSpellKeys = System.Array.Empty<string>();

        private Dictionary<string, SkillTree> _skillByClass;

        /// <summary>The tree for a class key, or null when that class has none.</summary>
        public SkillTree GetSkillTreeForClass(string classKey)
        {
            if (string.IsNullOrEmpty(classKey)) return null;
            EnsureSkillLookup();
            _skillByClass.TryGetValue(classKey, out var tree);
            return tree;
        }

        public SpellTree GetSpellTree(string schoolKey)
        {
            if (string.IsNullOrEmpty(schoolKey) || spellTrees == null) return null;
            foreach (var t in spellTrees)
            {
                if (t != null && string.Equals(t.schoolKey, schoolKey,
                        System.StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            return null;
        }

        /// <summary>Finds the node teaching a spell key, across every school. Used by the
        /// spell bar to explain why a spell is missing.</summary>
        public bool TryFindSpellNode(string spellKey, out SpellTree tree, out SpellNode node)
        {
            tree = null; node = null;
            if (string.IsNullOrEmpty(spellKey) || spellTrees == null) return false;

            foreach (var t in spellTrees)
            {
                if (t == null) continue;
                foreach (var n in t.Nodes)
                {
                    if (n == null || n.spell == null) continue;
                    if (string.Equals(n.spell.spellKey, spellKey,
                            System.StringComparison.OrdinalIgnoreCase))
                    {
                        tree = t; node = n; return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Arcane points granted on reaching <paramref name="newLevel"/>.</summary>
        public int ArcanePointsForLevel(int newLevel)
        {
            int interval = Mathf.Max(1, arcanePointLevelInterval);
            return newLevel % interval == 0 ? Mathf.Max(0, arcanePointsPerGrant) : 0;
        }

        public bool IsAlwaysKnown(string spellKey)
        {
            if (string.IsNullOrEmpty(spellKey) || alwaysKnownSpellKeys == null) return false;
            foreach (var k in alwaysKnownSpellKeys)
            {
                if (string.Equals(k, spellKey, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void EnsureSkillLookup()
        {
            if (_skillByClass != null) return;
            _skillByClass = new Dictionary<string, SkillTree>(System.StringComparer.OrdinalIgnoreCase);
            if (skillTrees == null) return;
            foreach (var t in skillTrees)
            {
                if (t == null || string.IsNullOrEmpty(t.classKey)) continue;
                _skillByClass[t.classKey] = t;
            }
        }

        private void OnValidate() { _skillByClass = null; }
    }
}
