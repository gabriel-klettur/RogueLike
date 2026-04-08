using UnityEngine;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Marks an entity to appear as a dot on the MinimapManager.
    /// Add this component to player and monster GameObjects at runtime or in prefab.
    /// Mirrors Python minimap entity drawing (player=green, monster=red).
    /// </summary>
    public class MinimapDot : MonoBehaviour
    {
        [SerializeField] private MinimapDotType dotType = MinimapDotType.NPC;
        [SerializeField] private Color dotColor = Color.white;
        [Tooltip("If true, dotColor is assigned automatically from MinimapManager defaults on Start.")]
        [SerializeField] private bool useDefaultColor = true;

        public MinimapDotType DotType => dotType;
        public Color DotColor => dotColor;

        private void OnEnable()  => MinimapManager.Register(this);
        private void OnDisable() => MinimapManager.Unregister(this);

        private void Start()
        {
            if (!useDefaultColor) return;
            // Try to get default color from MinimapManager instance
            var mgr = Object.FindObjectOfType<MinimapManager>();
            if (mgr != null)
                dotColor = mgr.GetDefaultColor(dotType);
        }

        /// <summary>
        /// Configure dot appearance at runtime (called by EntitySetup).
        /// </summary>
        public void Configure(MinimapDotType type, Color color)
        {
            dotType = type;
            dotColor = color;
            useDefaultColor = false;
        }
    }
}
