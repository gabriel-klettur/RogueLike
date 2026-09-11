using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Defines the tilemap layer type for a Tilemap GameObject.
    /// Maps to Python's map/model/layer.py Layer enum.
    /// 
    /// Each tilemap layer corresponds to a visual depth in the world:
    /// Ground tiles render behind entities, overhead tiles render in front.
    /// 
    /// Attach to each Tilemap child under a Grid object.
    /// </summary>
    public class TilemapLayerSetup : MonoBehaviour
    {
        public enum TilemapLayer
        {
            Ground = 0,
            FloorDecals = 1,
            Collision = 2,
            ObjectsLow = 3,
            WallsBottom = 4,
            Decorations = 5,
            WallsTop = 6,
            ObjectsHigh = 7,
            OverheadDetails = 8,

            // 9..15 — the tiers the ladder gained when it was grown to Unity's ceiling of 16
            // visual layers. Indexed rather than named: see SortingConfig.LAYER_TIER_9.
            Tier9 = 9,
            Tier10 = 10,
            Tier11 = 11,
            Tier12 = 12,
            Tier13 = 13,
            Tier14 = 14,
            Tier15 = 15
        }

        [SerializeField] private TilemapLayer layer = TilemapLayer.Ground;

        [Tooltip("If true, this tilemap is used for collision only and won't render.")]
        [SerializeField] private bool collisionOnly;

        public TilemapLayer Layer => layer;
        public bool IsCollisionOnly => collisionOnly;

        /// <summary>
        /// Configure layer and collision flag at runtime (replaces reflection-based field injection).
        /// </summary>
        public void Configure(TilemapLayer layerType, bool isCollisionOnly = false)
        {
            layer = layerType;
            collisionOnly = isCollisionOnly;
        }

        private void Awake()
        {
            ApplyLayerSettings();
        }

        /// <summary>
        /// Configure the TilemapRenderer sorting layer and order based on the assigned layer type.
        /// </summary>
        public void ApplyLayerSettings()
        {
            var renderer = GetComponent<TilemapRenderer>();
            if (renderer == null) return;

            if (collisionOnly || layer == TilemapLayer.Collision)
            {
                renderer.enabled = false;
                return;
            }

            // One resolver, not a switch per reader — see SortingConfig.TileSortingLayer.
            string sortingLayer = SortingConfig.TileSortingLayer((int)layer);
            if (string.IsNullOrEmpty(sortingLayer))
            {
                // A layer that draws nothing. Collision already returned above; anything else
                // here is a value added to the enum without a slot, which must not silently
                // render on Default (behind the entire world) — WorldLayerCeilingTests fails it.
                renderer.enabled = false;
                return;
            }

            renderer.sortingLayerName = sortingLayer;
            renderer.sortingOrder = 0;
        }
    }
}
