using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Production <see cref="IChunkDeltaRepository"/>. One JSON file per
    /// edited chunk under <c>persistentDataPath/Chunks/</c>:
    ///   - WorldId.Base : Chunks/&lt;cx&gt;_&lt;cy&gt;.delta.json
    ///   - other worlds : Chunks/&lt;slug&gt;/&lt;cx&gt;_&lt;cy&gt;.delta.json
    ///
    /// JsonUtility is used because <see cref="ChunkDelta"/> only contains
    /// types it serialises cleanly (struct + List&lt;TileEdit&gt;). Phase 4
    /// switches to MessagePack-CSharp for the network path; the repository
    /// surface stays unchanged so consumers do not notice the swap.
    ///
    /// Writes are atomic (tmp + replace) and skip empty deltas — a virgin
    /// chunk costs zero bytes on disk, which keeps save size O(chunks
    /// edited) instead of O(chunks visited).
    /// </summary>
    public sealed class JsonFileChunkDeltaRepository : IChunkDeltaRepository
    {
        private const string CHUNKS_DIR = "Chunks";
        private const string FILE_EXT   = ".delta.json";

        private readonly string _rootOverride;

        public JsonFileChunkDeltaRepository() : this(null) { }

        public JsonFileChunkDeltaRepository(string rootOverride)
        {
            _rootOverride = rootOverride;
        }

        public string PathFor(WorldId worldId, ChunkCoord coord)
        {
            string dir = WorldDirectory(worldId);
            EnsureDirectory(dir);
            return Path.Combine(dir, $"{coord.Cx}_{coord.Cy}{FILE_EXT}");
        }

        public bool Exists(WorldId worldId, ChunkCoord coord) => File.Exists(PathFor(worldId, coord));

        public ChunkDelta Read(WorldId worldId, ChunkCoord coord)
        {
            string path = PathFor(worldId, coord);
            if (!File.Exists(path)) return null;
            try
            {
                string json = File.ReadAllText(path);
                var loaded = JsonUtility.FromJson<ChunkDelta>(json);
                // The on-disk Coord field could differ from the requested
                // coordinate if a save was hand-edited or imported from
                // another world; force consistency so caller assumptions
                // about delta.Coord hold.
                if (loaded != null) loaded.Coord = coord;
                return loaded;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChunkDeltaRepository] Read '{path}' failed: {ex.Message}");
                return null;
            }
        }

        public void Write(WorldId worldId, ChunkCoord coord, ChunkDelta delta)
        {
            // Empty deltas are not persisted — keeps save size proportional
            // to actual edits. If a file already exists for this chunk and
            // the delta is now empty, delete it so the on-disk view stays
            // consistent with "no file -> no edits".
            if (delta == null || delta.IsEmpty)
            {
                Delete(worldId, coord);
                return;
            }

            string path = PathFor(worldId, coord);
            string tmp  = path + ".tmp";
            string json = JsonUtility.ToJson(delta);
            File.WriteAllText(tmp, json);
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
            else                    File.Move(tmp, path);
        }

        public bool Delete(WorldId worldId, ChunkCoord coord)
        {
            string path = PathFor(worldId, coord);
            if (!File.Exists(path)) return false;
            try { File.Delete(path); return true; }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ChunkDeltaRepository] Delete '{path}' failed: {ex.Message}");
                return false;
            }
        }

        public IEnumerable<ChunkCoord> ListEdited(WorldId worldId)
        {
            string dir = WorldDirectory(worldId);
            if (!Directory.Exists(dir)) yield break;
            foreach (var path in Directory.GetFiles(dir, "*" + FILE_EXT))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                // Trim the ".delta" suffix that Path.GetFileNameWithoutExtension
                // leaves behind because the FILE_EXT has two dots.
                if (name.EndsWith(".delta", StringComparison.Ordinal))
                    name = name.Substring(0, name.Length - 6);
                int sep = name.IndexOf('_');
                if (sep <= 0) continue;
                if (!int.TryParse(name.Substring(0, sep), out int cx)) continue;
                if (!int.TryParse(name.Substring(sep + 1), out int cy)) continue;
                yield return new ChunkCoord(worldId, cx, cy);
            }
        }

        // ── Path helpers ─────────────────────────────────────────────────────────

        private string PersistenceRoot
            => _rootOverride ?? Application.persistentDataPath;

        private string WorldDirectory(WorldId worldId)
            => worldId.IsBase
                ? Path.Combine(PersistenceRoot, CHUNKS_DIR)
                : Path.Combine(PersistenceRoot, CHUNKS_DIR, worldId.Slug);

        private static void EnsureDirectory(string dir)
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
    }
}
