using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Container SO grouping a set of <see cref="SkillNode"/> assets into
    /// one tree (the player's class progression). One asset per class:
    /// <c>WarriorSkillTree</c>, <c>MageSkillTree</c>, etc.
    ///
    /// Lookup by skillId is hashed lazily on first access so reading the
    /// tree from a save file is O(1) per learned skill.
    /// </summary>
    [CreateAssetMenu(fileName = "NewSkillTree", menuName = "Valkur/Data/Skill Tree")]
    public sealed class SkillTree : ScriptableObject
    {
        [Tooltip("Player-facing label for this tree (e.g. 'Warrior Path').")]
        public string displayName;

        [Tooltip("All nodes in this tree. Order is irrelevant for runtime — " +
                 "lookup is by skillId. UI tools can read this list directly to " +
                 "draw the graph.")]
        [SerializeField] private SkillNode[] nodes = System.Array.Empty<SkillNode>();

        public IReadOnlyList<SkillNode> Nodes => nodes;
        public int Count => nodes != null ? nodes.Length : 0;

        private Dictionary<string, SkillNode> _lookup;

        public SkillNode GetById(string skillId)
        {
            EnsureLookup();
            if (string.IsNullOrEmpty(skillId)) return null;
            _lookup.TryGetValue(skillId, out var node);
            return node;
        }

        public bool TryGet(string skillId, out SkillNode node)
        {
            EnsureLookup();
            if (string.IsNullOrEmpty(skillId)) { node = null; return false; }
            return _lookup.TryGetValue(skillId, out node);
        }

        private void EnsureLookup()
        {
            if (_lookup != null) return;
            _lookup = new Dictionary<string, SkillNode>(System.StringComparer.OrdinalIgnoreCase);
            if (nodes == null) return;
            foreach (var n in nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.skillId)) continue;
                _lookup[n.skillId] = n;
            }
        }

        private void OnValidate() { _lookup = null; } // rebuild on next access

#if UNITY_EDITOR
        public void EditorSetNodes(SkillNode[] newNodes)
        {
            nodes = newNodes ?? System.Array.Empty<SkillNode>();
            _lookup = null;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
