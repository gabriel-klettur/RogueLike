using System;
using System.IO;
using UnityEngine;
using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Shared scaffolding for raw-JSON repositories backed by a single file
    /// per world inside <c>StreamingAssets</c>. Lights, spawners, particles,
    /// and buildings all follow the same file layout and atomic-write
    /// pattern; centralising it here keeps the per-domain subclasses small
    /// and makes a future change to the IO mechanism (e.g. compression,
    /// hashing, server backend) a one-place edit.
    ///
    /// Path layout:
    ///   - <see cref="WorldId.Base"/> -> StreamingAssets/&lt;Subdir&gt;/&lt;FileName&gt;
    ///     (legacy flat layout; preserves byte-compatibility with existing
    ///     builds and saves).
    ///   - other worlds -> StreamingAssets/Worlds/&lt;slug&gt;/&lt;Subdir&gt;/&lt;FileName&gt;.
    ///
    /// Atomic writes: tmp file + <see cref="File.Replace(string, string, string)"/>
    /// with sidecar .bak so a crash mid-write cannot truncate the previous content.
    /// </summary>
    public abstract class WorldStreamingFileRepositoryBase
    {
        protected abstract string Subdir   { get; }
        protected abstract string FileName { get; }

        // Optional override for tests — points at a temp directory instead
        // of Application.streamingAssetsPath.
        private readonly string _streamingRootOverride;

        protected WorldStreamingFileRepositoryBase(string streamingRootOverride = null)
        {
            _streamingRootOverride = streamingRootOverride;
        }

        public string PathFor(WorldId worldId)
        {
            string dir = WorldDirectory(worldId);
            EnsureDirectory(dir);
            return Path.Combine(dir, FileName);
        }

        public bool ExistsFile(WorldId worldId) => File.Exists(PathFor(worldId));

        public string ReadFile(WorldId worldId)
        {
            string path = PathFor(worldId);
            if (!File.Exists(path)) return null;
            try { return File.ReadAllText(path); }
            catch (Exception ex)
            {
                Debug.LogError($"[{GetType().Name}] Read '{path}' failed: {ex.Message}");
                return null;
            }
        }

        public void WriteFileAtomic(WorldId worldId, string content)
        {
            string path = PathFor(worldId);
            string tmp  = path + ".tmp";
            File.WriteAllText(tmp, content ?? string.Empty);
            if (File.Exists(path))
                File.Replace(tmp, path, path + ".bak");
            else
                File.Move(tmp, path);
        }

        protected string StreamingRoot
            => _streamingRootOverride ?? Application.streamingAssetsPath;

        private string WorldDirectory(WorldId worldId)
        {
            if (worldId.IsBase)
                return Path.Combine(StreamingRoot, Subdir);
            return Path.Combine(StreamingRoot, "Worlds", worldId.Slug, Subdir);
        }

        private static void EnsureDirectory(string dir)
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
    }
}
