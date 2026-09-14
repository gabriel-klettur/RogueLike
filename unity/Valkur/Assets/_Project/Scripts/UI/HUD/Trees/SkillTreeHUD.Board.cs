using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.HUD;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The board itself: one socket per node, one elbow per prerequisite, and the pass that
    /// repaints all of it from <see cref="LearnedSkills"/>.
    /// </summary>
    public sealed partial class SkillTreeHUD
    {
        private SkillTree _boundTree;
        private bool _boardBuilt;
        private SkillNodeView _selected;

        private readonly Dictionary<SkillNode, List<Image>> _edgesByParent =
            new Dictionary<SkillNode, List<Image>>();
        private readonly HashSet<SkillNode> _openParents = new HashSet<SkillNode>();

        private SkillNode _lightingEdgeFrom;
        private float _lightingUntil;

        /// <summary>The node the card is showing, or null.</summary>
        public SkillNodeView Selected => _selected;

        /// <summary>
        /// Rebuilds the whole window for the currently bound tree, then repaints it.
        ///
        /// <para>The sockets are rebuilt only when the TREE changes; a purchase repaints them.
        /// The old panel destroyed and recreated every row on every purchase, which is the shape
        /// that cost the items editor 3 480 ms — cheap at seven nodes and the wrong habit.</para>
        /// </summary>
        public void Rebuild()
        {
            if (!_built) return;

            // The branch on the board: the class path, or a shared branch picked by its tab.
            var tree = CurrentTree();

            // `!_boardBuilt` is not redundant with the tree comparison: on the very first open with
            // no player resolved, both sides are null, the board is never built, and Repaint then
            // returns on its own null check — so the window came up as empty chrome with no title
            // and nothing saying why. A class with no tree must SAY it has no tree.
            if (tree != _boundTree || !_boardBuilt)
            {
                _boundTree = tree;
                RebuildBoard(tree);
                _boardBuilt = true;
            }

            Repaint();
        }

        private void RebuildBoard(SkillTree tree)
        {
            DisposeViews();

            ComputeGeometry(tree);
            BakeStone();

            // Re-place the board and the card: the window's size follows the tree.
            //
            // The pixel root is resized DIRECTLY and never through HudRect.Place, which ends with
            // `localScale = Vector3.one`. That is right for every other rect in the window — a
            // texel-space child must not carry a scale — and fatal for the one rect whose whole
            // job is the counter-scale. Measured before the fix: the panel was sized at 908x544
            // (454 texels x 2) while its content stayed at scale 1, so the board drew at half
            // size in the bottom-left quadrant of its own stone.
            int pad = _style.paddingTexels;
            _pixels.sizeDelta = new Vector2(_widthTexels, _heightTexels);
            HudRect.Place((RectTransform)_stone.transform, 0, 0, _widthTexels, _heightTexels);
            HudRect.Place(_boardRoot, pad, FooterTop(), _boardWidth, _boardHeight);
            HudRect.Place(_cardRoot, _widthTexels - pad - _style.cardWidthTexels, FooterTop(),
                          _style.cardWidthTexels, _boardHeight);
            LayoutHeaderAndFooter();
            RebuildTabs();
            LayoutCard();

            // LAST, not first: every Place above wipes localScale, so the counter-scale has to be
            // re-applied after the layout rather than before it.
            Refit(force: true);

            if (tree == null || tree.Count == 0)
            {
                _titleLabel.SetText(SkillText.NoTree.ToUpperInvariant());
                _flavourLabel.SetText(string.Empty);
                _pointsLabel.SetText("0");
                _footerLabel.SetText(string.Empty);
                _cardRoot.gameObject.SetActive(false);
                return;
            }

            SkillTreeLayout.Build(tree, _placements, _edges);

            // Edges first, so a socket always draws over the line that arrives at it.
            for (int i = 0; i < _edges.Count; i++) BuildEdge(_edges[i]);

            for (int i = 0; i < _placements.Count; i++)
            {
                var view = new SkillNodeView(_placements[i].Node, _placements[i], _boardRoot,
                                             _art, _theme, _style, _pixelScale);
                view.Clicked += OnNodeClicked;
                view.ApplyIcon(_pixelScale);
                _views.Add(view);
            }

            if (_views.Count > 0) Select(_views[0], silent: true);
        }

        private void BuildEdge(SkillEdgePlacement edge)
        {
            if (!_edgesByParent.TryGetValue(edge.From, out var list))
            {
                list = new List<Image>(3);
                _edgesByParent[edge.From] = list;
            }

            AddSegment(edge.Riser, list);
            AddSegment(edge.Cross, list);
            AddSegment(edge.Header, list);
        }

        private void AddSegment(RectInt seg, List<Image> into)
        {
            if (seg.width <= 0 || seg.height <= 0) return;
            var img = Tinted("Edge", _boardRoot, _art.White, seg.x, seg.y, seg.width, seg.height,
                             Color.Lerp(_theme.recess, _theme.stoneLight, 0.75f));
            _edgeImages.Add(img);
            into.Add(img);
        }

        private void LayoutHeaderAndFooter()
        {
            int pad = _style.paddingTexels;
            int titleY = _heightTexels - pad - _style.titleBarTexels;
            int medSize = _style.titleBarTexels;
            int medX = _widthTexels - pad - medSize;

            HudRect.Place(_titleLabel.rectTransform, pad + 2, titleY,
                          _widthTexels - pad * 2 - medSize - 8, _style.titleBarTexels);
            HudRect.Place(_pointsMedallion.rectTransform, medX, titleY, medSize, medSize);
            HudRect.Place(_pointsMedallionGlow.rectTransform, medX, titleY, medSize, medSize);
            HudRect.Place(_pointsLabel.rectTransform, medX, titleY, medSize, _style.titleBarTexels);
            HudRect.Place(_flavourLabel.rectTransform, pad + 2, titleY - _style.flavourTexels,
                          _widthTexels - pad * 2, _style.flavourTexels);
            HudRect.Place(_footerLabel.rectTransform, pad + 2, pad, _widthTexels / 2,
                          _style.footerTexels);
            HudRect.Place(_respecButton.rectTransform, _boardWidth + pad - 108, pad, 108,
                          _style.footerTexels);
        }

        // ── Repaint ───────────────────────────────────────────────────────────

        /// <summary>
        /// Repaints every node, every edge, the header and the footer from the live model. The
        /// only pass that writes a colour on this window.
        /// </summary>
        private void Repaint()
        {
            if (skills == null || _boundTree == null) return;

            int level = ResolveLevel();
            _titleLabel.SetText(TreeName().ToUpperInvariant());
            _flavourLabel.SetText(_boundTree.flavour ?? string.Empty);
            _pointsLabel.SetText(skills.AvailablePoints.ToString());
            _footerLabel.SetText(SkillText.Spent(skills.SpentPoints, _boundTree.TotalPointCost()));
            PaintTabs();

            _openParents.Clear();
            for (int i = 0; i < _views.Count; i++)
            {
                var view = _views[i];
                int rank = skills.RankOf(view.Node.skillId);
                int maxRank = Mathf.Max(1, view.Node.maxRank);

                _locks.Clear();
                bool free = skills.CollectLockReasons(view.Node, level, _locks);

                var state = ResolveState(free, rank, maxRank, _locks);
                view.SetState(state, rank, view.Node.pointCost, LevelGate(_locks));

                if (rank >= maxRank) _openParents.Add(view.Node);
            }

            PaintEdges();
            RefreshCard();
        }

        /// <summary>
        /// The node's state, derived from the SAME lock list the card prints — so the padlock on
        /// the board and the sentence beside it can never disagree.
        ///
        /// <para>The order matters and mirrors <c>CollectLockReasons</c>: a node blocked by both
        /// its level and its cost reads as level-blocked, because that is the one the player can
        /// do nothing about right now. A node blocked ONLY by cost keeps the look of an available
        /// one on purpose — the shape says "reachable" and the red number says "not yet".</para>
        /// </summary>
        private static SkillNodeState ResolveState(bool free, int rank, int maxRank,
                                                   List<SkillLock> locks)
        {
            if (rank >= maxRank) return SkillNodeState.Maxed;
            if (free) return rank > 0 ? SkillNodeState.Partial : SkillNodeState.Available;

            for (int i = 0; i < locks.Count; i++)
                if (locks[i].Kind == SkillLockKind.Level) return SkillNodeState.LockedByLevel;
            for (int i = 0; i < locks.Count; i++)
                if (locks[i].Kind == SkillLockKind.Prerequisite) return SkillNodeState.LockedByPrerequisite;
            return SkillNodeState.LockedByPoints;
        }

        /// <summary>
        /// The level a locked node is waiting for, or 0. Read off the lock list rather than
        /// recomputed, so the number on the padlock and the sentence on the card cannot differ.
        /// </summary>
        private static int LevelGate(List<SkillLock> locks)
        {
            for (int i = 0; i < locks.Count; i++)
                if (locks[i].Kind == SkillLockKind.Level) return locks[i].Required;
            return 0;
        }

        /// <summary>
        /// An edge is lit when its prerequisite is FINISHED — which is exactly the condition that
        /// opens the node below it, so the lit path and the reachable path are the same drawing.
        /// Gold, because a path the player has opened is the third and last place gold appears.
        /// </summary>
        private void PaintEdges()
        {
            foreach (var pair in _edgesByParent)
            {
                bool open = _openParents.Contains(pair.Key);
                bool lighting = open && pair.Key == _lightingEdgeFrom && Time.unscaledTime < _lightingUntil;
                Color c = open ? (lighting ? _theme.text : _theme.gold)
                               : Color.Lerp(_theme.recess, _theme.stoneLight, 0.75f);
                var list = pair.Value;
                for (int i = 0; i < list.Count; i++) list[i].color = c;
            }
        }

        private void TickEdgeLight(float now)
        {
            if (_lightingEdgeFrom == null) return;
            if (now < _lightingUntil) { PaintEdges(); return; }
            _lightingEdgeFrom = null;
            PaintEdges();
        }

        private string TreeName()
        {
            if (_boundTree == null) return SkillText.Title;
            return string.IsNullOrWhiteSpace(_boundTree.displayName) ? SkillText.Title
                                                                     : _boundTree.displayName;
        }

        // ── Interaction ───────────────────────────────────────────────────────

        private void OnNodeClicked(SkillNodeView view)
        {
            if (view == null) return;
            if (_selected != view) { Select(view, silent: false); return; }

            // A second click on the SELECTED node buys a rank. One click selects, two buy: a
            // talent is permanent and a single stray click must not spend a point.
            TryLearn(view);
        }

        private void Select(SkillNodeView view, bool silent)
        {
            _selected = view;
            DisarmRespec();
            RefreshCard();
        }

        /// <summary>
        /// Buys one rank, and produces the two events the old panel had no answer for: the rank
        /// itself, and the PATH it may have opened below.
        /// </summary>
        private void TryLearn(SkillNodeView view)
        {
            if (skills == null || view == null) return;

            int level = ResolveLevel();
            int before = skills.RankOf(view.Node.skillId);
            bool wasParentDone = before >= Mathf.Max(1, view.Node.maxRank);

            if (!skills.TryLearn(view.Node, level, out _))
            {
                RefuseAt(view);
                return;
            }

            int after = skills.RankOf(view.Node.skillId);
            view.SnapPip(after - 1, Time.unscaledTime);
            EmitRankBurst(view);

            bool nowParentDone = after >= Mathf.Max(1, view.Node.maxRank);
            if (nowParentDone && !wasParentDone)
            {
                _lightingEdgeFrom = view.Node;
                _lightingUntil = Time.unscaledTime + _style.edgeLightSeconds;
                EmitUnlockRings(view.Node);
                if (IsCapstone(view.Node)) EmitCapstoneFlash(view);
            }

            // The model already repainted us through OnLoadoutChanged, synchronously, inside
            // TryLearn — but that path is a SUBSCRIPTION, and this method's correctness must not
            // depend on one being live. Repainting seven nodes twice costs nothing; a board that
            // silently stops updating when a bind goes wrong costs the whole window.
            Repaint();
        }

        /// <summary>A node nothing else depends on, at the deepest authored row.</summary>
        private bool IsCapstone(SkillNode node)
        {
            if (_boundTree == null) return false;
            foreach (var other in _boundTree.Nodes)
            {
                if (other == null || other.prerequisites == null) continue;
                foreach (var prereq in other.prerequisites)
                    if (prereq == node) return false;
            }
            return true;
        }

        private void DisposeViews()
        {
            for (int i = 0; i < _views.Count; i++)
            {
                _views[i].Dispose();
                DestroyUI(_views[i].Root != null ? _views[i].Root.gameObject : null);
            }
            _views.Clear();
            _placements.Clear();
            _edges.Clear();

            for (int i = 0; i < _edgeImages.Count; i++)
                DestroyUI(_edgeImages[i] != null ? _edgeImages[i].gameObject : null);
            _edgeImages.Clear();
            _edgesByParent.Clear();
            _selected = null;
        }
    }
}
