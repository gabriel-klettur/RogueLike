using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core;
using Valkur.Core.Rendering;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Builds the tilemap grid hierarchy at runtime if not already present in the scene.
    /// Creates a Grid with child Tilemaps for each layer defined in TilemapLayerSetup.TilemapLayer.
    /// Maps to Python's multi-layer map model (Ground, FloorDecals, Collision, etc.).
    /// 
    /// Attach to a persistent GameObject in the gameplay scene, or call BuildGrid() from setup code.
    /// </summary>
    public class WorldGridBuilder : MonoBehaviour
    {
        [Header("Grid Settings")]
        [SerializeField] private Vector3 cellSize = new Vector3(1f, 1f, 0f);

        [Header("Collision")]
        [SerializeField] private PhysicsMaterial2D tilemapPhysicsMaterial;

        private Grid _grid;

        public Grid Grid => _grid;

        private void Awake()
        {
            if (_grid == null)
                BuildGrid();
        }

        /// <summary>
        /// Create the Grid and all tilemap layers. Safe to call multiple times (idempotent).
        /// </summary>
        public void BuildGrid()
        {
            if (_grid != null) return;

            var gridGo = new GameObject("WorldGrid");
            gridGo.transform.SetParent(transform, false);
            _grid = gridGo.AddComponent<Grid>();
            _grid.cellSize = cellSize;
            _grid.cellLayout = GridLayout.CellLayout.Rectangle;

            var layers = System.Enum.GetValues(typeof(TilemapLayerSetup.TilemapLayer));
            foreach (TilemapLayerSetup.TilemapLayer layer in layers)
            {
                CreateTilemapLayer(gridGo.transform, layer);
            }

            Debug.Log($"[WorldGridBuilder] Grid built with {layers.Length} tilemap layers.");

            // Deferred: the Global Light2D is created by GameplaySceneSetup in Start, so the
            // lit/unlit decision cannot be made until the frame after the grid is built.
            StartCoroutine(ApplyTilemapMaterial());
        }

        /// <summary>
        /// Gives every visible tilemap the lit material when the scene actually has a Global
        /// Light2D to feed it, and the unlit one when it does not.
        ///
        /// This method used to be called ApplyUnlitFallbackIfNeeded and stamped
        /// Sprite-Unlit-Default UNCONDITIONALLY — the "IfNeeded" was vestigial and there was
        /// no Light2D probe in the body at all. Its own comment blamed "reflection-based
        /// Light2D creation is unreliable (lightType may remain Freeform)", which was a
        /// precise description of the enum bug in GameplaySceneSetup rather than a reason to
        /// give up on lighting. The cost was the whole day/night cycle: the world rendered at
        /// noon brightness at 03:00 because nothing it drew could receive light.
        /// </summary>
        private System.Collections.IEnumerator ApplyTilemapMaterial()
        {
            // Wait one frame so GameplaySceneSetup.Start has created/repaired the global light.
            yield return null;

            bool lit = WorldSpriteMaterials.AmbientLightingAvailable;
            var renderers = _grid.GetComponentsInChildren<TilemapRenderer>();
            int count = 0;
            foreach (var r in renderers)
            {
                if (!r.enabled) continue; // Skip collision layers

                var material = WorldSpriteMaterials.WorldWithSnow(SnowRoleFor(r));
                if (material == null) yield break;   // Resolve() already logged the missing shader.
                r.sharedMaterial = material;
                count++;
            }

            if (lit)
                Debug.Log($"[WorldGridBuilder] {count} TilemapRenderers are lit — the day/night ambient reaches them.");
            else
                Debug.LogWarning($"[WorldGridBuilder] No Global Light2D found; {count} TilemapRenderers fell back to " +
                                  "Sprite-Unlit-Default. The world will not react to the day/night cycle.");
        }

        /// <summary>
        /// Which snow role a tilemap layer collects in.
        ///
        /// The split is the projection, not a preference: in top-down the GROUND faces the sky
        /// across its whole area, so it takes an even blanket, while everything drawn as a
        /// standing thing — walls, decorations, overhead detail — only collects on the edges
        /// with nothing above them, which the shader finds from the sprite's own alpha.
        /// Getting it backwards is very visible: a blanket role on a wall paints its whole
        /// face white, which reads as a missing texture.
        ///
        /// Resolved from the layer component rather than from the renderer's sorting layer,
        /// because <c>TilemapLayerSetup</c> is what actually owns the identity of each map.
        /// A tilemap with no setup component falls back to Cap, the conservative answer: a
        /// missed cap is a surface that stays bare, a wrong blanket is a white rectangle.
        /// </summary>
        private static WorldSpriteMaterials.SnowRole SnowRoleFor(TilemapRenderer renderer)
        {
            var setup = renderer.GetComponent<TilemapLayerSetup>();
            if (setup == null) return WorldSpriteMaterials.SnowRole.Cap;

            switch (setup.Layer)
            {
                case TilemapLayerSetup.TilemapLayer.Ground:
                case TilemapLayerSetup.TilemapLayer.FloorDecals:
                    return WorldSpriteMaterials.SnowRole.Blanket;
                default:
                    return WorldSpriteMaterials.SnowRole.Cap;
            }
        }

        /// <summary>
        /// Get the Tilemap for a specific layer. Returns null if grid not built or layer not found.
        /// </summary>
        public Tilemap GetTilemap(TilemapLayerSetup.TilemapLayer layer)
        {
            if (_grid == null) return null;

            var layerTransform = _grid.transform.Find(layer.ToString());
            if (layerTransform == null) return null;

            return layerTransform.GetComponent<Tilemap>();
        }

        /// <summary>
        /// Clear all tiles from all tilemap layers without destroying the grid hierarchy.
        /// Used by ZonePortal for same-scene overlay swaps.
        /// </summary>
        public void ClearWorld()
        {
            if (_grid == null) return;

            var tilemaps = _grid.GetComponentsInChildren<Tilemap>();
            foreach (var tm in tilemaps)
                tm.ClearAllTiles();

            Debug.Log("[WorldGridBuilder] World cleared.");
        }

        private void CreateTilemapLayer(Transform parent, TilemapLayerSetup.TilemapLayer layer)
        {
            var go = new GameObject(layer.ToString());
            go.transform.SetParent(parent, false);

            var tilemap = go.AddComponent<Tilemap>();
            tilemap.tileAnchor = new Vector3(0.5f, 0.5f, 0f);

            var renderer = go.AddComponent<TilemapRenderer>();
            renderer.sortOrder = TilemapRenderer.SortOrder.TopLeft;
            // IMPORTANT: keep Unity's default Chunk mode.
            // A previous iteration set Mode.Individual to "fix" chunk-boundary
            // seam lines, but MCP profiling proved (a) the seam lines were a
            // Game-View-only composite artifact that doesn't appear in builds,
            // and (b) Individual mode submits one quad per visible tile —
            // ~60k tiles → CPU collapse to <30 FPS. Chunk mode batches the
            // tilemap into 16×16-cell meshes (typically 1 draw call per
            // chunk per layer), which is the correct trade-off for a moderate-
            // sized open-world map.
            // Manual chunk culling bounds keep Unity's per-chunk frustum culling working.
            // With Auto, Unity grows each chunk's culling bounds to fit the largest sprite
            // in the tilemap (tall walls, wide decorations, etc.), which in practice
            // disables culling entirely → every chunk in every layer renders every frame,
            // collapsing FPS when the camera reveals far-away zones (e.g. while panning
            // in the Tile Editor with the Cinemachine follow detached).
            // Manual + (1, 2, 0) gives us 1 cell of horizontal padding and 2 cells of
            // vertical padding (enough for tall wall sprites) while still culling chunks
            // outside the camera viewport.
            renderer.detectChunkCullingBounds = TilemapRenderer.DetectChunkCullingBounds.Manual;
            renderer.chunkCullingBounds = new Vector3(1f, 2f, 0f);

            var layerSetup = go.AddComponent<TilemapLayerSetup>();
            layerSetup.Configure(layer);

            // Collision layer gets a TilemapCollider2D
            if (layer == TilemapLayerSetup.TilemapLayer.Collision)
            {
                go.layer = LayerMask.NameToLayer("World"); // Projectile layer matrix: 10 ↔ 11

                var collider = go.AddComponent<TilemapCollider2D>();
                if (tilemapPhysicsMaterial != null)
                    collider.sharedMaterial = tilemapPhysicsMaterial;

                var composite = go.AddComponent<CompositeCollider2D>();
                composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
                composite.generationType = CompositeCollider2D.GenerationType.Manual;

                collider.usedByComposite = true;

                var rb = go.GetComponent<Rigidbody2D>();
                if (rb != null)
                    rb.bodyType = RigidbodyType2D.Static;

                renderer.enabled = false;

                layerSetup.Configure(layer, true);
            }

            // WallsBottom intentionally has NO independent collider. The M2
            // per-visual-layer system makes the Collision tilemap (+ its tag
            // map) the single authoritative source for what blocks an entity
            // at a given layer. An always-on collider here would bypass that
            // filter — a player on a different visual layer would still be
            // blocked by a wall sprite painted on WallsBottom — which is the
            // exact contradiction the M2 design was built to eliminate.
            // Authors who want a WallsBottom wall to block must also paint a
            // Collision cell at the same coordinate (with the desired tag).

            layerSetup.ApplyLayerSettings();
        }

    }
}
