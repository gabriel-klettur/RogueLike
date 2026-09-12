using UnityEngine;
using Valkur.Core.Input;
using Valkur.Data;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// Moving through the constellation without a mouse.
    ///
    /// <para><b>Why it matters more here than on most panels.</b> A grimoire is a PLAN: the
    /// player opens it to compare two nodes three steps apart and decide which branch to
    /// commit a run to. Every other way of reading it — the rail, the card, the filter — was
    /// built and the only way to move between nodes was to find them with a pointer. A window
    /// whose whole content is reachable only by aiming is one a player on a pad cannot use at
    /// all, and one a player on a keyboard uses worse than they could.</para>
    ///
    /// <para><b>The geometry decides, not the list order.</b> Arrow keys pick the nearest node
    /// in that DIRECTION, measured on the board, which is what a player means when they press
    /// right while looking at a tree. Walking the placement list instead would jump across the
    /// board whenever the layout packer reordered siblings — correct by construction and wrong
    /// to the eye.</para>
    ///
    /// <para><b>Input goes through <see cref="InputCompat"/>.</b> Reading
    /// <c>Keyboard.current</c> here would be the regression CLAUDE.md's input section exists to
    /// prevent: it breaks under the recurring 2022.3 Editor event-drop bug, and
    /// <c>InputCompat</c> is the semantic menu facade that ORs both backends and honours
    /// <c>InputBlocker</c>.</para>
    /// </summary>
    public sealed partial class SpellTreeHUD
    {
        /// <summary>
        /// Reads the four arrows and Confirm. Cancel is deliberately NOT read: the character
        /// sheet owns Escape and holds it through <c>EscapeOwnership</c>, and two readers of
        /// one key in an undefined Update order is how a single press closes both the panel and
        /// the window behind it — or neither, depending on the frame.
        /// </summary>
        private void TickNavigation()
        {
            if (InputCompat.NavRightPressed()) StepSelection(Vector2.right);
            else if (InputCompat.NavLeftPressed()) StepSelection(Vector2.left);
            else if (InputCompat.NavUpPressed()) StepSelection(Vector2.up);
            else if (InputCompat.NavDownPressed()) StepSelection(Vector2.down);
            else if (InputCompat.ConfirmPressed()) ConfirmSelection();
        }

        /// <summary>
        /// Moves the selection one node in <paramref name="direction"/>, or seeds it when
        /// nothing is selected yet.
        /// </summary>
        public void StepSelection(Vector2 direction)
        {
            if (_nodes.Count == 0) return;

            if (_selected == null || !_byNode.ContainsKey(_selected))
            {
                SelectNode(SeedSelection());
                return;
            }

            var from = _byNode[_selected].Centre;
            var best = NearestInDirection(from, direction);
            if (best != null) SelectNode(best);
        }

        /// <summary>
        /// Where the keyboard starts: the node the player can actually buy, else the leftmost.
        ///
        /// <para>Starting at index 0 would be the list's answer, not the board's — and the one
        /// node worth landing on is the one an Enter away from being bought.</para>
        /// </summary>
        private SpellNode SeedSelection()
        {
            GrimoireNodeView available = null;
            GrimoireNodeView leftmost = null;

            for (int i = 0; i < _nodes.Count; i++)
            {
                var view = _nodes[i];
                if (view.Node == null || !PassesFilter(view.Node)) continue;

                if (available == null && view.State == GrimoireNodeState.Available)
                    available = view;
                if (leftmost == null || view.Centre.x < leftmost.Centre.x)
                    leftmost = view;
            }
            var chosen = available ?? leftmost;
            return chosen != null ? chosen.Node : null;
        }

        /// <summary>
        /// The nearest node in a direction, scored by how far along that axis it is against
        /// how far off it is.
        ///
        /// <para>Pure distance picks the node diagonally adjacent as often as the one straight
        /// ahead, which reads as the selection wandering. The sideways error is weighted so a
        /// node almost in line wins over a nearer one well off to one side — and anything
        /// BEHIND the direction is refused outright rather than scored badly, or pressing right
        /// at the right-hand edge walks the selection backwards.</para>
        ///
        /// <para>Nodes the filter excludes are skipped: a keyboard that lands on something the
        /// player has just faded out is a keyboard that ignores the control they used.</para>
        /// </summary>
        private SpellNode NearestInDirection(Vector2 from, Vector2 direction)
        {
            const float SidewaysPenalty = 2.2f;

            SpellNode best = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < _nodes.Count; i++)
            {
                var view = _nodes[i];
                if (view.Node == null || view.Node == _selected) continue;
                if (!PassesFilter(view.Node)) continue;

                Vector2 delta = view.Centre - from;
                float along = Vector2.Dot(delta, direction);
                if (along <= 0.5f) continue;                 // behind, or level with, the cursor

                float sideways = Mathf.Abs(delta.x * direction.y - delta.y * direction.x);
                float score = along + sideways * SidewaysPenalty;
                if (score >= bestScore) continue;

                bestScore = score;
                best = view.Node;
            }
            return best;
        }

        /// <summary>
        /// Enter buys the selected node — through the SAME path the card's button uses, so a
        /// purchase from the keyboard produces the motes, the chain flow and the sound, and a
        /// refusal produces the shake and nothing else.
        /// </summary>
        public void ConfirmSelection()
        {
            if (_selected == null) return;
            TryLearnSelected();
        }
    }
}
