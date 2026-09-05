using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Where every node of a school sits on the constellation, in abstract (column, row)
    /// slots. Pure arithmetic over the prerequisite graph — no Unity UI, so it can be
    /// measured without a canvas.
    ///
    /// <para>ROW IS DEPTH, COLUMN IS PACKING. Depth is what the data really says: a node sits
    /// one row below the deepest prerequisite that reaches it. Column is not in the data at
    /// all — <c>SpellNode.column</c> is <c>0</c> on all 71 shipped nodes and had no reader
    /// anywhere in the project — so it is COMPUTED by tidy-tree packing: leaves take
    /// consecutive slots and a parent centres over its children. The nine spell trees have
    /// ZERO merges (measured), so for them that packing is exact and no two edges can cross.
    /// </para>
    ///
    /// <para>AUTHORED POSITIONS WIN, PER SCHOOL. The moment any node of a school carries a
    /// non-zero <c>column</c>, that whole school is treated as hand-laid and the packing steps
    /// aside. Per school rather than per node, because a layout half-computed and half-authored
    /// is the worst of both — the packer would spread nodes into slots an author had claimed.
    /// This is also what finally makes <c>column</c> mean something: it was the twelfth field
    /// in this project to be authored, serialised, shown in the inspector and read by nobody.
    /// </para>
    /// </summary>
    internal static class SpellGraphLayout
    {
        /// <summary>One node's place on the board.</summary>
        internal struct Placement
        {
            public SpellNode Node;
            /// <summary>0 at a root, +1 per prerequisite step. Drives the vertical axis.</summary>
            public int Row;
            /// <summary>Fractional slot across. Siblings are whole numbers apart.</summary>
            public float Column;
        }

        /// <summary>
        /// Resolve one school. Returns placements in DRAW order — parents before children —
        /// so a caller can wire connectors as it goes.
        /// </summary>
        public static List<Placement> Resolve(IReadOnlyList<SpellNode> nodes)
        {
            var result = new List<Placement>();
            if (nodes == null || nodes.Count == 0) return result;

            var owned = new HashSet<SpellNode>();
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i] != null) owned.Add(nodes[i]);
            if (owned.Count == 0) return result;

            var children = new Dictionary<SpellNode, List<SpellNode>>();
            var roots = new List<SpellNode>();
            BuildAdjacency(nodes, owned, children, roots);

            var rows = new Dictionary<SpellNode, int>();
            foreach (var node in owned) rows[node] = DepthOf(node, owned, rows, null);

            if (HasAuthoredColumns(nodes))
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    var node = nodes[i];
                    if (node == null) continue;
                    result.Add(new Placement { Node = node, Row = rows[node], Column = node.column });
                }
                return result;
            }

            PackTidyTree(roots, children, rows, result);
            return result;
        }

        private static bool HasAuthoredColumns(IReadOnlyList<SpellNode> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i] != null && nodes[i].column != 0) return true;
            return false;
        }

        private static void BuildAdjacency(IReadOnlyList<SpellNode> nodes, HashSet<SpellNode> owned,
            Dictionary<SpellNode, List<SpellNode>> children, List<SpellNode> roots)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null) continue;

                bool hasParent = false;
                var pres = node.prerequisites;
                for (int p = 0; pres != null && p < pres.Length; p++)
                {
                    var parent = pres[p];
                    if (parent == null || !owned.Contains(parent)) continue;
                    hasParent = true;
                    if (!children.TryGetValue(parent, out var list))
                        children[parent] = list = new List<SpellNode>();
                    if (!list.Contains(node)) list.Add(node);
                }
                if (!hasParent) roots.Add(node);
            }

            roots.Sort(BySibling);
            foreach (var list in children.Values) list.Sort(BySibling);
        }

        /// <summary>
        /// Siblings order by <c>row</c> then id. That is the ONLY job the authored <c>row</c>
        /// field has — it is a seed ordinal, not a depth (a shipped node carries <c>row: 8</c>
        /// in a school five deep), so reading it as a Y coordinate would scatter every tree.
        /// </summary>
        private static int BySibling(SpellNode a, SpellNode b)
        {
            int byRow = a.row.CompareTo(b.row);
            return byRow != 0 ? byRow : string.CompareOrdinal(a.nodeId, b.nodeId);
        }

        /// <summary>
        /// One more than the deepest prerequisite inside this school. The <paramref name="path"/>
        /// set is the cycle guard: nothing validates the authored graph against a loop, and a
        /// loop here would recurse until the stack ran out.
        /// </summary>
        private static int DepthOf(SpellNode node, HashSet<SpellNode> owned,
            Dictionary<SpellNode, int> memo, HashSet<SpellNode> path)
        {
            if (memo.TryGetValue(node, out int cached)) return cached;

            path ??= new HashSet<SpellNode>();
            if (!path.Add(node)) return 0;

            int depth = 0;
            var pres = node.prerequisites;
            for (int p = 0; pres != null && p < pres.Length; p++)
            {
                var parent = pres[p];
                if (parent == null || !owned.Contains(parent)) continue;
                depth = Mathf.Max(depth, DepthOf(parent, owned, memo, path) + 1);
            }

            path.Remove(node);
            memo[node] = depth;
            return depth;
        }

        /// <summary>
        /// Leaves take consecutive slots; a parent centres over its children. Roots are laid
        /// left to right with a gap between their subtrees.
        ///
        /// <para>A node with TWO parents is emitted under the first one that reaches it and
        /// centred between them afterwards. The spell trees have none, but each of the five
        /// skill trees has exactly one, and a packer that emitted such a node twice would draw
        /// the same spell in two places.</para>
        /// </summary>
        private static void PackTidyTree(List<SpellNode> roots,
            Dictionary<SpellNode, List<SpellNode>> children,
            Dictionary<SpellNode, int> rows, List<Placement> into)
        {
            var columns = new Dictionary<SpellNode, float>();
            var emitted = new HashSet<SpellNode>();
            float nextLeafSlot = 0f;

            for (int i = 0; i < roots.Count; i++)
            {
                Assign(roots[i]);
                nextLeafSlot += 1f;   // a clear gap between one root's subtree and the next
            }

            // Emitted parents-before-children, so a caller drawing connectors as it walks
            // always has the far end placed already.
            for (int i = 0; i < roots.Count; i++) Emit(roots[i]);

            float Assign(SpellNode node)
            {
                if (columns.TryGetValue(node, out float known)) return known;

                if (!children.TryGetValue(node, out var kids) || kids.Count == 0)
                {
                    columns[node] = nextLeafSlot;
                    nextLeafSlot += 1f;
                    return columns[node];
                }

                float min = float.MaxValue, max = float.MinValue;
                for (int i = 0; i < kids.Count; i++)
                {
                    float c = Assign(kids[i]);
                    min = Mathf.Min(min, c);
                    max = Mathf.Max(max, c);
                }
                columns[node] = (min + max) * 0.5f;
                return columns[node];
            }

            void Emit(SpellNode node)
            {
                if (!emitted.Add(node)) return;
                into.Add(new Placement { Node = node, Row = rows[node], Column = columns[node] });
                if (!children.TryGetValue(node, out var kids)) return;
                for (int i = 0; i < kids.Count; i++) Emit(kids[i]);
            }
        }
    }
}
