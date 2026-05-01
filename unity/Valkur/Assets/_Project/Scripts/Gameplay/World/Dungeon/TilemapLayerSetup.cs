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
            OverheadDetails = 8
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

            switch (layer)
            {
                case TilemapLayer.Ground:
                    renderer.sortingLayerName = SortingConfig.LAYER_GROUND;
                    renderer.sortingOrder = 0;
                    break;
                case TilemapLayer.FloorDecals:
                    renderer.sortingLayerName = SortingConfig.LAYER_FLOOR_DECALS;
                    renderer.sortingOrder = 0;
                    break;
                case TilemapLayer.ObjectsLow:
                    renderer.sortingLayerName = SortingConfig.LAYER_OBJECTS_LOW;
                    renderer.sortingOrder = 0;
                    break;
                case TilemapLayer.WallsBottom:
                    renderer.sortingLayerName = SortingConfig.LAYER_WALLS_BOTTOM;
                    renderer.sortingOrder = 0;
                    break;
                case TilemapLayer.Decorations:
                    renderer.sortingLayerName = SortingConfig.LAYER_DECORATIONS;
                    renderer.sortingOrder = 0;
                    break;
                case TilemapLayer.WallsTop:
                    renderer.sortingLayerName = SortingConfig.LAYER_WALLS_TOP;
                    renderer.sortingOrder = 0;
                    break;
                case TilemapLayer.ObjectsHigh:
                    renderer.sortingLayerName = SortingConfig.LAYER_OBJECTS_HIGH;
                    renderer.sortingOrder = 0;
                    break;
                case TilemapLayer.OverheadDetails:
                    renderer.sortingLayerName = SortingConfig.LAYER_OVERHEAD;
                    renderer.sortingOrder = 0;
                    break;
            }
        }
    }
}
