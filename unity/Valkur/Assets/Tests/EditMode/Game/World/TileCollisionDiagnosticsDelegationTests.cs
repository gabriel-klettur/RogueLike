using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Regression net for the "382 tiles but pathCount=0" false positive.
    ///
    /// The visual <c>Collision</c> tilemap keeps its painted cells as the authoring
    /// source of truth, but <see cref="Valkur.Gameplay.World.Layering.WorldCollisionBaker"/>
    /// DISABLES its <see cref="TilemapCollider2D"/> and redistributes every cell to the
    /// ten <c>CollisionPhysics_*</c> sub-tilemaps that actually own physics. Its own
    /// composite is therefore legitimately empty. The diagnostic used to call that
    /// "not baked. Player will pass through these tiles." on every single boot, which
    /// is both wrong and the loudest kind of wrong — it describes a wall-less world.
    ///
    /// Contract under test:
    ///   • tiles &gt; 0, paths == 0, source collider DISABLED  → silent (delegated).
    ///   • tiles &gt; 0, paths == 0, source collider ENABLED   → still warns (real fault).
    ///
    /// The second case is the half that must not be lost: gating the warning too
    /// broadly would hide a genuinely unbaked layer.
    /// </summary>
    [TestFixture]
    public class TileCollisionDiagnosticsDelegationTests
    {
        private const string UnbakedFragment = "CompositeCollider2D was not baked";

        private GameObject _gridGo;
        private Tile _wallTile;

        [SetUp]
        public void SetUp()
        {
            _gridGo = new GameObject("Grid", typeof(Grid));

            _wallTile = ScriptableObject.CreateInstance<Tile>();
            _wallTile.name = "test_wall";
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _wallTile.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_gridGo != null) Object.DestroyImmediate(_gridGo);
            if (_wallTile != null) Object.DestroyImmediate(_wallTile);
        }

        /// <summary>
        /// Builds a painted tilemap carrying the Tilemap/TilemapCollider2D/CompositeCollider2D
        /// trio, with the source collider left enabled or disabled. GenerateGeometry is
        /// deliberately never called, so pathCount stays 0 — the state the diagnostic reads.
        /// </summary>
        private GameObject MakePaintedLayer(string name, bool sourceColliderEnabled)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_gridGo.transform, false);

            var tilemap = go.AddComponent<Tilemap>();
            tilemap.SetTile(new Vector3Int(0, 0, 0), _wallTile);
            tilemap.SetTile(new Vector3Int(1, 0, 0), _wallTile);

            var tilemapCollider = go.AddComponent<TilemapCollider2D>();
            tilemapCollider.usedByComposite = true;

            var composite = go.AddComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;

            var body = go.GetComponent<Rigidbody2D>();
            if (body != null) body.bodyType = RigidbodyType2D.Static;

            // Set last: adding the composite can re-enable/refresh the source collider.
            tilemapCollider.enabled = sourceColliderEnabled;

            return go;
        }

        [Test]
        public void DisabledSourceCollider_IsDelegatedNotUnbaked()
        {
            MakePaintedLayer("Collision", sourceColliderEnabled: false);

            // The ungated summary line is expected — it is the one log the diagnostic
            // always emits. Declaring it lets NoUnexpectedReceived assert that the
            // per-layer WARNING is what disappeared, not merely that logging is quiet.
            LogAssert.Expect(LogType.Log,
                new System.Text.RegularExpressions.Regex("composite collider"));

            TileCollisionDiagnostics.Report();

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void DisabledSourceCollider_CountsAsDelegatedInTheSummary()
        {
            MakePaintedLayer("Collision", sourceColliderEnabled: false);

            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex("1 delegated"));

            TileCollisionDiagnostics.Report();
        }

        [Test]
        public void EnabledSourceCollider_WithNoPaths_StillWarns()
        {
            // The half of the contract that must survive: a layer whose collider is
            // live but whose composite was never generated IS broken, and silencing
            // it would hide the very bug the diagnostic exists to surface.
            MakePaintedLayer("SomeUnbakedLayer", sourceColliderEnabled: true);

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex(UnbakedFragment));

            TileCollisionDiagnostics.Report();
        }
    }
}
