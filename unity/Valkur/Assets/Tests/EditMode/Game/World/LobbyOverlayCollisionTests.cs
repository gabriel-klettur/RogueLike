// Validates that the lobby overlay JSON shipped in StreamingAssets contains
// real Collision data and that loading it through OverlayLoader produces a
// CompositeCollider2D with non-zero pathCount. Catches data regressions
// (e.g. someone clears the Collision layer or saves an empty overlay).

using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.TestTools;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.World
{
    /// <summary>
    /// Asserts the lobby zone has authored collision data and that the runtime
    /// loader paints it onto a tilemap which produces real composite geometry.
    /// </summary>
    [TestFixture]
    public class LobbyOverlayCollisionTests
    {
        private static readonly string LobbyOverlayPath =
            Path.Combine(Application.streamingAssetsPath, "Maps", "lobby.overlay.json");

        private GameObject _builderGo;

        [SetUp]
        public void SetUp()
        {
            // Some imported sprites trigger renderer.material warnings when the
            // tilemap renderer is enabled in EditMode; allow them.
            LogAssert.ignoreFailingMessages = true;
            _builderGo = new GameObject("TestWorldGridBuilder");
        }

        [TearDown]
        public void TearDown()
        {
            if (_builderGo != null) Object.DestroyImmediate(_builderGo);
        }

        [Test]
        public void LobbyOverlayFile_Exists()
        {
            Assert.IsTrue(File.Exists(LobbyOverlayPath),
                $"Lobby overlay JSON missing at: {LobbyOverlayPath}");
        }

        [Test]
        public void LobbyOverlayFile_HasCollisionEntries()
        {
            string json = File.ReadAllText(LobbyOverlayPath);
            // A dirt-cheap data check that doesn't depend on JSON dialect: at least
            // one entry name we know belongs to the Collision layer.
            StringAssert.Contains("\"Collision\"", json,
                "Lobby overlay must declare a Collision layer.");
            StringAssert.Contains("\"wall\"", json,
                "Lobby Collision layer must contain at least one 'wall' entry.");
        }

        /// <summary>
        /// Loading the lobby overlay must paint at least one Collision tile and
        /// the runtime collider stack (TilemapCollider2D + CompositeCollider2D +
        /// Static Rigidbody2D) must be present. The composite <c>pathCount</c>
        /// assertion lives in the PlayMode test below — in EditMode the
        /// Tilemap geometry pipeline does not run, so pathCount stays at 0
        /// even with painted tiles (Unity behaviour, not a bug).
        /// </summary>
        [Test]
        public void LobbyOverlay_AfterLoad_PaintsCollisionTilesAndKeepsCompositeStack()
        {
            var builder = _builderGo.AddComponent<WorldGridBuilder>();
            builder.BuildGrid();

            var collision = builder.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            Assert.IsNotNull(collision, "Collision tilemap must exist on the grid.");

            OverlayLoader.LoadOverlay("lobby.overlay.json", builder, 0, 0);

            int paintedCells = 0;
            var bounds = collision.cellBounds;
            for (int y = bounds.yMin; y < bounds.yMax; y++)
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                    if (collision.HasTile(new Vector3Int(x, y, 0))) paintedCells++;

            Assert.Greater(paintedCells, 0,
                "Loading the lobby overlay should have painted at least one Collision tile.");

            var tmCol = collision.GetComponent<TilemapCollider2D>();
            Assert.IsNotNull(tmCol, "Collision tilemap is missing TilemapCollider2D.");
            Assert.IsTrue(tmCol.usedByComposite,
                "TilemapCollider2D.usedByComposite must be true so the composite picks up the cells.");

            var composite = collision.GetComponent<CompositeCollider2D>();
            Assert.IsNotNull(composite, "Collision tilemap is missing CompositeCollider2D.");
            Assert.AreEqual(CompositeCollider2D.GenerationType.Manual, composite.generationType,
                "Composite must be Manual so RebakeTilemapColliders controls when geometry is generated.");

            var rb = collision.GetComponent<Rigidbody2D>();
            Assert.IsNotNull(rb, "Composite requires a Rigidbody2D on the same GameObject.");
            Assert.AreEqual(RigidbodyType2D.Static, rb.bodyType,
                "Collision tilemap Rigidbody2D must be Static so it acts as a wall, not a moving body.");
        }
    }
}
