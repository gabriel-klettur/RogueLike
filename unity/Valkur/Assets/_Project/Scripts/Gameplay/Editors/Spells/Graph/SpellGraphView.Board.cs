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
        private void RefreshBoard()
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
            if (tree == null) { SetHeader("No school", 0, 0); return; }

            var nodes = new List<SpellNode>();
            for (int i = 0; i < tree.Count; i++)
                if (tree.Nodes[i] != null) nodes.Add(tree.Nodes[i]);

            var placements = SpellGraphLayout.Resolve(nodes);
            if (placements.Count == 0) { SetHeader(tree.displayName, 0, 0); return; }

            float minCol = float.MaxValue, maxCol = float.MinValue;
            int maxRow = 0;
            for (int i = 0; i < placements.Count; i++)
            {
                minCol = Mathf.Min(minCol, placements[i].Column);
                maxCol = Mathf.Max(maxCol, placements[i].Column);
                maxRow = Mathf.Max(maxRow, placements[i].Row);
            }

            _board.sizeDelta = new Vector2(
                (maxCol - minCol) * COL_SPACING + BOARD_PADDING * 2f,
                maxRow * ROW_SPACING + BOARD_PADDING * 2f);

            Color accent = ResolveAccent(tree);

            // Connectors are laid down BEFORE the nodes and never re-sorted, so a socket
            // always covers the ends of the lines that reach it.
            var positions = new Dictionary<SpellNode, Vector2>();
            for (int i = 0; i < placements.Count; i++)
                positions[placements[i].Node] = BoardPosition(placements[i], minCol, maxRow);

            var owned = new HashSet<SpellNode>(nodes);
            for (int i = 0; i < placements.Count; i++)
            {
                var node = placements[i].Node;
                var pres = node.prerequisites;
                for (int p = 0; pres != null && p < pres.Length; p++)
                {
                    var parent = pres[p];
                    if (parent == null || !owned.Contains(parent)) continue;
                    if (!positions.TryGetValue(parent, out var a)) continue;
                    BuildLink(a, positions[node], accent);
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
            FitToView();
        }

        private Vector2 BoardPosition(SpellGraphLayout.Placement p, float minCol, int maxRow)
        {
            // Row 0 at the TOP, deeper nodes below — the direction every skill tree in the
            // genre reads, and the same direction the outline indents.
            float x = (p.Column - minCol) * COL_SPACING
                      - ((_board.sizeDelta.x - BOARD_PADDING * 2f) * 0.5f);
            float y = ((maxRow * ROW_SPACING) * 0.5f) - p.Row * ROW_SPACING;
            return new Vector2(x, y);
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

        private void BuildLink(Vector2 from, Vector2 to, Color accent)
        {
            var go = UIFactory.CreateUI("Link", _board);
            var rt = go.GetComponent<RectTransform>();

            Vector2 diff = to - from;
            float length = diff.magnitude;
            if (length < 0.01f) { SafeDestroy.Of(go); return; }

            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = from;
            rt.sizeDelta = new Vector2(length, 11f);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg);

            var img = go.AddComponent<Image>();
            img.sprite = SpellGraphSprites.Link;
            img.color = Color.Lerp(LINK_COLOR, accent, 0.35f);
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
                new Color(accent.r, accent.g, accent.b, selected ? 0.55f : 0.16f), 1.85f);

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
                -NODE_PX * 0.5f - 16f, 10f, UITheme.TEXT_PRIMARY);
            AddCaption(go.transform, "Cost", $"{node.pointCost}p · L{node.levelRequirement}",
                -NODE_PX * 0.5f - 29f, 9f, UITheme.TEXT_MUTED);

            var button = go.AddComponent<Button>();
            button.targetGraphic = hit;
            string captured = node.spell != null ? node.spell.spellKey : null;
            if (!string.IsNullOrEmpty(captured))
                button.onClick.AddListener(() =>
                {
                    _selectedSpellKey = captured;
                    _onSelect?.Invoke(captured);
                    RefreshBoard();
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
            rt.sizeDelta = new Vector2(COL_SPACING - 8f, 14f);

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
            if (KeyboardInputManager.WasEscapePressedThisFrame()) { Close(notify: true); return; }

            float wheel = MouseInputManager.GetMouseWheelDelta();
            if (Mathf.Abs(wheel) > 0.01f)
            {
                _zoom = Mathf.Clamp(_zoom + Mathf.Sign(wheel) * ZOOM_STEP, ZOOM_MIN, ZOOM_MAX);
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

            _board.anchoredPosition =
                _panStartBoard + (MouseInputManager.GetScreenMousePosition() - _panStartMouse);
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
        /// Open on the whole school at once.
        ///
        /// <para>Measured before this, a school's board came out 728 px tall against a 590 px
        /// viewport, so the deepest row of every one of the nine was off screen until the
        /// author panned — and a constellation whose whole point is the SHAPE cannot open with
        /// the shape cropped. The fit is capped at 1 so a small school is not blown up past
        /// the size its art is drawn for.</para>
        /// </summary>
        private void FitToView()
        {
            if (_board == null || _viewport == null) { ApplyZoom(); return; }

            Vector2 board = _board.sizeDelta;
            Rect viewport = _viewport.rect;
            if (board.x < 1f || board.y < 1f || viewport.width < 1f || viewport.height < 1f)
            { ApplyZoom(); return; }

            float fit = Mathf.Min(viewport.width / board.x, viewport.height / board.y);
            _zoom = Mathf.Clamp(Mathf.Min(1f, fit), ZOOM_MIN, ZOOM_MAX);
            _board.anchoredPosition = Vector2.zero;
            ApplyZoom();
        }
    }
}
