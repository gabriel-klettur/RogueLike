using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Valkur.UIKit
{
    /// <summary>
    /// Sibling component for <see cref="GridLayoutGroup"/> that recomputes
    /// <c>cellSize</c> and <c>constraintCount</c> from the host
    /// <see cref="RectTransform"/>'s available width whenever the parent panel
    /// is resized. Lets a fixed-cellSize grid behave like a responsive grid
    /// that reflows on resize — drop the panel narrower and columns shrink;
    /// drag the panel wider and more columns appear.
    ///
    /// Algorithm
    /// ─────────
    ///   available = width - padding.left - padding.right
    ///   cols      = max(1, floor((available + spacing) / (minCellSize + spacing)))
    ///   cellW     = (available - (cols - 1) * spacing) / cols
    ///   cellW     = clamp(cellW, 1, maxCellSize)
    ///
    /// Cells are kept square (height = width) which matches every editor's
    /// slot button (icon + bottom label).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GridLayoutGroup))]
    [RequireComponent(typeof(RectTransform))]
    public class GridAutoSize : UIBehaviour
    {
        [SerializeField, Tooltip("Smallest cell width allowed before column count drops.")]
        private float minCellSize = 56f;

        [SerializeField, Tooltip("Largest cell width allowed before column count grows.")]
        private float maxCellSize = 96f;

        [SerializeField, Tooltip("Spacing between cells (applied on both axes).")]
        private float spacing = 4f;

        [SerializeField, Tooltip("Optional explicit cell height. " +
            "When ≤ 0 the cell stays square (height = computed width, the historical default). " +
            "Set > 0 for rectangular cells — e.g. the Tile Editor's category buttons use a " +
            "responsive width but a fixed 22 px row height.")]
        private float cellHeightOverride = 0f;

        // RectOffset is a Unity native wrapper that calls set_left/right/top/bottom
        // on construction. Those setters are forbidden inside MonoBehaviour
        // field initializers — initialise lazily inside OnEnable instead.
        [SerializeField, Tooltip("Left padding inside the grid container.")]
        private int paddingLeft = 4;
        [SerializeField, Tooltip("Right padding inside the grid container.")]
        private int paddingRight = 4;
        [SerializeField, Tooltip("Top padding inside the grid container.")]
        private int paddingTop = 4;
        [SerializeField, Tooltip("Bottom padding inside the grid container.")]
        private int paddingBottom = 4;

        public float MinCellSize { get => minCellSize; set { minCellSize = value; ForceRecompute(); } }
        public float MaxCellSize { get => maxCellSize; set { maxCellSize = value; ForceRecompute(); } }
        public float Spacing     { get => spacing;     set { spacing     = value; ForceRecompute(); } }
        public float CellHeightOverride { get => cellHeightOverride; set { cellHeightOverride = value; ForceRecompute(); } }

        public RectOffset Padding
        {
            get => _padding ?? new RectOffset(paddingLeft, paddingRight, paddingTop, paddingBottom);
            set
            {
                if (value == null) return;
                paddingLeft   = value.left;   paddingRight  = value.right;
                paddingTop    = value.top;    paddingBottom = value.bottom;
                _padding = new RectOffset(paddingLeft, paddingRight, paddingTop, paddingBottom);
                ForceRecompute();
            }
        }

        private GridLayoutGroup _grid;
        private RectTransform   _rt;
        private RectOffset      _padding;
        private float           _lastWidth = -1f;

        protected override void OnEnable()
        {
            base.OnEnable();
            // Lazy-init RectOffset here — constructing it in a field
            // initializer would invoke its native setters before Unity is
            // ready, throwing 'set_left is not allowed' at scene load.
            if (_padding == null)
                _padding = new RectOffset(paddingLeft, paddingRight, paddingTop, paddingBottom);
            CacheRefs();
            ForceRecompute();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            Recompute();
        }

        /// <summary>Public hook so external callers can request a recompute
        /// after they reparent/reattach the component.</summary>
        public void ForceRecompute()
        {
            _lastWidth = -1f;
            Recompute();
        }

        private void CacheRefs()
        {
            if (_grid == null) _grid = GetComponent<GridLayoutGroup>();
            if (_rt   == null) _rt   = GetComponent<RectTransform>();
        }

        private void Recompute()
        {
            CacheRefs();
            if (_grid == null || _rt == null) return;

            float width = _rt.rect.width;
            // Skip until the layout system has assigned a real width — running
            // before the parent VLG has resolved gives us width=0 and produces
            // bogus column counts that flicker once the real width arrives.
            if (width <= 1f) return;
            if (Mathf.Approximately(width, _lastWidth)) return;
            _lastWidth = width;

            float available  = Mathf.Max(0f, width - paddingLeft - paddingRight);
            float cellPlusGap = Mathf.Max(1f, minCellSize + spacing);
            int   cols        = Mathf.Max(1, Mathf.FloorToInt((available + spacing) / cellPlusGap));
            float cellW       = cols > 0 ? (available - (cols - 1) * spacing) / cols : minCellSize;
            cellW             = Mathf.Clamp(cellW, 1f, maxCellSize);

            if (_padding == null)
                _padding = new RectOffset(paddingLeft, paddingRight, paddingTop, paddingBottom);

            float cellH = cellHeightOverride > 0f ? cellHeightOverride : cellW;
            _grid.cellSize        = new Vector2(cellW, cellH);
            _grid.spacing         = new Vector2(spacing, spacing);
            _grid.padding         = _padding;
            _grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = cols;
        }
    }
}
