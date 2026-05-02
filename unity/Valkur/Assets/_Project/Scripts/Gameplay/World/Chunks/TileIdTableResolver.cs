using System.Collections.Generic;
using UnityEngine.Tilemaps;
using Valkur.Data.Chunks;

namespace Valkur.Gameplay.World.Chunks
{
    /// <summary>
    /// Default <see cref="IChunkTileResolver"/>. Goes through the per-world
    /// <see cref="ITileIdTable"/> for the id↔name lookup and through a
    /// caller-supplied <c>nameToAsset</c> delegate for the name↔asset
    /// lookup.
    ///
    /// The delegate is the seam where production code plugs in the
    /// existing <c>TileRegistry</c> (Resources-backed) and tests plug in
    /// a fixture-controlled dictionary. No hard dependency on
    /// <c>Resources</c> here so EditMode tests can run without ever
    /// touching the asset database.
    ///
    /// Hits per id are cached so a 50x50 chunk does not re-hash the same
    /// name 2,500 times during a single paint pass.
    /// </summary>
    public sealed class TileIdTableResolver : IChunkTileResolver
    {
        private readonly ITileIdTable _idTable;
        private readonly System.Func<string, TileBase> _nameToAsset;
        private readonly Dictionary<ushort, TileBase> _cache = new Dictionary<ushort, TileBase>();

        public TileIdTableResolver(ITileIdTable idTable, System.Func<string, TileBase> nameToAsset)
        {
            _idTable     = idTable     ?? throw new System.ArgumentNullException(nameof(idTable));
            _nameToAsset = nameToAsset ?? throw new System.ArgumentNullException(nameof(nameToAsset));
        }

        public TileBase Resolve(ushort tileId)
        {
            if (tileId == 0) return null;
            if (_cache.TryGetValue(tileId, out var cached)) return cached;

            string name = _idTable.GetName(tileId);
            TileBase asset = string.IsNullOrEmpty(name) ? null : _nameToAsset(name);
            _cache[tileId] = asset;
            return asset;
        }
    }
}
