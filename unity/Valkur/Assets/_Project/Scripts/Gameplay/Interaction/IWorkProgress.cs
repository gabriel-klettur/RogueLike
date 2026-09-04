using UnityEngine;

namespace Valkur.Gameplay.Interaction
{
    /// <summary>
    /// Something the player is working at that has a visible amount left: a seam losing its
    /// charges, a tree losing its durability, a cast filling up.
    ///
    /// <para>It exists so the world-space progress bar can be ONE rig. The bar used to take a
    /// <c>HarvestNode</c> directly, which meant anything that was not a harvest node — a
    /// fishing cast, a lock being picked — could only have a bar by copying it. This project
    /// has already paid for that shape once: mining and chopping drifted into two different
    /// activities precisely because the same idea had more than one implementation, and the
    /// difference lived in code rather than in data.</para>
    ///
    /// <para>Deliberately small. It carries no bounds, no owner and no verb — only what a bar
    /// needs to draw itself. Anything richer would tempt the next caller to reach through the
    /// bar for something else and put the coupling back.</para>
    /// </summary>
    public interface IWorkProgress
    {
        /// <summary>
        /// How full the bar is, 0 to 1. Which DIRECTION that means is the implementer's
        /// business: a seam reports how much is left and drains, a cast reports how far along
        /// it is and fills. The bar draws the number it is given.
        /// </summary>
        float Progress01 { get; }

        /// <summary>
        /// The world point the bar sits above — the top-centre of whatever is being worked.
        /// A point rather than a Bounds because that is all the bar uses, and because a
        /// fishing spot's "thing" is a patch of water with no meaningful box.
        /// </summary>
        Vector2 ProgressAnchor { get; }

        /// <summary>
        /// Whether work is happening right now. The bar shows itself while this is true and
        /// lingers briefly afterwards, so a finished job is readable rather than vanishing on
        /// the frame it completes.
        /// </summary>
        bool IsWorking { get; }
    }
}
