using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Gameplay.Tile
{
    /// <summary>
    /// Unit tests for <see cref="TerrainMap"/>: get/set/clear, null handling,
    /// Vector2Int/Vector3Int overload parity.
    /// </summary>
    [TestFixture]
    public class TerrainMapTests
    {
        [Test]
        public void NewMap_IsEmpty()
        {
            var m = new TerrainMap();
            Assert.AreEqual(0, m.Count);
            Assert.IsNull(m.GetTerrain(new Vector2Int(0, 0)));
        }

        [Test]
        public void SetTerrain_StoresValue()
        {
            var m = new TerrainMap();
            m.SetTerrain(new Vector2Int(3, 5), "grass");
            Assert.AreEqual("grass", m.GetTerrain(new Vector2Int(3, 5)));
            Assert.AreEqual(1, m.Count);
        }

        [Test]
        public void SetTerrain_Vector3Int_MapsToVector2Int()
        {
            var m = new TerrainMap();
            m.SetTerrain(new Vector3Int(2, 4, 7), "sand");
            Assert.AreEqual("sand", m.GetTerrain(new Vector3Int(2, 4, 0)),
                "z component is dropped — z=7 and z=0 should map to the same cell.");
            Assert.AreEqual("sand", m.GetTerrain(new Vector2Int(2, 4)));
        }

        [Test]
        public void SetTerrain_NullOrEmpty_RemovesEntry()
        {
            var m = new TerrainMap();
            m.SetTerrain(new Vector2Int(1, 1), "grass");
            m.SetTerrain(new Vector2Int(1, 1), null);
            Assert.IsNull(m.GetTerrain(new Vector2Int(1, 1)));
            Assert.AreEqual(0, m.Count);

            m.SetTerrain(new Vector2Int(2, 2), "dirt");
            m.SetTerrain(new Vector2Int(2, 2), "");
            Assert.IsNull(m.GetTerrain(new Vector2Int(2, 2)));
        }

        [Test]
        public void SetTerrain_Overwrites()
        {
            var m = new TerrainMap();
            m.SetTerrain(new Vector2Int(0, 0), "grass");
            m.SetTerrain(new Vector2Int(0, 0), "dirt");
            Assert.AreEqual("dirt", m.GetTerrain(new Vector2Int(0, 0)));
            Assert.AreEqual(1, m.Count);
        }

        [Test]
        public void Clear_EmptiesMap()
        {
            var m = new TerrainMap();
            m.SetTerrain(new Vector2Int(0, 0), "a");
            m.SetTerrain(new Vector2Int(1, 0), "b");
            m.SetTerrain(new Vector2Int(2, 0), "c");
            m.Clear();
            Assert.AreEqual(0, m.Count);
        }

        [Test]
        public void Cells_ExposesReadOnlyView()
        {
            var m = new TerrainMap();
            m.SetTerrain(new Vector2Int(0, 0), "grass");
            m.SetTerrain(new Vector2Int(1, 0), "dirt");
            var view = m.Cells;
            Assert.AreEqual(2, view.Count);
            Assert.AreEqual("grass", view[new Vector2Int(0, 0)]);
            Assert.AreEqual("dirt",  view[new Vector2Int(1, 0)]);
        }
    }
}
