using System.Collections.Generic;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// What the board says about one node. Six states, and every one of them is distinguished
    /// by something OTHER than its colour as well — HUD_VISUAL_LANGUAGE.md R6.
    ///
    /// <para>The redundancy is the CAPTION under the socket. A badge glyph was the first
    /// design and it answers a smaller question: a lock icon says "locked" and the player
    /// still has to click to learn why, while "NIVEL 15" says it from across the board. The
    /// caption is already there to carry the node's name, so the second line costs nothing
    /// and carries the whole reason.</para>
    /// </summary>
    public enum GrimoireNodeState
    {
        /// <summary>Bought. Socket filled, icon at full colour.</summary>
        Learned = 0,

        /// <summary>Buyable right now. Socket hollow with a slow halo; nothing else breathes.</summary>
        Available = 1,

        /// <summary>Everything else is met and the purse is short.</summary>
        NeedsPoints = 2,

        /// <summary>The character is not high enough.</summary>
        NeedsLevel = 3,

        /// <summary>A node earlier in the chain has not been bought.</summary>
        NeedsPrerequisite = 4,

        /// <summary>The asset is broken — no id, or no node at all.</summary>
        Malformed = 5,
    }

    /// <summary>
    /// Turns the model's list of locks into the ONE state the board draws, plus the facts the
    /// caption needs. Pure and static so the whole decision is testable with no canvas, which
    /// matters here more than usual: uGUI performs no layout in Edit Mode, so anything left
    /// inside the view is effectively unpinnable.
    /// </summary>
    public static class GrimoireNodeStatus
    {
        /// <summary>
        /// The state to draw, from the locks the model collected.
        ///
        /// <para><b>The precedence is the model's own order, deliberately.</b>
        /// <c>KnownSpells.CollectLockReasons</c> names level, then prerequisites, then points
        /// — the order the player can act on them — and the board shows the FIRST, so the
        /// socket and the caption's first phrase agree. Picking a different one here would
        /// mean the board and the card disagreed about why a node is shut.</para>
        /// </summary>
        public static GrimoireNodeState Resolve(bool learned, IReadOnlyList<SpellLock> locks)
        {
            if (learned) return GrimoireNodeState.Learned;
            if (locks == null || locks.Count == 0) return GrimoireNodeState.Available;

            for (int i = 0; i < locks.Count; i++)
            {
                switch (locks[i].Kind)
                {
                    case SpellLockKind.Malformed:    return GrimoireNodeState.Malformed;
                    case SpellLockKind.AlreadyKnown: return GrimoireNodeState.Learned;
                    case SpellLockKind.Level:        return GrimoireNodeState.NeedsLevel;
                    case SpellLockKind.Prerequisite: return GrimoireNodeState.NeedsPrerequisite;
                    case SpellLockKind.Points:       return GrimoireNodeState.NeedsPoints;
                }
            }
            return GrimoireNodeState.Available;
        }

        /// <summary>Whether the player can act on this node at all.</summary>
        public static bool IsBuyable(GrimoireNodeState state) =>
            state == GrimoireNodeState.Available;

        /// <summary>
        /// Whether the socket is drawn FILLED. Only a bought node is: an available one is an
        /// empty socket with a light behind it, which is what makes the board read as a thing
        /// being filled in rather than a menu with some rows greyed out.
        /// </summary>
        public static bool IsFilled(GrimoireNodeState state) =>
            state == GrimoireNodeState.Learned;

        /// <summary>
        /// Whether the icon keeps its own colours. A locked node shows its art desaturated so
        /// the board's colour belongs to PROGRESS and not to whichever spells happen to have
        /// colourful icons.
        /// </summary>
        public static bool IsIconLit(GrimoireNodeState state) =>
            state == GrimoireNodeState.Learned || state == GrimoireNodeState.Available;

        /// <summary>
        /// Whether the link INTO this node is drawn as an open path. A chain is lit when its
        /// parent is bought, which is what lets the player read the frontier of what they can
        /// reach without a single word — the same job the minimap's fog frontier does.
        /// </summary>
        public static bool LinkIsOpen(GrimoireNodeState parentState) =>
            parentState == GrimoireNodeState.Learned;
    }
}
