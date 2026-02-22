using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core;

namespace Valkur.Gameplay.Rendering
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

            var unlitMaterial = new Material(unlitShader);
            var renderers = _grid.GetComponentsInChildren<TilemapRenderer>();
            int count = 0;
            foreach (var r in renderers)
            {
                if (!r.enabled) continue; // Skip collision layers
                r.material = unlitMaterial;
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

        private void CreateTilemapLayer(Transform parent, TilemapLayerSetup.TilemapLayer layer)
        {
            var go = new GameObject(layer.ToString());
            go.transform.SetParent(parent, false);

            var tilemap = go.AddComponent<Tilemap>();
            tilemap.tileAnchor = new Vector3(0.5f, 0.5f, 0f);

            var renderer = go.AddComponent<TilemapRenderer>();
            renderer.sortOrder = TilemapRenderer.SortOrder.TopLeft;
            renderer.detectChunkCullingBounds = TilemapRenderer.DetectChunkCullingBounds.Auto;

            var layerSetup = go.AddComponent<TilemapLayerSetup>();
            layerSetup.Configure(layer);

            // Collision layer gets a TilemapCollider2D
            if (layer == TilemapLayerSetup.TilemapLayer.Collision)
            {
                var collider = go.AddComponent<TilemapCollider2D>();
                if (tilemapPhysicsMaterial != null)
                    collider.sharedMaterial = tilemapPhysicsMaterial;

                var composite = go.AddComponent<CompositeCollider2D>();
                composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
                composite.generationType = CompositeCollider2D.GenerationType.Synchronous;

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
                var collider = go.AddComponent<TilemapCollider2D>();
                if (tilemapPhysicsMaterial != null)
                    collider.sharedMaterial = tilemapPhysicsMaterial;

                var composite = go.AddComponent<CompositeCollider2D>();
                composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
                composite.generationType = CompositeCollider2D.GenerationType.Synchronous;

                collider.usedByComposite = true;

                var rb = go.GetComponent<Rigidbody2D>();
                if (rb != null)
                    rb.bodyType = RigidbodyType2D.Static;
            }

            layerSetup.ApplyLayerSettings();
        }

    }
}
