using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Editors.Spells
{
    /// <summary>
    /// Pins how the Spells Editor's constellation is FRAMED — the orientation, the board box,
    /// and the auto-fit — over the shipped grimoire.
    ///
    /// <para>None of this was pinned before, and layout is exactly the kind of thing that
    /// regresses without failing: the view spent its whole life opening every school at 42 %
    /// of the width of its own window because a <c>Min(1f, fit)</c> clamp made the fit inert,
    /// and nothing said so. The assertions here are on CHARACTERISTICS — depth runs right,
    /// the box contains the drawn pixels, all nine schools land at one scale — rather than on
    /// literal pixel values, so retuning a spacing is allowed and breaking a guarantee is not.
    /// </para>
    /// </summary>
    public sealed class SpellGraphFramingTests
    {
        /// <summary>The slab is 92 % x 90 % of the screen, less the header, rail and footer.</summary>
        private static readonly Vector2 Viewport1080 = new Vector2(1754f, 842f);

        private static ProgressionCatalog LoadCatalog()
        {
            var catalog = Resources.Load<ProgressionCatalog>("Progression/ProgressionCatalog");
            Assert.IsNotNull(catalog, "Resources/Progression/ProgressionCatalog.asset is missing.");
            return catalog;
        }

        private static List<SpellTree> ShippedTrees()
        {
            var trees = new List<SpellTree>();
            var authored = LoadCatalog().spellTrees;
            for (int i = 0; authored != null && i < authored.Length; i++)
                if (authored[i] != null) trees.Add(authored[i]);
            Assert.IsNotEmpty(trees, "The shipped catalog carries no spell trees.");
            return trees;
        }

        private static List<SpellGraphLayout.Placement> PlacementsOf(SpellTree tree)
        {
            var nodes = new List<SpellNode>();
            for (int i = 0; i < tree.Count; i++)
                if (tree.Nodes[i] != null) nodes.Add(tree.Nodes[i]);
            return SpellGraphLayout.Resolve(nodes);
        }

        // ── orientation ──────────────────────────────────────────────────────────────

        [Test]
        public void Depth_RunsLeftToRight_AndSlotsRunDownward()
        {
            var frame = SpellGraphGeometry.Measure(new List<SpellGraphLayout.Placement>
            {
                new SpellGraphLayout.Placement { Row = 0, Column = 0f },
                new SpellGraphLayout.Placement { Row = 1, Column = 0f },
                new SpellGraphLayout.Placement { Row = 0, Column = 1f },
            });

            Vector2 root = frame.Position(new SpellGraphLayout.Placement { Row = 0, Column = 0f });
            Vector2 deeper = frame.Position(new SpellGraphLayout.Placement { Row = 1, Column = 0f });
            Vector2 sibling = frame.Position(new SpellGraphLayout.Placement { Row = 0, Column = 1f });

            Assert.Greater(deeper.x, root.x, "A prerequisite step must move RIGHT.");
            Assert.AreEqual(root.y, deeper.y, 0.001f, "Depth must not move a node vertically.");
            Assert.Less(sibling.y, root.y, "The next packing slot must move DOWN.");
            Assert.AreEqual(root.x, sibling.x, 0.001f, "A sibling must not move a node sideways.");
        }

        [Test]
        public void EveryShippedSchool_IsWiderThanItIsTall()
        {
            // The whole reason depth moved onto X. A school taller than it is wide would mean
            // the packer had produced something the viewport's 2.08 aspect cannot use.
            foreach (var tree in ShippedTrees())
            {
                var frame = SpellGraphGeometry.Measure(PlacementsOf(tree));
                Assert.Greater(frame.BoardSize.x, frame.BoardSize.y,
                    $"'{tree.schoolKey}' frames taller than wide; depth-on-X buys nothing there.");
            }
        }

        // ── the box is the drawn extent ──────────────────────────────────────────────

        [Test]
        public void BoardBox_ContainsEveryNodesHaloAndCaptions()
        {
            foreach (var tree in ShippedTrees())
            {
                var placements = PlacementsOf(tree);
                if (placements.Count == 0) continue;

                var frame = SpellGraphGeometry.Measure(placements);
                Vector2 half = frame.BoardSize * 0.5f;

                for (int i = 0; i < placements.Count; i++)
                {
                    Vector2 p = frame.Position(placements[i]);
                    string who = $"{tree.schoolKey}/{placements[i].Node?.nodeId ?? "?"}";

                    Assert.GreaterOrEqual(p.x - SpellGraphGeometry.ReachSide, -half.x - 0.01f,
                        $"{who} is drawn past the LEFT edge of the board box.");
                    Assert.LessOrEqual(p.x + SpellGraphGeometry.ReachSide, half.x + 0.01f,
                        $"{who} is drawn past the RIGHT edge of the board box.");
                    Assert.LessOrEqual(p.y + SpellGraphGeometry.ReachUp, half.y + 0.01f,
                        $"{who}'s halo is drawn above the TOP edge of the board box.");
                    Assert.GreaterOrEqual(p.y - SpellGraphGeometry.ReachDown, -half.y - 0.01f,
                        $"{who}'s captions are drawn below the BOTTOM edge of the board box.");
                }
            }
        }

        [Test]
        public void ReachesAreDerived_FromTheConstantsTheDrawingUses()
        {
            // If a reach is ever hardcoded rather than derived, retuning the halo or a caption
            // moves the picture and leaves the frame behind — the fit then crops silently.
            Assert.AreEqual(SpellGraphGeometry.NODE_PX * SpellGraphGeometry.HALO_SCALE * 0.5f,
                SpellGraphGeometry.HaloReach, 0.001f);
            Assert.AreEqual(
                SpellGraphGeometry.NODE_PX * 0.5f + SpellGraphGeometry.CAPTION_COST_DROP
                + SpellGraphGeometry.CAPTION_H * 0.5f,
                SpellGraphGeometry.CaptionReach, 0.001f);
            Assert.Greater(SpellGraphGeometry.ReachDown, SpellGraphGeometry.ReachUp,
                "Captions hang below and nothing hangs above, so the box is asymmetric.");
        }

        [Test]
        public void CaptionsCannotTouch_TheNeighbourOnTheHorizontalAxis()
        {
            // Depth is the horizontal step now, so it — not the sibling step — is what has to
            // clear a caption. This is the assertion that fails if the two are ever swapped
            // back without the caption width following.
            Assert.Less(SpellGraphGeometry.CaptionWidth, SpellGraphGeometry.DEPTH_SPACING,
                "A caption is at least as wide as the gap to the next depth level.");
        }

        // ── the fit ──────────────────────────────────────────────────────────────────

        [Test]
        public void Fit_LeavesTheAuthoredMargin_AndNeverOverflows()
        {
            foreach (var tree in ShippedTrees())
            {
                var frame = SpellGraphGeometry.Measure(PlacementsOf(tree));
                float zoom = SpellGraphGeometry.FitZoom(frame.BoardSize, Viewport1080);
                Vector2 drawn = frame.BoardSize * zoom;

                Assert.LessOrEqual(drawn.x,
                    Viewport1080.x - SpellGraphGeometry.VIEW_MARGIN * 2f + 0.01f,
                    $"'{tree.schoolKey}' overflows the viewport horizontally.");
                Assert.LessOrEqual(drawn.y,
                    Viewport1080.y - SpellGraphGeometry.VIEW_MARGIN * 2f + 0.01f,
                    $"'{tree.schoolKey}' overflows the viewport vertically.");
            }
        }

        [Test]
        public void Fit_ActuallyFills_RatherThanBeingClampedInert()
        {
            // The defect this replaces: a Min(1f, fit) clamp meant every school opened at 1.0
            // and filled ~42 % of the width. A fit that fills neither axis past two thirds is
            // that same failure back again, whatever the number in the clamp says.
            foreach (var tree in ShippedTrees())
            {
                var frame = SpellGraphGeometry.Measure(PlacementsOf(tree));
                float zoom = SpellGraphGeometry.FitZoom(frame.BoardSize, Viewport1080);
                float fillX = frame.BoardSize.x * zoom / Viewport1080.x;
                float fillY = frame.BoardSize.y * zoom / Viewport1080.y;

                Assert.Greater(Mathf.Max(fillX, fillY), 0.66f,
                    $"'{tree.schoolKey}' fills only {fillX:P0} x {fillY:P0} of the viewport.");
            }
        }

        [Test]
        public void Fit_IsTheSameScale_ForEveryShippedSchool()
        {
            // FIT_MAX exists to be the binding constraint everywhere, so a node is the same
            // size on all nine tabs. A school whose own fit fell below it would render smaller
            // than its neighbours and nothing else would report it.
            foreach (var tree in ShippedTrees())
            {
                var frame = SpellGraphGeometry.Measure(PlacementsOf(tree));
                float zoom = SpellGraphGeometry.FitZoom(frame.BoardSize, Viewport1080);
                Assert.AreEqual(SpellGraphGeometry.FIT_MAX, zoom, 0.0001f,
                    $"'{tree.schoolKey}' does not reach FIT_MAX, so it opens at a different "
                    + "node size than the other schools.");
            }
        }

        [Test]
        public void FitMax_KeepsHeadroom_OverTheTightestShippedSchool()
        {
            float tightest = float.MaxValue;
            string worst = "?";
            foreach (var tree in ShippedTrees())
            {
                var frame = SpellGraphGeometry.Measure(PlacementsOf(tree));
                float natural = Mathf.Min(
                    (Viewport1080.x - SpellGraphGeometry.VIEW_MARGIN * 2f) / frame.BoardSize.x,
                    (Viewport1080.y - SpellGraphGeometry.VIEW_MARGIN * 2f) / frame.BoardSize.y);
                if (natural < tightest) { tightest = natural; worst = tree.schoolKey; }
            }

            Assert.Greater(tightest, SpellGraphGeometry.FIT_MAX * 1.02f,
                $"'{worst}' fits at {tightest:F2} against FIT_MAX {SpellGraphGeometry.FIT_MAX:F2} "
                + "— under 2 % of headroom, so the next spacing tune breaks the uniform scale.");
        }

        // ── connectors clear the nodes ───────────────────────────────────────────────

        [Test]
        public void NodeRim_IsDerivedFromTheSocketSprite()
        {
            // If the rim is ever hardcoded, retuning the socket ring leaves the wires
            // ending somewhere the node no longer is.
            Assert.AreEqual(
                SpellGraphGeometry.NODE_PX * 0.5f * SpellGraphSprites.SOCKET_OUTER_R,
                SpellGraphGeometry.NodeRimRadius, 0.001f);
        }

        [Test]
        public void EveryShippedLink_ClearsBothNodesItJoins()
        {
            // The defect: links ran centre to centre under nodes that are not opaque — the
            // socket interior is Color.clear, its bevel bottoms out at 0.42 alpha and the
            // plate at 0.72 — so each wire was drawn across the face of both its endpoints.
            // A trimmed link needs the two rims to be further apart than the wire is long.
            float rim = SpellGraphGeometry.NodeRimRadius;

            foreach (var tree in ShippedTrees())
            {
                var placements = PlacementsOf(tree);
                var frame = SpellGraphGeometry.Measure(placements);

                var owned = new HashSet<SpellNode>();
                for (int i = 0; i < placements.Count; i++) owned.Add(placements[i].Node);

                var at = new Dictionary<SpellNode, Vector2>();
                for (int i = 0; i < placements.Count; i++)
                    at[placements[i].Node] = frame.Position(placements[i]);

                int edges = 0;
                for (int i = 0; i < placements.Count; i++)
                {
                    var node = placements[i].Node;
                    var pres = node.prerequisites;
                    for (int p = 0; pres != null && p < pres.Length; p++)
                    {
                        var parent = pres[p];
                        if (parent == null || !owned.Contains(parent)) continue;
                        edges++;
                        float span = Vector2.Distance(at[parent], at[node]);
                        Assert.Greater(span, rim * 2f + 1f,
                            $"'{tree.schoolKey}' edge {parent.nodeId} -> {node.nodeId} spans "
                            + $"{span:F1} px, which two {rim:F1} px rims swallow — the wire "
                            + "would be dropped rather than drawn.");
                    }
                }
                Assert.Greater(edges, 0, $"'{tree.schoolKey}' has no prerequisite edges at all.");
            }
        }

        // ── responsive chrome ────────────────────────────────────────────────────────

        [Test]
        public void RailColumns_KeepEveryTabReadable()
        {
            int schools = ShippedTrees().Count;
            foreach (float width in new[] { 320f, 520f, 900f, 1738f, 3000f })
            {
                int cols = SpellGraphGeometry.RailColumns(width, schools);
                Assert.GreaterOrEqual(cols, 1);
                Assert.LessOrEqual(cols, schools, "More columns than tabs leaves empty slots.");
                if (cols > 1)
                    Assert.GreaterOrEqual(width / cols, SpellGraphGeometry.MIN_TAB_W - 0.01f,
                        $"At {width} px the rail packs {cols} tabs into "
                        + $"{width / cols:F0} px each.");
            }
        }

        [Test]
        public void RailColumns_WidenWithTheWindow_AndNeverExceedTheTabCount()
        {
            int schools = ShippedTrees().Count;
            int narrow = SpellGraphGeometry.RailColumns(400f, schools);
            int wide = SpellGraphGeometry.RailColumns(1738f, schools);
            Assert.Less(narrow, wide, "A wider window must fit more tabs per row.");
            Assert.AreEqual(schools, wide,
                "At the shipped 1080p slab width all nine schools fit on one row.");
        }

        [Test]
        public void ChromeTopInset_GrowsWithTheRail_SoTabsNeverOverlapTheBoard()
        {
            int schools = ShippedTrees().Count;
            float previous = -1f;
            foreach (float width in new[] { 3000f, 1738f, 900f, 520f, 320f })
            {
                int cols = SpellGraphGeometry.RailColumns(width, schools);
                int rows = SpellGraphGeometry.RailRows(schools, cols);
                float inset = SpellGraphGeometry.ChromeTopInset(
                    SpellGraphGeometry.RailHeight(rows));

                Assert.GreaterOrEqual(rows * cols, schools, "The rail cannot fit every school.");
                Assert.Greater(inset, SpellGraphGeometry.HEADER_H,
                    "The viewport starts above the rail it is supposed to clear.");
                Assert.GreaterOrEqual(inset, previous,
                    "A narrower window needs more rail rows, so a deeper inset.");
                previous = inset;
            }
        }

        [Test]
        public void RailHeight_IsZero_ForNoTabsAndOneRowForFew()
        {
            Assert.AreEqual(0f, SpellGraphGeometry.RailHeight(0), 0.001f);
            Assert.AreEqual(SpellGraphGeometry.RAIL_ROW_H,
                SpellGraphGeometry.RailHeight(1), 0.001f);
            Assert.AreEqual(SpellGraphGeometry.RAIL_ROW_H * 2f
                + SpellGraphGeometry.RAIL_ROW_SPACING,
                SpellGraphGeometry.RailHeight(2), 0.001f);
            Assert.AreEqual(0, SpellGraphGeometry.RailRows(0, 5));
        }

        [Test]
        public void Fit_StillFrames_WhenTheRailTakesThreeRows()
        {
            // The narrow-window case the old hardcoded -100 inset could not survive: the
            // board must still be framed once the rail has eaten more of the slab.
            int schools = ShippedTrees().Count;
            int cols = SpellGraphGeometry.RailColumns(320f, schools);
            float inset = SpellGraphGeometry.ChromeTopInset(
                SpellGraphGeometry.RailHeight(SpellGraphGeometry.RailRows(schools, cols)));
            var viewport = new Vector2(320f, 842f - inset - SpellGraphGeometry.FOOTER_H);

            foreach (var tree in ShippedTrees())
            {
                var frame = SpellGraphGeometry.Measure(PlacementsOf(tree));
                float zoom = SpellGraphGeometry.FitZoom(frame.BoardSize, viewport);
                Assert.Greater(zoom, 0f, $"'{tree.schoolKey}' cannot be framed at all.");
                Assert.LessOrEqual(frame.BoardSize.x * zoom,
                    viewport.x - SpellGraphGeometry.VIEW_MARGIN * 2f + 0.01f,
                    $"'{tree.schoolKey}' overflows a narrow viewport.");
                Assert.LessOrEqual(frame.BoardSize.y * zoom,
                    viewport.y - SpellGraphGeometry.VIEW_MARGIN * 2f + 0.01f,
                    $"'{tree.schoolKey}' overflows a short viewport.");
            }
        }

        [Test]
        public void FitZoom_IsInertRatherThanWrong_WhenTheViewportIsNotLaidOutYet()
        {
            // uGUI resolves nothing on the frame a canvas is built, so this really is reached.
            // Returning 1 keeps the board visible; the view retries the fit next frame.
            Assert.AreEqual(1f,
                SpellGraphGeometry.FitZoom(new Vector2(800f, 400f), Vector2.zero), 0.0001f);
        }
    }
}
