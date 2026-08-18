using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Dynamically updates a SpriteRenderer's sortingOrder based on the entity's Y position.
    /// Entities lower on screen (higher Y in world) render in front of entities higher on screen.
    /// Maps to Python's Z-layer + Y-sort rendering pipeline (entities_renderer.py / z_layer/render.py).
    /// 
    /// Attach to any entity with a SpriteRenderer that needs Y-based depth sorting.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class YSortEntity : MonoBehaviour
    {
        [Tooltip("Base Z-layer order. Higher values render in front. Maps to Python Z_LAYERS.")]
        [SerializeField] private int zLayerBase = SortingConfig.Z_ENTITY;

        [Tooltip("Offset added to the Y-derived order for fine-tuning.")]
        [SerializeField] private int sortingOffset;

        [Tooltip("If true, sorting layer is set to 'Entities' on Awake.")]
        [SerializeField] private bool autoSetSortingLayer = true;

        private SpriteRenderer _sr;
        private float _lastY;
        private const float Y_THRESHOLD = 0.01f;

        public int ZLayerBase
        {
            get => zLayerBase;
            set => zLayerBase = value;
        }

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            if (autoSetSortingLayer)
                _sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            _lastY = transform.position.y;
            UpdateSortingOrder();
        }

        private void LateUpdate()
        {
            // Always skip when the entity hasn't moved meaningfully on Y.
            // The sorting order is derived solely from transform.position.y,
            // so writing it again when nothing changed wastes a
            // SpriteRenderer.sortingOrder set (which propagates a dirty flag
            // through URP's batcher). 7+ stationary YSortEntity instances were
            // paying this cost every LateUpdate before this guard.
            float currentY = transform.position.y;
            if (Mathf.Abs(currentY - _lastY) < Y_THRESHOLD)
                return;
            _lastY = currentY;

            UpdateSortingOrder();
        }

        private void UpdateSortingOrder()
        {
            _sr.sortingOrder = SortingConfig.ComputeSortingOrder(zLayerBase, transform.position.y) + sortingOffset;
        }
    }
}
