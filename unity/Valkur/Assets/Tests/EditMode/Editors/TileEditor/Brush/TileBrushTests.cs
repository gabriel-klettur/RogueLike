using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode
{
    public class TileBrushTests
    {
        private Tilemap CreateTilemap(out GameObject root)
        {
            root = new GameObject("TilemapRoot");
            var grid = root.AddComponent<Grid>();
            grid.cellSize = Vector3.one;

            var tileGo = new GameObject("Tilemap");
            tileGo.transform.SetParent(root.transform, false);
            return tileGo.AddComponent<Tilemap>();
        }

        private static Tile CreateTile(Color color)
        {
            var tex = new Texture2D(2, 2);
            tex.SetPixels(new[] { color, color, color, color });
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 2f);
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            return tile;
        }

        [Test]
        public void Paint_WithEditConstraint_BlocksDisallowedCells()
        {
            var tilemap = CreateTilemap(out var root);
            var tile = CreateTile(Color.green);

            bool AllowOnlyOrigin(Vector3Int pos) => pos.x == 0 && pos.y == 0;
            var edits = TileBrush.Paint(tilemap, Vector3Int.zero, tile, brushSize: 3, canEditCell: AllowOnlyOrigin);

            Assert.AreEqual(1, edits.Count);
            Assert.AreEqual(tile, tilemap.GetTile(Vector3Int.zero));
            Assert.IsNull(tilemap.GetTile(new Vector3Int(1, 0, 0)));

            Object.DestroyImmediate(root);
        }

        [Test]
        public void FloodFill_WithStartCellBlocked_ReturnsNoEdits()
        {
            var tilemap = CreateTilemap(out var root);
            var oldTile = CreateTile(Color.gray);
            var newTile = CreateTile(Color.red);
            tilemap.SetTile(Vector3Int.zero, oldTile);

            bool BlockAll(Vector3Int _) => false;
            var edits = TileBrush.FloodFill(tilemap, Vector3Int.zero, newTile, canEditCell: BlockAll);

            Assert.AreEqual(0, edits.Count);
            Assert.AreEqual(oldTile, tilemap.GetTile(Vector3Int.zero));

            Object.DestroyImmediate(root);
        }
    }
}
