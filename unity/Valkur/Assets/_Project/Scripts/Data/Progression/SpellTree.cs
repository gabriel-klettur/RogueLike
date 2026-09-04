using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// One school of the grimoire — Fire, Ice, Lightning, Arcane, Light, Dark, Martial —
    /// holding the <see cref="SpellNode"/>s that teach its spells. Bought with ARCANE
    /// POINTS, which is a different currency from the skill tree's points on purpose;
    /// see <see cref="SkillTree"/> for the full argument.
    ///
    /// A school is per ELEMENT and not per class, because a spell is shared: all five
    /// classes can learn Fireball, and a per-class copy of the spell graph would be five
    /// assets drifting apart on the first retune. What makes a mage a mage is which
    /// schools they can afford to go deep in, plus their own class talent tree — not a
    /// private list of spells nobody else may touch.
    ///
    /// <see cref="classAffinities"/> is the seam for class identity without duplication:
    /// a school a class has no affinity for costs more, so the dwarf CAN learn ice magic
    /// and pays for the privilege.
    /// </summary>
    [CreateAssetMenu(fileName = "NewSpellTree", menuName = "Valkur/Progression/Spell Tree")]
    public sealed class SpellTree : ScriptableObject
    {
        [Tooltip("Player-facing school name, e.g. 'Pyromancy'.")]
        public string displayName;

        [Tooltip("Stable id used for save persistence and for the grimoire's tab strip.")]
        public string schoolKey;

        [TextArea(2, 4)]
        public string flavour;

        [Tooltip("Tint used for this school's tab and node frames.")]
        public Color accent = Color.white;

        [Tooltip("Classes with a natural affinity for this school, by " +
                 "PlayerDefinition.playerKey. A class NOT listed here can still learn the " +
                 "school — it just pays the off-affinity surcharge, which is what makes a " +
                 "class identity a tendency rather than a wall.")]
        public string[] classAffinities = System.Array.Empty<string>();

        [Tooltip("Multiplier on every node's cost for a class with no affinity. 1 = no " +
                 "surcharge, which makes the school universal.")]
        [Min(1f)] public float offAffinityCostMultiplier = 2f;

        [SerializeField] private SpellNode[] nodes = System.Array.Empty<SpellNode>();

        public IReadOnlyList<SpellNode> Nodes => nodes;
        public int Count => nodes != null ? nodes.Length : 0;

        private Dictionary<string, SpellNode> _lookup;

        public SpellNode GetById(string nodeId)
        {
            EnsureLookup();
            if (string.IsNullOrEmpty(nodeId)) return null;
            _lookup.TryGetValue(nodeId, out var node);
            return node;
        }

        public bool TryGet(string nodeId, out SpellNode node)
        {
            EnsureLookup();
            if (string.IsNullOrEmpty(nodeId)) { node = null; return false; }
            return _lookup.TryGetValue(nodeId, out node);
        }

        public bool HasAffinity(string classKey)
        {
            if (string.IsNullOrEmpty(classKey) || classAffinities == null) return false;
            foreach (var k in classAffinities)
            {
                if (string.Equals(k, classKey, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// What <paramref name="node"/> actually costs the given class. Rounded UP so the
        /// surcharge can never round away to nothing on a 1-point node, which is most of
        /// them.
        /// </summary>
        public int ResolveCost(SpellNode node, string classKey)
        {
            if (node == null) return 0;
            if (HasAffinity(classKey)) return node.pointCost;
            return Mathf.CeilToInt(node.pointCost * Mathf.Max(1f, offAffinityCostMultiplier));
        }

        private void EnsureLookup()
        {
            if (_lookup != null) return;
            _lookup = new Dictionary<string, SpellNode>(System.StringComparer.OrdinalIgnoreCase);
            if (nodes == null) return;
            foreach (var n in nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.nodeId)) continue;
                _lookup[n.nodeId] = n;
            }
        }

        private void OnValidate() { _lookup = null; }

#if UNITY_EDITOR
        public void EditorSetNodes(SpellNode[] newNodes)
        {
            nodes = newNodes ?? System.Array.Empty<SpellNode>();
            _lookup = null;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
