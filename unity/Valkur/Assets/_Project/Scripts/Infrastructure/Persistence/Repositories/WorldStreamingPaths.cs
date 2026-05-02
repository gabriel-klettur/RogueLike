using System.IO;
using UnityEngine;
using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Single source of truth for the StreamingAssets directory layout
    /// across worlds. Phase 1 single-world saves keep the legacy flat
    /// layout for <see cref="WorldId.Base"/>; non-base worlds nest under
    /// <c>StreamingAssets/Worlds/&lt;slug&gt;/&lt;subdir&gt;/</c>.
    ///
    /// Every JSON-file repository (buildings, lights, spawners, particles,
    /// zone DB, map editor zones) already follows this convention via
    /// <see cref="WorldStreamingFileRepositoryBase"/>; this helper exposes
    /// the same logic to non-repository callers (WorldLoader, OverlayLoader,
    /// the in-game importers) so all path resolution stays consistent.
    /// </summary>
    public static class WorldStreamingPaths
    {
        /// <summary>
        /// Returns the directory under StreamingAssets that holds the
        /// requested category of data for the given world. Examples:
        ///   (Base, "Maps")  -> &lt;StreamingAssets&gt;/Maps
        ///   (alt, "Maps")  -> &lt;StreamingAssets&gt;/Worlds/&lt;slug&gt;/Maps
        ///   (alt, "Collisions") -> &lt;StreamingAssets&gt;/Worlds/&lt;slug&gt;/Collisions
        /// </summary>
        public static string DirectoryFor(WorldId worldId, string subdir)
        {
            string root = Application.streamingAssetsPath;
            if (worldId.IsBase)
                return Path.Combine(root, subdir);
            return Path.Combine(root, "Worlds", worldId.Slug, subdir);
        }

        /// <summary>Convenience: full path to a single file inside the
        /// world's category directory.</summary>
        public static string FileFor(WorldId worldId, string subdir, string fileName)
            => Path.Combine(DirectoryFor(worldId, subdir), fileName);
    }
}
