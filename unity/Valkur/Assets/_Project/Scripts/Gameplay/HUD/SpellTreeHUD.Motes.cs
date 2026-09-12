using UnityEngine;
using Valkur.Data;
using Valkur.UI.HUD;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// The seven things that make the grimoire move, and the long list of things that do not.
    ///
    /// <para><b>Motes answer an EVENT and nothing else</b> (HUD_VISUAL_LANGUAGE.md R8). The
    /// tempting design here is magic drifting over an open book; it is a screensaver, and the
    /// price of it is that the one burst this panel exists for — a spell being learned — stops
    /// meaning anything. Nothing emits at rest, nothing emits on a refusal, and the only thing
    /// that moves while the player reads is the halo on the single node they can afford.</para>
    ///
    /// <para>Emission is DRIVEN by comparing what the board drew last time against what it
    /// draws now, so every path that can change the model — the card's button, the console, a
    /// level-up, a save being restored — produces the same feedback. A panel that emitted from
    /// inside its own click handler would be silent for all but one of those.</para>
    /// </summary>
    public sealed partial class SpellTreeHUD
    {
        /// <summary>
        /// Compares this refresh against the last and emits for whatever changed. Updates the
        /// cache as it goes, so the same change is never announced twice.
        /// </summary>
        private void EmitForChanges(SpellTree tree)
        {
            if (_motes == null || tree == null || grimoire == null) return;

            bool learnedSomething = false;
            SpellNode learnedNode = null;

            foreach (var node in tree.Nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.nodeId)) continue;

                var now = StateOf(tree, node);
                if (!_lastStates.TryGetValue(node.nodeId, out var before))
                {
                    _lastStates[node.nodeId] = now;
                    continue;
                }
                if (before == now) continue;
                _lastStates[node.nodeId] = now;

                if (now == GrimoireNodeState.Learned)
                {
                    learnedSomething = true;
                    learnedNode = node;
                    EmitLearn(node, tree.accent);
                }
                else if (now == GrimoireNodeState.Available &&
                         before != GrimoireNodeState.Available)
                {
                    // "I have points" became "I have points FOR THIS". Two or three motes,
                    // once — the smallest thing the panel can say.
                    EmitAffordable(node, tree.accent);
                }
            }

            EmitPointsChange();

            if (!learnedSomething) return;

            OpenChains(learnedNode, tree);

            if (IsSchoolComplete(tree))
            {
                EmitCapstone(tree.accent);
                GrimoireAudio.Play(GrimoireSound.Capstone, _style);
            }
            else
            {
                GrimoireAudio.Play(GrimoireSound.Learn, _style);
            }
        }

        /// <summary>The purchase. The only burst allowed to be big.</summary>
        private void EmitLearn(SpellNode node, Color accent)
        {
            if (!_byNode.TryGetValue(node, out var view)) return;
            Vector2 at = BoardToPanel(view.Centre);

            for (int i = 0; i < _style.learnMotes; i++)
            {
                float angle = i / (float)_style.learnMotes * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                _motes.Emit(at, dir * Random.Range(14f, 30f), accent,
                            _style.moteLifeSeconds, HudMoteShapeFor(i), 10f, 1.4f, i % 3 == 0);
            }
        }

        /// <summary>
        /// Lights the chains OUT of the node just bought, once, toward its children. It is
        /// what turns a purchase into a path: the player sees which nodes it opened without
        /// reading a word.
        /// </summary>
        private void OpenChains(SpellNode learned, SpellTree tree)
        {
            if (learned == null) return;
            for (int i = 0; i < _chains.Count; i++)
                if (_chains[i].Parent == learned)
                    _chains[i].View.Flow(_style.chainFlowSeconds);
        }

        private void EmitAffordable(SpellNode node, Color accent)
        {
            if (!_byNode.TryGetValue(node, out var view)) return;
            Vector2 at = BoardToPanel(view.Centre);

            for (int i = 0; i < _style.affordableMotes; i++)
                _motes.Emit(at + new Vector2(Random.Range(-3f, 3f), 0f),
                            new Vector2(Random.Range(-4f, 4f), Random.Range(10f, 18f)),
                            accent, _style.moteLifeSeconds * 0.8f, HudMoteShape.Dot, 6f, 1f);
        }

        /// <summary>Points arriving while the panel is open: they rise to the purse, which
        /// otherwise just jumps.</summary>
        private void EmitPointsChange()
        {
            int now = grimoire.AvailablePoints;
            if (_lastPoints < 0) { _lastPoints = now; return; }
            if (now <= _lastPoints) { _lastPoints = now; return; }
            _lastPoints = now;

            var target = new Vector2(_frame.Title.x + _frame.Title.width - 8,
                                     _frame.Title.y + _frame.Title.height * 0.5f);
            for (int i = 0; i < _style.pointsGainedMotes; i++)
                _motes.Emit(target + new Vector2(Random.Range(-10f, 10f), -12f),
                            new Vector2(Random.Range(-3f, 3f), Random.Range(16f, 26f)),
                            _theme.gold, _style.moteLifeSeconds, HudMoteShape.Plus, 0f, 1.2f, true);
        }

        /// <summary>
        /// Finishing a school. The only event in this window that happens once, and the only
        /// one allowed the long mote life.
        /// </summary>
        private void EmitCapstone(Color accent)
        {
            var rail = _frame.Rail;
            var at = new Vector2(rail.x + rail.width * 0.5f, rail.y + rail.height * 0.5f);

            for (int i = 0; i < _style.capstoneMotes; i++)
            {
                float angle = i / (float)_style.capstoneMotes * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                _motes.Emit(at, dir * Random.Range(20f, 44f), accent,
                            _style.capstoneMoteLifeSeconds, HudMoteShape.Star, 6f, 1.1f, true);
            }
        }

        /// <summary>
        /// Turning to another school. Covers the cut: every node and every chain on the board
        /// is replaced in one frame, which is the same seam <c>WeaponSwapFlashFX</c> exists to
        /// hide.
        /// </summary>
        private void EmitSchoolSwap()
        {
            if (_motes == null) return;
            var tree = ActiveTree();
            if (tree == null) return;

            var board = _frame.Board;
            for (int i = 0; i < _style.schoolSwapMotes; i++)
            {
                float t = i / Mathf.Max(1f, _style.schoolSwapMotes - 1f);
                var at = new Vector2(board.x + 2f, board.y + board.height * t);
                _motes.Emit(at, new Vector2(Random.Range(30f, 60f), Random.Range(-6f, 6f)),
                            tree.accent, _style.schoolSwapSeconds, HudMoteShape.Glow, 0f, 1.6f);
            }
        }

        private bool IsSchoolComplete(SpellTree tree)
        {
            foreach (var node in tree.Nodes)
                if (node != null && !grimoire.IsNodeLearned(node)) return false;
            return tree.Count > 0;
        }

        /// <summary>
        /// Board-local pixels to panel texels. The board is SCALED to fit, so a mote emitted
        /// at a node's board coordinate would otherwise land somewhere else entirely — the
        /// same trap as reading a sprite's rect for a body's size.
        /// </summary>
        private Vector2 BoardToPanel(Vector2 boardLocal)
        {
            var board = _frame.Board;
            return new Vector2(
                board.x + board.width * 0.5f + boardLocal.x * _boardZoom,
                board.y + board.height * 0.5f + boardLocal.y * _boardZoom);
        }

        private static HudMoteShape HudMoteShapeFor(int i)
        {
            switch (i % 3)
            {
                case 0:  return HudMoteShape.Star;
                case 1:  return HudMoteShape.Dot;
                default: return HudMoteShape.Plus;
            }
        }
    }
}
