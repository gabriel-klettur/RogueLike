using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data.Chunks;
using Valkur.Gameplay.World.Chunks;

namespace Valkur.Tests.EditMode.Game.World.Chunks
{
    /// <summary>
    /// Pins the id -> name -> asset translation contract that every
    /// chunk renderer in the project relies on. Three invariants:
    ///   - Empty id (0) always resolves to null.
    ///   - Cache short-circuits repeat lookups (no double-hash for the
    ///     same id within a paint pass).
    ///   - Unknown names route to null instead of throwing.
    /// </summary>
    [TestFixture]
    public class ChunkTileResolverTests
    {
        private DictionaryTileIdTable _idTable;
        private Tile _grass, _dirt;

        [SetUp]
        public void SetUp()
        {
            _idTable = new DictionaryTileIdTable();
            _idTable.Register("grass");
            _idTable.Register("dirt");
            _grass = ScriptableObject.CreateInstance<Tile>(); _grass.name = "grass";
            _dirt  = ScriptableObject.CreateInstance<Tile>(); _dirt.name  = "dirt";
        }

        [TearDown]
        public void TearDown()
        {
            if (_grass != null) Object.DestroyImmediate(_grass);
            if (_dirt  != null) Object.DestroyImmediate(_dirt);
        }

        private TileBase NameLookup(string n)
        {
            if (n == "grass") return _grass;
            if (n == "dirt")  return _dirt;
            return null;
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void EmptyId_ResolvesToNull()
        {
            var r = new TileIdTableResolver(_idTable, NameLookup);
            Assert.IsNull(r.Resolve(0),
                "Tile id 0 is reserved for 'empty' — resolver must never " +
                "translate it into a real asset.");
        }

        [Test]
        public void KnownId_ResolvesToRegisteredAsset()
        {
            var r = new TileIdTableResolver(_idTable, NameLookup);
            ushort grassId = _idTable.GetId("grass");
            Assert.AreSame(_grass, r.Resolve(grassId));
        }

        [Test]
        public void UnknownNameInTable_ResolvesToNull_DoesNotThrow()
        {
            // Register a tile whose asset doesn't exist (delegate returns null).
            _idTable.Register("ghost");
            var r = new TileIdTableResolver(_idTable, NameLookup);
            ushort ghostId = _idTable.GetId("ghost");
            Assert.IsNull(r.Resolve(ghostId),
                "Unknown name -> null asset is the documented quiet fallback. " +
                "Throwing here would crash whole-chunk paints over a single " +
                "missing sprite.");
        }

        [Test]
        public void RepeatedLookup_HitsCache_DoesNotInvokeDelegateTwice()
        {
            int calls = 0;
            System.Func<string, TileBase> counting = n => { calls++; return NameLookup(n); };
            var r = new TileIdTableResolver(_idTable, counting);
            ushort grassId = _idTable.GetId("grass");

            r.Resolve(grassId);
            r.Resolve(grassId);
            r.Resolve(grassId);

            Assert.AreEqual(1, calls,
                "Cache must short-circuit repeat lookups so a 50x50 paint " +
                "of one tile costs one hash, not 2,500.");
        }
    }
}
