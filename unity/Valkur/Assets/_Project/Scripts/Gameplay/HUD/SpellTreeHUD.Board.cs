using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay.Spells;
using Valkur.UI.HUD;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// The constellation: the rail that picks a school, the nodes, and the chains between them.
    /// </summary>
    public sealed partial class SpellTreeHUD
    {
        private readonly List<GrimoireNodeView> _nodes = new List<GrimoireNodeView>();
        private readonly List<GrimoireLinkView> _links = new List<GrimoireLinkView>();
        private readonly Dictionary<SpellNode, GrimoireNodeView> _byNode =
            new Dictionary<SpellNode, GrimoireNodeView>();

        /// <summary>
        /// Both ends of each chain plus the view that draws it.
        ///
        /// <para>It held only the PARENT at first, which is all a purchase needs to light what
        /// it opened. The role filter needs the other end too — a chain with one surviving end
        /// is the path TO something the player is looking at and must stay lit — and resolving
        /// the child by searching the node list is how a parallel-array bug gets written.</para>
        /// </summary>
        private struct ChainEnds
        {
            public SpellNode Parent;
            public SpellNode Child;
            public GrimoireLinkView View;
        }

        private readonly List<ChainEnds> _chains = new List<ChainEnds>();

        private float _boardZoom = 1f;

        // ── Refresh ───────────────────────────────────────────────────────

        private void Refresh()
        {
            if (_pixels == null || grimoire == null) return;

            RebuildRail();

            var tree = ActiveTree();
            if (tree == null)
            {
                _title.SetText(GrimoireText.NoSchools.ToUpperInvariant());
                _purse.SetText(string.Empty);
                _flavour.text = string.Empty;
                ClearBoard();
                RefreshCard();
                return;
            }

            _title.SetText(tree.displayName.ToUpperInvariant());
            _purse.SetText(GrimoireText.Points(grimoire.AvailablePoints).ToUpperInvariant());
            _flavour.text = tree.flavour;

            RebuildBoard(tree);
            RefreshCard();
            EmitForChanges(tree);
        }

        // ── The rail ──────────────────────────────────────────────────────

        private void RebuildRail()
        {
            ClearChildren(_railRoot);
            _railRows.Clear();

            var trees = grimoire.Trees;
            int row = _style.railRowTexels;
            int gap = _style.railRowGapTexels;

            // The rail is shrunk to the rows it actually has, TOP-aligned inside its band, so
            // no empty cut-out is drawn under the last school. It happens here rather than in
            // BuildRail because this is the first point at which the schools are known.
            var band = _frame.Rail;
            int rows = 0;
            for (int i = 0; i < trees.Count; i++) if (trees[i] != null) rows++;
            int used = rows > 0 ? rows * row + (rows - 1) * gap : band.height;
            int railHeight = Mathf.Clamp(used, 0, band.height);
            HudRect.Place(_railRoot, band.x, band.y + (band.height - railHeight),
                          band.width, railHeight);
            // Rows are laid out against the ROOT just placed, not against the band, or they
            // would hang off the top of the hole they live in.
            int width = band.width;
            int top = railHeight;

            for (int i = 0; i < trees.Count; i++)
            {
                var tree = trees[i];
                if (tree == null) continue;

                int y = top - (i + 1) * row - i * gap;
                if (y < 0) break;   // more schools than rail: the rail is sized, not squeezed

                bool active = i == _activeSchool;
                var entry = new RailRow();

                var rowGo = new GameObject("School_" + tree.schoolKey, typeof(RectTransform));
                rowGo.transform.SetParent(_railRoot, false);
                HudRect.Place((RectTransform)rowGo.transform, 0, y, width, row);

                entry.Background = rowGo.AddComponent<Image>();
                entry.Background.sprite = _ink.RowPlate;
                entry.Background.type = Image.Type.Sliced;
                entry.Background.color = active
                    ? new Color(tree.accent.r * 0.30f, tree.accent.g * 0.30f, tree.accent.b * 0.30f, 1f)
                    : _theme.stoneDark;

                var button = rowGo.AddComponent<Button>();
                button.targetGraphic = entry.Background;
                button.transition = Selectable.Transition.None;
                int captured = i;
                button.onClick.AddListener(() => SelectSchool(captured));

                // SHAPE, not only brightness: measured on the shipped accents, the active
                // tab's luminance against an idle one ranged from 2.2 : 1 (Umbramancy) to
                // 6.5 : 1 (Radiance), so colour alone said "open" three times more loudly on
                // one school than on another (R6).
                var filetGo = new GameObject("Filet", typeof(RectTransform));
                filetGo.transform.SetParent(rowGo.transform, false);
                HudRect.Place((RectTransform)filetGo.transform, 0, 0, 2, row);
                entry.Filet = filetGo.AddComponent<Image>();
                entry.Filet.color = tree.accent;
                entry.Filet.enabled = active;
                entry.Filet.raycastTarget = false;

                // The school's FULL name. Nine horizontal tabs truncated eight of the nine
                // shipped names; a vertical rail has the width to spell them and scales to a
                // twelfth school with no redesign.
                //
                // It must NOT wrap. Captured live, "Formas Marciales" broke onto a second line
                // that overflowed the row and landed on top of the count under it, and "Ritos
                // Verdes" did the same onto "Fuego Interior" — so the one thing the rail exists
                // to say was the thing it destroyed. One line, shrunk to fit, never truncated.
                // Measured, not guessed: "FORMAS MARCIALES" inks 62 texels in the small face,
                // and at a 9-texel sigil with an 18-texel counter its box was 56 — it ran into
                // its own count in the rendered frame. Seven and fifteen give it 65.
                //
                // The arithmetic lives in GrimoireGeometry so a test can ask it of the shipped
                // data and the shipped font, with no canvas: the defect above was found by
                // LOOKING at a frame, which is the expensive way to learn a number.
                int countWidth = GrimoireGeometry.RailCountTexels;

                // ONE type size for the whole rail, and the pixel face rather than Arial.
                //
                // Per-label auto-fit is what produced nine different sizes in one column —
                // "Fulgor" large, "Umbramancia" tiny, three names wrapped onto their own
                // counter — because each label solved its own width in isolation. A bitmap
                // face cannot do that: every glyph is the same size by construction, so a
                // column of nine rows reads as one list. Upper-cased because the small face is
                // capitals only, and its Spanish glyphs (Á É Í Ó Ú Ñ) are the capitals too.
                // The school's own mark. Nine identical slabs told apart by a word is a list;
                // nine faces is a rail you can navigate without reading.
                // NATIVE size, not whatever is left over. A sigil is authored 9x9 and drawn
                // with point filtering, so at 7 texels the sampler simply DROPS two of its
                // nine rows and two of its columns — the X loses arms and the snowflake loses
                // points. Scarcity in a mark is a COVERAGE problem (element size against the
                // grid it is drawn into), never a detail problem, and the two are different
                // numbers living in different files.
                int sigilSize = GrimoireGeometry.RailSigilTexels;
                entry.Sigil = HudRect.MakeImage("Sigil", rowGo.transform,
                    _ink.Sigil(tree.schoolKey), GrimoireGeometry.RailInset,
                    (row - sigilSize) / 2, sigilSize, sigilSize);
                entry.Sigil.color = active
                    ? tree.accent
                    : new Color(tree.accent.r * 0.55f, tree.accent.g * 0.55f,
                                tree.accent.b * 0.55f, 1f);

                int nameX = GrimoireGeometry.RailNameX;
                entry.NameInk = HudPixelText.Create(rowGo.transform, "Name", _art,
                    HudFontFace.Small, HudTextAlign.Left, nameX, (row - 5) / 2 + 1,
                    GrimoireGeometry.RailNameBox(width), 5);
                entry.NameInk.SetColour(active ? _theme.text : _theme.textDim);
                entry.NameInk.SetText(tree.displayName.ToUpperInvariant());

                // The count shares the row rather than sitting under it: a row tall enough for
                // two stacked lines is a rail that fits six schools instead of nine.
                entry.Count = HudPixelText.Create(rowGo.transform, "Count", _art,
                    HudFontFace.Small, HudTextAlign.Right, width - 4 - countWidth,
                    (row - 5) / 2 + 1, countWidth, 5);
                entry.Count.SetColour(active ? _theme.gold : _theme.textDisabled);
                entry.Count.SetText(KnownCount(tree) + "/" + tree.Count);

                _railRows.Add(entry);
            }
        }

        private int KnownCount(SpellTree tree)
        {
            int known = 0;
            foreach (var node in tree.Nodes)
                if (node != null && grimoire.IsNodeLearned(node)) known++;
            return known;
        }

        // ── The board ─────────────────────────────────────────────────────

        private void ClearBoard()
        {
            ClearChildren(_boardRoot);
            _nodes.Clear();
            _links.Clear();
            _byNode.Clear();
            _chains.Clear();
        }

        private void RebuildBoard(SpellTree tree)
        {
            ClearBoard();

            var placements = SpellGraphLayout.Resolve(tree.Nodes);
            if (placements.Count == 0) return;

            var board = GrimoireGeometry.Measure(placements, _style);
            var vp = BoardViewportRect();
            var viewport = new Vector2(vp.width, vp.height);
            _boardZoom = GrimoireGeometry.FitZoom(board.Size, viewport, 4f);
            _boardRoot.localScale = new Vector3(_boardZoom, _boardZoom, 1f);

            float captionWidth = GrimoireGeometry.CaptionWidth(_style);

            // Links first, so a chain is drawn UNDER every socket it touches.
            for (int i = 0; i < placements.Count; i++)
            {
                var node = placements[i].Node;
                if (node == null || node.prerequisites == null) continue;
                for (int p = 0; p < node.prerequisites.Length; p++)
                {
                    if (node.prerequisites[p] == null) continue;
                    _chains.Add(new ChainEnds
                    {
                        Parent = node.prerequisites[p],
                        Child = node,
                        View = GrimoireLinkView.Create(_boardRoot, _style.linkTexels, _style),
                    });
                }
            }

            for (int i = 0; i < placements.Count; i++)
            {
                var placement = placements[i];
                if (placement.Node == null) continue;

                var view = GrimoireNodeView.Create(_boardRoot, placement.Node, _style.nodeTexels,
                                                   _style.captionTexels, captionWidth, _art,
                                                   _style);
                view.Place(board.Position(placement));
                view.SetPulsePeriod(_style.availablePulseSeconds);
                view.Clicked += OnNodeClicked;
                view.Hovered += OnNodeHovered;
                view.Unhovered += OnNodeUnhovered;

                _nodes.Add(view);
                _byNode[placement.Node] = view;
            }

            SpanLinks(placements, board);
            PaintBoard(tree);
        }

        /// <summary>
        /// Stretches each chain between its two sockets. Done in a second pass because a link
        /// declared before its child's view existed cannot know where it ends.
        /// </summary>
        private void SpanLinks(List<SpellGraphLayout.Placement> placements,
                               GrimoireGeometry.Board board)
        {
            float rim = GrimoireGeometry.RimRadius(_style);
            int cursor = 0;

            for (int i = 0; i < placements.Count; i++)
            {
                var node = placements[i].Node;
                if (node == null || node.prerequisites == null) continue;

                for (int p = 0; p < node.prerequisites.Length; p++)
                {
                    var parent = node.prerequisites[p];
                    if (parent == null) continue;
                    if (cursor >= _chains.Count) return;

                    var link = _chains[cursor++].View;
                    _links.Add(link);

                    if (!_byNode.TryGetValue(parent, out var parentView))
                    {
                        // A prerequisite in another school: there is nothing on this board to
                        // draw a chain to, and inventing an anchor would draw a line to a
                        // place that means nothing.
                        link.gameObject.SetActive(false);
                        continue;
                    }

                    link.Span(parentView.Centre, board.Position(placements[i]), rim);
                }
            }
        }

        private void PaintBoard(SpellTree tree)
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                var view = _nodes[i];
                var node = view.Node;
                if (node == null) continue;

                var state = StateOf(tree, node);
                view.Paint(state, tree.accent, ResolveIcon(node),
                           node.ResolveDisplayName(), ShortReasonOf(tree, node), _theme);
            }

            for (int i = 0; i < _chains.Count; i++)
            {
                var state = StateOf(tree, _chains[i].Parent);
                _chains[i].View.Paint(state == GrimoireNodeState.Learned,
                                      state == GrimoireNodeState.Available,
                                      tree.accent, _theme.outline);
            }

            ApplyFilterToBoard();
            RefreshBoardSelection();
        }

        /// <summary>
        /// The node's icon, BAKED to the size actually drawn. The 73 shipped icons are 320 and
        /// 1024 px, bilinear, no mipmaps, inside an atlas; minified into a ~40 px socket by the
        /// sampler they shimmer (R10). The baker refuses on a null graphics device, which is
        /// what a batch-mode test run has, so the raw sprite is the fallback.
        /// </summary>
        private Sprite ResolveIcon(SpellNode node) => ResolveIcon(node, BoardIconPixels());

        /// <summary>Pixel size a node's icon is DRAWN at on the board.</summary>
        private int BoardIconPixels() => Mathf.Max(8, Mathf.RoundToInt(
            _style.nodeTexels * 0.62f * _boardZoom * _pixelScale));

        /// <summary>
        /// The node's icon, BAKED to the size actually drawn, and cached BY THAT SIZE.
        ///
        /// <para>The cache was keyed by node alone, so the detail card reused the socket's
        /// bake: measured, a texture baked for 27 px drawn inside a 64 px box — a 2.4x
        /// magnification, which is the aliasing R10 exists to prevent with the sign reversed.
        /// Two consumers at two sizes need two bakes, and the key has to say so.</para>
        ///
        /// <para>The baker refuses on a null graphics device — which is what a batch test run
        /// has — so the raw sprite is the fallback.</para>
        /// </summary>
        private Sprite ResolveIcon(SpellNode node, int px)
        {
            var sprite = node != null ? node.ResolveIcon() : null;
            if (sprite == null || !HudTextureBaker.CanBake) return sprite;

            string key = node.nodeId + "@" + px;
            if (_bakedIcons.TryGetValue(key, out var baked) && baked != null) return baked;

            var tex = HudTextureBaker.Icon(sprite, px);
            if (tex == null) return sprite;

            var made = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                     new Vector2(0.5f, 0.5f), HudArt.SpritePixelsPerUnit, 0,
                                     SpriteMeshType.FullRect);
            made.name = key;
            _bakedIcons[key] = made;
            return made;
        }

        private readonly Dictionary<string, Sprite> _bakedIcons =
            new Dictionary<string, Sprite>();

        private void RefreshBoardSelection()
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                var view = _nodes[i];
                bool selected = _selected != null && view.Node == _selected;
                // Selection is a one-texel gold filet around the socket, NOT a tint: the
                // socket's colour already carries the school and the state, and a third
                // meaning on the same pixels is how a readout stops being readable.
                var outline = view.GetComponent<Outline>();
                if (selected && outline == null)
                    outline = view.gameObject.AddComponent<Outline>();
                if (outline != null)
                {
                    outline.enabled = selected;
                    outline.effectColor = _theme.gold;
                    outline.effectDistance = new Vector2(1f, -1f);
                }
            }
        }

        private void TickBoard(float dt)
        {
            for (int i = 0; i < _nodes.Count; i++) _nodes[i].Tick(dt);

            var tree = ActiveTree();
            var accent = tree != null ? tree.accent : Color.white;
            for (int i = 0; i < _links.Count; i++) _links[i].Tick(dt, accent);
        }

        // ── Input ─────────────────────────────────────────────────────────

        private void OnNodeClicked(GrimoireNodeView view) => SelectNode(view.Node);

        /// <summary>
        /// Hover previews, click commits — ALWAYS, not only until the first click.
        ///
        /// <para>It was guarded by <c>_selected == null</c>, so the card followed the pointer
        /// until the player chose something and then stopped: comparing two nodes became
        /// impossible at exactly the moment it starts to matter, which is after you have a
        /// candidate.</para>
        /// </summary>
        private void OnNodeHovered(GrimoireNodeView view) => PreviewNode(view.Node);

        /// <summary>
        /// Leaving a node puts the card back on what is SELECTED, or on its empty state. A
        /// card that kept showing the last thing the pointer crossed is a card that no longer
        /// answers "what am I about to buy".
        /// </summary>
        private void OnNodeUnhovered(GrimoireNodeView view) => RefreshCard();
    }
}
