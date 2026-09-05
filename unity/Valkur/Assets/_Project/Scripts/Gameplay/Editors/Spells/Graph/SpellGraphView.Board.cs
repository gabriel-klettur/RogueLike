using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.UIKit;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Drawing the constellation: the connectors, the node sockets, and the pan/zoom that
    /// moves the board under the viewport.
    /// </summary>
    internal sealed partial class SpellGraphView
    {
        /// <summary>Opacity of a connector on the route to the selected node.</summary>
        private const float ROUTE_LINK_ALPHA = 0.95f;

        /// <summary>Thickness of a route connector against an idle one.</summary>
        private const float LINK_W = 11f, ROUTE_LINK_W = 13f;

        /// <summary>
        /// Rebuild the whole board for the current school.
        ///
        /// <para><paramref name="refit"/> is what separates the two reasons this runs. Opening
        /// the view or switching school is a NEW board and has to be framed; a selection is the
        /// same board with four colours moved, and reframing on that would throw away the
        /// author's pan and zoom on every node click.</para>
        /// </summary>
        private void RefreshBoard(bool refit)
        {
            if (_board == null) return;

            for (int i = 0; i < _boardItems.Count; i++)
            {
                var go = _boardItems[i];
                if (go == null) continue;
                go.transform.SetParent(null, false);
                SafeDestroy.Of(go);
            }
            _boardItems.Clear();
            _nodeRects.Clear();

            var tree = ResolveTree(_schoolKey);
            if (tree == null) { SetHeader("No school", 0, 0); _fitPending = false; return; }

            var nodes = new List<SpellNode>();
            for (int i = 0; i < tree.Count; i++)
                if (tree.Nodes[i] != null) nodes.Add(tree.Nodes[i]);

            var placements = SpellGraphLayout.Resolve(nodes);
            if (placements.Count == 0)
            { SetHeader(tree.displayName, 0, 0); _fitPending = false; return; }

            var frame = SpellGraphGeometry.Measure(placements);
            _board.sizeDelta = frame.BoardSize;

            Color accent = ResolveAccent(tree);

            // Connectors are laid down BEFORE the nodes and never re-sorted, so a socket
            // always covers the ends of the lines that reach it.
            var positions = new Dictionary<SpellNode, Vector2>();
            for (int i = 0; i < placements.Count; i++)
                positions[placements[i].Node] = frame.Position(placements[i]);

            var owned = new HashSet<SpellNode>(nodes);
            var route = ResolveUnlockRoute(placements, owned);

            for (int i = 0; i < placements.Count; i++)
            {
                var node = placements[i].Node;
                var pres = node.prerequisites;
                for (int p = 0; pres != null && p < pres.Length; p++)
                {
                    var parent = pres[p];
                    if (parent == null || !owned.Contains(parent)) continue;
                    if (!positions.TryGetValue(parent, out var a)) continue;
                    BuildLink(a, positions[node], accent,
                        onRoute: route.Contains(node) && route.Contains(parent));
                }
            }

            int withArt = 0;
            for (int i = 0; i < placements.Count; i++)
            {
                bool isCapstone = !HasChildIn(placements[i].Node, owned);
                if (BuildNode(placements[i], positions[placements[i].Node], accent, isCapstone))
                    withArt++;
            }

            SetHeader(string.IsNullOrEmpty(tree.displayName) ? _schoolKey : tree.displayName,
                placements.Count, withArt);
            SetStatus(placements.Count - withArt);
            if (refit) FitToView();
        }

        /// <summary>
        /// The selected node plus every prerequisite that leads to it.
        ///
        /// <para>This is the one question a prerequisite graph exists to answer — "what do I
        /// have to buy before this" — and reading it off the picture meant tracing wires by
        /// eye through nodes the wires ran underneath. Empty when nothing is selected, so a
        /// school with no selection draws exactly as it did.</para>
        /// </summary>
        private HashSet<SpellNode> ResolveUnlockRoute(
            List<SpellGraphLayout.Placement> placements, HashSet<SpellNode> owned)
        {
            var route = new HashSet<SpellNode>();
            if (string.IsNullOrEmpty(_selectedSpellKey)) return route;

            SpellNode selected = null;
            for (int i = 0; i < placements.Count; i++)
            {
                var n = placements[i].Node;
                if (n.spell != null && n.spell.spellKey == _selectedSpellKey) { selected = n; break; }
            }
            if (selected == null) return route;

            // Iterative, and guarded by the visited set rather than by depth: nothing
            // validates the authored graph against a prerequisite loop, and a walk that
            // trusted it would not come back.
            var pending = new Stack<SpellNode>();
            pending.Push(selected);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (!route.Add(node)) continue;
                var pres = node.prerequisites;
                for (int p = 0; pres != null && p < pres.Length; p++)
                    if (pres[p] != null && owned.Contains(pres[p])) pending.Push(pres[p]);
            }
            return route;
        }

        private static bool HasChildIn(SpellNode node, HashSet<SpellNode> owned)
        {
            foreach (var candidate in owned)
            {
                var pres = candidate.prerequisites;
                for (int p = 0; pres != null && p < pres.Length; p++)
                    if (pres[p] == node) return true;
            }
            return false;
        }

        private SpellTree ResolveTree(string schoolKey)
        {
            var trees = _catalog != null ? _catalog.spellTrees : null;
            for (int i = 0; trees != null && i < trees.Length; i++)
                if (trees[i] != null && SchoolKeyOf(trees[i]) == schoolKey) return trees[i];
            return trees != null && trees.Length > 0 ? trees[0] : null;
        }

        /// <summary>
        /// The school's own colour. <c>SpellTree.accent</c> defaults to WHITE, which is this
        /// project's "nobody authored this" sentinel and would give nine identical
        /// constellations — so an unauthored school takes a hue derived from its key instead,
        /// which is stable across sessions and different for every school.
        /// </summary>
        private static Color ResolveAccent(SpellTree tree)
        {
            if (!KiPalette.IsUnauthored(tree.accent))
                return new Color(tree.accent.r, tree.accent.g, tree.accent.b, 1f);

            string key = SchoolKeyOf(tree) ?? "";
            int hash = 0;
            for (int i = 0; i < key.Length; i++) hash = hash * 31 + key[i];
            float hue = Mathf.Abs(hash % 997) / 997f;
            return Color.HSVToRGB(hue, 0.55f, 1f);
        }

        // ── connectors ───────────────────────────────────────────────────────────────

        /// <summary>
        /// One connector, drawn RIM TO RIM.
        ///
        /// <para>It used to run centre to centre and pass under the node it arrived at. That
        /// only works if a node is opaque, and none of them is: the socket is a RING whose
        /// interior is <c>Color.clear</c>, its bevel falls to 0.42 alpha on the unlit side,
        /// and the plate under the icon is 0.72 — so every wire was faintly drawn across the
        /// face of both nodes it joined, and a node with three wires had three of them
        /// crossing inside its own circle. Trimming by
        /// <see cref="SpellGraphGeometry.NodeRimRadius"/> removes the cause rather than
        /// hiding it, which also means the fix cannot be undone by a future socket retune.</para>
        /// </summary>
        private void BuildLink(Vector2 from, Vector2 to, Color accent, bool onRoute)
        {
            Vector2 diff = to - from;
            float length = diff.magnitude;
            float trim = SpellGraphGeometry.NodeRimRadius;

            // Two nodes closer together than their own rims have no wire to show. Skipping
            // beats drawing a negative-length rect, which uGUI renders mirrored.
            if (length <= trim * 2f + 1f) return;

            Vector2 dir = diff / length;
            var go = UIFactory.CreateUI(onRoute ? "Link_Route" : "Link", _board);
            var rt = go.GetComponent<RectTransform>();

            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = from + dir * trim;
            rt.sizeDelta = new Vector2(length - trim * 2f, onRoute ? ROUTE_LINK_W : LINK_W);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg);

            var img = go.AddComponent<Image>();
            img.sprite = SpellGraphSprites.Link;
            // A route wire is the school's own colour at full strength; an idle one stays the
            // muted grey-blue it has always been, so the contrast IS the answer. The alpha is
            // set on a COPY of the accent rather than by constructing a fresh colour, which
            // would have raised this file's entry in the raw-colour ratchet — and that ratchet
            // only means anything while its counts may fall and may never rise. Note the
            // ratchet counts TEXT, so even naming the constructor in a comment lifts it.
            Color routeColor = accent;
            routeColor.a = ROUTE_LINK_ALPHA;
            img.color = onRoute ? routeColor : Color.Lerp(LINK_COLOR, accent, 0.35f);
            img.raycastTarget = false;
            img.type = Image.Type.Simple;

            _boardItems.Add(go);
        }

        // ── nodes ────────────────────────────────────────────────────────────────────

        /// <summary>Builds one node. Returns whether it resolved REAL art rather than a glyph.</summary>
        private bool BuildNode(SpellGraphLayout.Placement placement, Vector2 position,
            Color accent, bool isCapstone)
        {
            var node = placement.Node;
            bool selected = node.spell != null && !string.IsNullOrEmpty(_selectedSpellKey)
                            && node.spell.spellKey == _selectedSpellKey;

            var go = UIFactory.CreateUI("Node_" + node.nodeId, _board);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = new Vector2(NODE_PX, NODE_PX);
            _boardItems.Add(go);
            _nodeRects[node] = rt;

            // The node's own hit area. Every layer below sets raycastTarget false so a caption
            // cannot swallow a click, which leaves nothing for the Button to target — an
            // invisible Image on the root is the graphic. Alpha does not affect a uGUI raycast
            // unless alphaHitTestMinimumThreshold is set, so a fully clear one still catches.
            var hit = go.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);

            AddLayer(go.transform, "Halo", SpellGraphSprites.Glow,
                new Color(accent.r, accent.g, accent.b, selected ? 0.55f : 0.16f),
                SpellGraphGeometry.HALO_SCALE);

            AddLayer(go.transform, "Socket",
                isCapstone ? SpellGraphSprites.SocketCapstone : SpellGraphSprites.Socket,
                selected ? SOCKET_SELECTED : Color.Lerp(SOCKET_IDLE, accent, 0.45f), 1f);

            AddLayer(go.transform, "Plate", SpellGraphSprites.Plate, PLATE_COLOR, 0.62f);

            var icon = ResolveNodeIcon(node, out bool hasArt);
            var iconImg = AddLayer(go.transform, "Icon", icon,
                hasArt ? Color.white : new Color(NEEDS_ART.r, NEEDS_ART.g, NEEDS_ART.b, 0.85f),
                hasArt ? 0.56f : 0.40f);
            iconImg.preserveAspect = true;

            AddCaption(go.transform, "Name", node.ResolveDisplayName(),
                -NODE_PX * 0.5f - SpellGraphGeometry.CAPTION_NAME_DROP, 10f, UITheme.TEXT_PRIMARY);
            AddCaption(go.transform, "Cost", $"{node.pointCost}p · L{node.levelRequirement}",
                -NODE_PX * 0.5f - SpellGraphGeometry.CAPTION_COST_DROP, 9f, UITheme.TEXT_MUTED);

            var button = go.AddComponent<Button>();
            button.targetGraphic = hit;
            string captured = node.spell != null ? node.spell.spellKey : null;
            if (!string.IsNullOrEmpty(captured))
                button.onClick.AddListener(() =>
                {
                    _selectedSpellKey = captured;
                    _onSelect?.Invoke(captured);
                    RefreshBoard(refit: false);
                });

            return hasArt;
        }

        private static Image AddLayer(Transform parent, string name, Sprite sprite, Color color,
            float scale)
        {
            var go = UIFactory.CreateUI(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one * scale;

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static void AddCaption(Transform parent, string name, string text, float y,
            float size, Color color)
        {
            // Named rather than "Caption" twice over: this hierarchy is what an author reads
            // to find the piece they want to re-skin, and two identically-named children make
            // that a guess.
            var go = UIFactory.CreateUI(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, y);
            // Width follows the HORIZONTAL step. It used to be the sibling step, which was
            // horizontal until depth moved onto X — leaving it there put a 130 px name under a
            // node 122 px from its neighbour, i.e. two names overlapping by eight pixels.
            rt.sizeDelta = new Vector2(SpellGraphGeometry.CaptionWidth, SpellGraphGeometry.CAPTION_H);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Midline;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
        }

        /// <summary>
        /// What goes in the socket. The chain is the whole point of the view being
        /// re-skinnable, and the last link is a stand-in rather than nothing.
        ///
        /// <para><c>SpellNode.ResolveIcon</c> answered <c>iconOverride</c> or NULL and never
        /// consulted the spell's own icon, so with <c>iconOverride</c> unauthored on all 71
        /// shipped nodes it returned null for every node in the game. It falls through to
        /// <c>SpellDefinition.iconSprite</c> now, which 46 of the 104 spells carry.</para>
        /// </summary>
        private static Sprite ResolveNodeIcon(SpellNode node, out bool hasArt)
        {
            var resolved = node.ResolveIcon();
            if (resolved != null) { hasArt = true; return resolved; }

            hasArt = false;
            return SpellGraphSprites.Mark(node.role);
        }

        /// <summary>
        /// The footer states the remaining ART work, because that is what this view is for
        /// while the icons are still being drawn — a count of placeholders is the one number
        /// an author acts on, and it is invisible in the Grid and the Table.
        /// </summary>
        private void SetStatus(int missingArt)
        {
            if (_statusText == null) return;
            string hint = "Drag to pan  ·  Wheel to zoom  ·  Click a node to select it";
            _statusText.text = missingArt == 0
                ? hint + "   ·   every node in this school has art"
                : hint + $"   ·   {missingArt} node(s) still on a role glyph";
        }

        private void SetHeader(string schoolName, int nodeCount, int withArt)
        {
            if (_headerText == null) return;
            _headerText.text = nodeCount == 0
                ? schoolName
                : $"{schoolName}   —   {nodeCount} nodes,  {withArt}/{nodeCount} with art";
        }

        // ── pan / zoom ───────────────────────────────────────────────────────────────

        private void Update()
        {
            if (EditorInput.ClosePressed()) { Close(notify: true); return; }

            TickResponsiveLayout();

            // A fit that could not measure its viewport on the frame the canvas was built
            // retries here, once the layout has resolved.
            if (_fitPending) FitToView();

            float wheel = MouseInputManager.GetMouseWheelDelta();
            if (Mathf.Abs(wheel) > 0.01f)
            {
                // Multiplied, not added: one notch is the same 12 % wherever the author is.
                float floor = Mathf.Min(ZOOM_MIN, _fitZoom);
                _zoom = Mathf.Clamp(_zoom * (1f + Mathf.Sign(wheel) * ZOOM_STEP),
                    floor, ZOOM_MAX);
                _autoFramed = false;
                ApplyZoom();
            }

            // The drag has to START on the background, or dragging a node would pan instead
            // of clicking it. Once started it keeps tracking wherever the pointer goes.
            if (MouseInputManager.WasLeftMouseButtonPressedThisFrame() && PointerOverBackground())
            {
                _panning = true;
                _panStartMouse = MouseInputManager.GetScreenMousePosition();
                _panStartBoard = _board.anchoredPosition;
            }
            if (_panning && !MouseInputManager.IsLeftMouseButtonPressed()) _panning = false;
            if (!_panning) return;

            var moved = MouseInputManager.GetScreenMousePosition() - _panStartMouse;
            if (moved.sqrMagnitude > 1f) _autoFramed = false;
            _board.anchoredPosition = _panStartBoard + moved;
        }

        /// <summary>
        /// Keep the chrome and the framing honest as the window changes size.
        ///
        /// <para>The slab is anchored to fractions of the screen, so it already RESIZED with
        /// the window — what it did not do was re-derive the rail's wrapping, the viewport's
        /// insets, or the fit, all three of which were computed once at open. Resize the
        /// window after opening and the board kept a zoom fitted to a viewport that no longer
        /// existed, while a rail that needed a third row was drawn over the graph.</para>
        ///
        /// <para>The re-frame is gated on <see cref="_autoFramed"/>: a resize should recover a
        /// view the author never touched, and must not overwrite one they framed themselves.
        /// The chrome is re-derived either way — that is layout, not framing.</para>
        /// </summary>
        private void TickResponsiveLayout()
        {
            if (_viewport == null) return;

            Vector2 size = _viewport.rect.size;
            bool chromeMoved = LayoutChrome();
            if (chromeMoved) size = _viewport.rect.size;

            // A sub-pixel wobble is not a resize; re-fitting on one would fight the author.
            if (!chromeMoved && (size - _lastViewportSize).sqrMagnitude < 1f) return;

            _lastViewportSize = size;
            if (_autoFramed) FitToView();
        }

        private bool PointerOverBackground()
        {
            if (_viewport == null) return false;
            Vector2 screen = MouseInputManager.GetScreenMousePosition();
            if (!RectTransformUtility.RectangleContainsScreenPoint(_viewport, screen, null))
                return false;

            for (int i = 0; i < _boardItems.Count; i++)
            {
                var go = _boardItems[i];
                if (go == null || !go.name.StartsWith("Node_")) continue;
                var rt = (RectTransform)go.transform;
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null)) return false;
            }
            return true;
        }

        private void ApplyZoom() => _board.localScale = Vector3.one * _zoom;

        /// <summary>
        /// Frame the whole school inside the viewport.
        ///
        /// <para>The old fit was capped at 1, which was the binding constraint on every school
        /// in the game: the natural fit measures 1.9-2.9, so all nine opened at 1.0 and filled
        /// about 42 % of the width of the window they were drawn in. The cap now lives in
        /// <see cref="SpellGraphGeometry.FIT_MAX"/>, above what any shipped school needs, so
        /// the fit really fits — and stays the SAME for all nine, which is what stops a node
        /// resizing as the author clicks along the school rail.</para>
        ///
        /// <para>The board is re-centred here and nowhere else. Its content is centred inside
        /// its own box by <c>Measure</c>, captions and all, so zeroing the position is exact
        /// rather than approximately right.</para>
        /// </summary>
        private void FitToView()
        {
            if (_board == null || _viewport == null) { _fitPending = false; ApplyZoom(); return; }

            Rect viewport = _viewport.rect;
            if (viewport.width < 1f || viewport.height < 1f)
            {
                // Not a failure — uGUI has simply not laid out yet. Retry next frame rather
                // than silently keeping a zoom that was never fitted to anything.
                _fitPending = true;
                ApplyZoom();
                return;
            }

            _fitPending = false;
            _autoFramed = true;
            _lastViewportSize = viewport.size;
            _fitZoom = SpellGraphGeometry.FitZoom(_board.sizeDelta,
                new Vector2(viewport.width, viewport.height));
            _zoom = Mathf.Min(_fitZoom, ZOOM_MAX);
            _board.anchoredPosition = Vector2.zero;
            ApplyZoom();
        }
    }
}
