using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Diagnostics
{
    /// <summary>
    /// Defensive smoke tests for <see cref="TileEditorDiagnostics"/>.
    ///
    /// The diagnostics helper is a one-shot debug logger invoked the first time the
    /// brush paints in a session. It must NEVER throw — even when given a tile with
    /// a missing sprite, no <c>TilemapRenderer</c>, or in a project with no URP/Light2D
    /// assembly loaded — because that would crash the very first paint and lock up
    /// the whole editor session.
    /// </summary>
    [TestFixture]
    public class TileEditorDiagnosticsTests
    {
        private GameObject _root;
        private GameObject _ownerGo;
        private MonoBehaviour _owner;
        private Tilemap _tilemap;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("DiagRoot");
            _root.AddComponent<Grid>().cellSize = Vector3.one;

            var tmGo = new GameObject("Tilemap");
            tmGo.transform.SetParent(_root.transform, false);
            _tilemap = tmGo.AddComponent<Tilemap>();
            tmGo.AddComponent<TilemapRenderer>();

            _ownerGo = new GameObject("DiagOwner");
            _owner = _ownerGo.AddComponent<DiagnosticsOwner>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_root);
            UnityEngine.Object.DestroyImmediate(_ownerGo);
        }

        [Test]
        public void LogBrushDiagnostics_WithFullySetUpTile_DoesNotThrow()
        {
            var tex = new Texture2D(1, 1); tex.SetPixel(0, 0, Color.white); tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 16f);
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.color = Color.white;
            try
            {
                LogAssert.ignoreFailingMessages = true;
                Assert.DoesNotThrow(() =>
                    TileEditorDiagnostics.LogBrushDiagnostics(_owner, _tilemap, Vector3Int.zero, tile));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                UnityEngine.Object.DestroyImmediate(tile);
            }
        }

        [Test]
        public void LogBrushDiagnostics_WithNullTile_DoesNotThrow()
        {
            // First-paint diagnostic must survive even a null tile (e.g. resolver miss).
            try
            {
                LogAssert.ignoreFailingMessages = true;
                Assert.DoesNotThrow(() =>
                    TileEditorDiagnostics.LogBrushDiagnostics(_owner, _tilemap, Vector3Int.zero, null));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void LogBrushDiagnostics_WithSpritelessTile_DoesNotThrow()
        {
            var tile = ScriptableObject.CreateInstance<Tile>(); // sprite intentionally left null
            try
            {
                LogAssert.ignoreFailingMessages = true;
                Assert.DoesNotThrow(() =>
                    TileEditorDiagnostics.LogBrushDiagnostics(_owner, _tilemap, Vector3Int.zero, tile));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                UnityEngine.Object.DestroyImmediate(tile);
            }
        }

        [Test]
        public void LogBrushDiagnostics_WhenTilemapHasNoRenderer_DoesNotThrow()
        {
            // Brand-new tilemap without a TilemapRenderer component.
            var bareGo = new GameObject("BareTilemap");
            bareGo.transform.SetParent(_root.transform, false);
            var bare = bareGo.AddComponent<Tilemap>();

            var tile = ScriptableObject.CreateInstance<Tile>();
            try
            {
                LogAssert.ignoreFailingMessages = true;
                Assert.DoesNotThrow(() =>
                    TileEditorDiagnostics.LogBrushDiagnostics(_owner, bare, Vector3Int.zero, tile));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                UnityEngine.Object.DestroyImmediate(tile);
                UnityEngine.Object.DestroyImmediate(bareGo);
            }
        }

        [Test]
        public void LogBrushDiagnostics_IsPubliclyExposedAsStaticEntryPoint()
        {
            // Pin the API shape — partial classes / refactors must not turn this private
            // or accidentally rename the entry point used by TileBrush.
            var method = typeof(TileEditorDiagnostics).GetMethod(
                "LogBrushDiagnostics",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method, "LogBrushDiagnostics must remain a public static method.");
        }

        // Concrete MonoBehaviour subclass — diagnostics signature requires a real instance.
        private sealed class DiagnosticsOwner : MonoBehaviour { }
    }
}
