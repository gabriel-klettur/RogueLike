using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core;

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
        private Material _unlitFallbackMaterial;

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

            // Deferred: check for Light2D after one frame (GameplaySceneSetup creates it in Start)
            StartCoroutine(ApplyUnlitFallbackIfNeeded());
        }

        private System.Collections.IEnumerator ApplyUnlitFallbackIfNeeded()
        {
            // Wait one frame so all setup has completed
            yield return null;

            // Always apply Unlit material to TilemapRenderers.
            // The Sprite-Lit-Default shader requires a properly configured Global Light2D,
            // and reflection-based Light2D creation is unreliable (lightType may remain Freeform).
            // Unlit material renders sprites at full brightness without any light dependency.
            var unlitShader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (unlitShader == null)
            {
                Debug.LogError("[WorldGridBuilder] Sprite-Unlit-Default shader not found! Tiles may render black.");
                yield break;
            }

            _unlitFallbackMaterial = new Material(unlitShader);
            _unlitFallbackMaterial.hideFlags = HideFlags.HideAndDontSave;
            var renderers = _grid.GetComponentsInChildren<TilemapRenderer>();
            int count = 0;
            foreach (var r in renderers)
            {
                if (!r.enabled) continue; // Skip collision layers
                r.sharedMaterial = _unlitFallbackMaterial;
                count++;
            }
            Debug.Log($"[WorldGridBuilder] Applied Sprite-Unlit-Default to {count} TilemapRenderers.");
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

        private void OnDestroy()
        {
            if (_unlitFallbackMaterial != null)
                Destroy(_unlitFallbackMaterial);
        }

        private void CreateTilemapLayer(Transform parent, TilemapLayerSetup.TilemapLayer layer)
        {
            var go = new GameObject(layer.ToString());
            go.transform.SetParent(parent, false);

            var tilemap = go.AddComponent<Tilemap>();
            tilemap.tileAnchor = new Vector3(0.5f, 0.5f, 0f);

            var renderer = go.AddComponent<TilemapRenderer>();
            renderer.sortOrder = TilemapRenderer.SortOrder.TopLeft;
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

            // WallsBottom also gets collision
            if (layer == TilemapLayerSetup.TilemapLayer.WallsBottom)
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
            }

            layerSetup.ApplyLayerSettings();
        }

    }
}
