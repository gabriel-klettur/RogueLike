using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.UI.HUD
{
    /// <summary>One node placed on the board, in texels, bottom-left origin.</summary>
    public readonly struct SkillNodePlacement
    {
        public readonly SkillNode Node;

        /// <summary>Bottom-left of the node's 32x32 socket.</summary>
        public readonly int X;
        public readonly int Y;

        /// <summary>Centre of the socket. Where an edge starts or ends.</summary>
        public readonly int CentreX;
        public readonly int CentreY;

        /// <summary>Left edge and width of the label column under the socket.</summary>
        public readonly int CellX;
        public readonly int CellWidth;

        public SkillNodePlacement(SkillNode node, int x, int y, int cellX, int cellWidth, int nodeSize)
        {
            Node = node;
            X = x;
            Y = y;
            CellX = cellX;
            CellWidth = cellWidth;
            CentreX = x + nodeSize / 2;
            CentreY = y + nodeSize / 2;
        }
    }

    /// <summary>
    /// One prerequisite drawn as an orthogonal elbow: up from the child, across, up into the
    /// parent. Three segments, any of which may be zero-length when the two share a column.
    /// </summary>
    public readonly struct SkillEdgePlacement
    {
        public readonly SkillNode From;      // the prerequisite, higher on the board
        public readonly SkillNode To;        // the node it opens

        /// <summary>Vertical run leaving the child, in texels: x, bottom y, height.</summary>
        public readonly RectInt Riser;

        /// <summary>Horizontal run joining the two columns. Zero-width when they match.</summary>
        public readonly RectInt Cross;

        /// <summary>Vertical run entering the parent.</summary>
        public readonly RectInt Header;

        public SkillEdgePlacement(SkillNode from, SkillNode to, RectInt riser, RectInt cross, RectInt header)
        {
            From = from;
            To = to;
            Riser = riser;
            Cross = cross;
            Header = header;
        }
    }

    /// <summary>
    /// Turns a <see cref="SkillTree"/>'s authored <c>row</c> / <c>column</c> grid into texel
    /// positions and edge polylines. Pure: no GameObject, no Unity UI, no state — so the whole
    /// shape of the board is provable in EditMode, which matters because uGUI performs no
    /// layout there and a structural probe over the built panel measures nothing.
    ///
    /// <para><b>Why this exists at all.</b> All 35 shipped talent nodes carry a hand-authored
    /// <c>row</c> and <c>column</c> — three roots, three middles each with one prerequisite,
    /// one capstone — and the shipped panel used those two fields as the sort key of a flat
    /// list and threw the shape away. The data supported a real board the whole time.</para>
    ///
    /// <para><b>Row 0 is the TOP.</b> <c>SkillNode.row</c> documents itself as "lower is nearer
    /// the root", and a talent tree is read downward from its roots, so row increases as y
    /// decreases. Texel space is bottom-up, which is why the conversion happens here once
    /// instead of at every call site.</para>
    /// </summary>
    public static class SkillTreeLayout
    {
        /// <summary>Side of a node socket, in texels. The same 32 the ability slots use.</summary>
        public const int NodeSize = 32;

        /// <summary>Distance between two column centres.</summary>
        public const int ColumnPitch = 92;

        /// <summary>Distance between two row centres.</summary>
        public const int RowPitch = 70;

        /// <summary>
        /// Height under a socket reserved for its pips, its cost and its name — three rows that
        /// must not overlap. 3 (pips) + 7 (cost) + 7 (name) plus the gaps between them; it was
        /// 18 and the cost printed over the name.
        /// </summary>
        public const int CellFooterHeight = 26;

        /// <summary>Thickness of an edge. One texel, like every other line in the HUD.</summary>
        public const int EdgeThickness = 1;

        /// <summary>Board margin so an edge elbow never touches the panel frame.</summary>
        public const int Margin = 6;

        /// <summary>
        /// Places every node of <paramref name="tree"/> and every prerequisite edge between
        /// them. Returns the board size in texels.
        /// </summary>
        public static Vector2Int Build(SkillTree tree,
                                       List<SkillNodePlacement> placements,
                                       List<SkillEdgePlacement> edges)
        {
            placements?.Clear();
            edges?.Clear();
            if (tree == null || tree.Count == 0) return Vector2Int.zero;

            if (!Bounds(tree, out int minRow, out int maxRow, out int minCol, out int maxCol))
                return Vector2Int.zero;

            int columns = maxCol - minCol + 1;
            int rows = maxRow - minRow + 1;
            int width = Margin * 2 + columns * ColumnPitch;
            int height = Margin * 2 + rows * RowPitch;

            var byNode = new Dictionary<SkillNode, SkillNodePlacement>(tree.Count);

            foreach (var node in tree.Nodes)
            {
                if (node == null) continue;
                int col = Mathf.Clamp(node.column, minCol, maxCol) - minCol;

                // Row 0 at the top: the first row's cell occupies the highest band of the board.
                int rowFromTop = Mathf.Clamp(node.row, minRow, maxRow) - minRow;
                int cellX = Margin + col * ColumnPitch;
                int cellTop = height - Margin - rowFromTop * RowPitch;
                int y = cellTop - NodeSize;
                int x = cellX + (ColumnPitch - NodeSize) / 2;

                var placement = new SkillNodePlacement(node, x, y, cellX, ColumnPitch, NodeSize);
                placements?.Add(placement);
                byNode[node] = placement;
            }

            if (edges != null) BuildEdges(tree, byNode, edges);
            return new Vector2Int(width, height);
        }

        /// <summary>
        /// The elbow from each node up to each of its prerequisites. Drawn from the CHILD
        /// because that is the direction the player reads it in — "what does this need" — and
        /// because a node with two prerequisites then fans out from one point instead of
        /// having two unrelated lines arrive at it.
        /// </summary>
        private static void BuildEdges(SkillTree tree,
                                       Dictionary<SkillNode, SkillNodePlacement> byNode,
                                       List<SkillEdgePlacement> edges)
        {
            foreach (var node in tree.Nodes)
            {
                if (node == null || node.prerequisites == null) continue;
                if (!byNode.TryGetValue(node, out var child)) continue;

                foreach (var prereq in node.prerequisites)
                {
                    if (prereq == null) continue;
                    if (!byNode.TryGetValue(prereq, out var parent)) continue;

                    int childTop = child.Y + NodeSize;
                    int parentBottom = parent.Y - CellFooterHeight;
                    if (parentBottom <= childTop) continue;   // same row, or authored upside down

                    // The crossbar sits halfway up the gap, so two elbows into the same parent
                    // from different columns share a rail instead of crossing each other.
                    int midY = childTop + (parentBottom - childTop) / 2;

                    var riser = new RectInt(child.CentreX, childTop, EdgeThickness, midY - childTop);

                    int x0 = Mathf.Min(child.CentreX, parent.CentreX);
                    int x1 = Mathf.Max(child.CentreX, parent.CentreX);
                    var cross = new RectInt(x0, midY, Mathf.Max(EdgeThickness, x1 - x0 + EdgeThickness),
                                            EdgeThickness);

                    var header = new RectInt(parent.CentreX, midY, EdgeThickness, parentBottom - midY);

                    edges.Add(new SkillEdgePlacement(prereq, node, riser, cross, header));
                }
            }
        }

        /// <summary>The occupied extent of the authored grid. False when the tree has no node.</summary>
        public static bool Bounds(SkillTree tree, out int minRow, out int maxRow,
                                  out int minCol, out int maxCol)
        {
            minRow = minCol = int.MaxValue;
            maxRow = maxCol = int.MinValue;
            if (tree == null) return false;

            foreach (var node in tree.Nodes)
            {
                if (node == null) continue;
                if (node.row < minRow) minRow = node.row;
                if (node.row > maxRow) maxRow = node.row;
                if (node.column < minCol) minCol = node.column;
                if (node.column > maxCol) maxCol = node.column;
            }

            return minRow != int.MaxValue;
        }
    }
}
