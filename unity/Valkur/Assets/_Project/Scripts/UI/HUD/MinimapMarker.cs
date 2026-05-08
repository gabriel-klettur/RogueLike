using UnityEngine;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// World-space marker rendered on the minimap.
    /// Mirrors Python MinimapController's markers list (portals, vendors, quests).
    /// Attach to any GameObject; its position is sampled each frame by MinimapManager.
    /// </summary>
    public class MinimapMarker : MonoBehaviour
    {
        public enum MarkerShape { Square, Diamond, Plus }

        [Tooltip("World-space offset applied before projecting to the minimap (e.g. for feet-based entities).")]
        public Vector2 offset;

        [Tooltip("Marker fill color.")]
        public Color color = new Color(0.9f, 0.7f, 0.15f, 1f);

        [Tooltip("Marker size in minimap pixels (side length).")]
        [Range(1, 9)] public int pixelSize = 4;

        [Tooltip("Marker shape drawn on the minimap.")]
        public MarkerShape shape = MarkerShape.Diamond;

        [Tooltip("If true, the marker pulses between color and a brighter tint for attention.")]
        public bool pulse = false;

        [Tooltip("Pulse period in seconds.")]
        public float pulsePeriod = 1.0f;

        [Tooltip("Optional short caption rendered next to the marker on the minimap (e.g. vendor role initials \"BS\", \"LJ\"). Empty disables the label.")]
        public string label = string.Empty;

        /// <summary>Effective color at the current time (honors pulse).</summary>
        public Color EffectiveColor
        {
            get
            {
                if (!pulse) return color;
                float t = Mathf.PingPong(Time.unscaledTime / Mathf.Max(0.05f, pulsePeriod), 1f);
                var tint = new Color(
                    Mathf.Clamp01(color.r + 0.25f),
                    Mathf.Clamp01(color.g + 0.25f),
                    Mathf.Clamp01(color.b + 0.25f),
                    color.a);
                return Color.Lerp(color, tint, t);
            }
        }

        public Vector2 WorldPosition
        {
            get
            {
                Vector3 p = transform.position;
                return new Vector2(p.x + offset.x, p.y + offset.y);
            }
        }

        private void OnEnable()  => MinimapManager.RegisterMarker(this);
        private void OnDisable() => MinimapManager.UnregisterMarker(this);

        /// <summary>
        /// Configure marker appearance at runtime. Used by reflection-based wiring from
        /// gameplay-side components (ZonePortal, VendorNPC) so they can register
        /// themselves on the minimap without the Gameplay assembly referencing UI.
        /// </summary>
        public void Configure(Color markerColor, MarkerShape markerShape, int sizePixels, bool pulseEnabled, float periodSeconds)
        {
            color       = markerColor;
            shape       = markerShape;
            pixelSize   = Mathf.Clamp(sizePixels, 1, 9);
            pulse       = pulseEnabled;
            pulsePeriod = Mathf.Max(0.05f, periodSeconds);
        }

        /// <summary>
        /// Configure marker + add a short text caption (e.g. vendor role initials).
        /// Captions longer than 3 chars are truncated so they don't crowd the dial.
        /// </summary>
        public void Configure(Color markerColor, MarkerShape markerShape, int sizePixels, bool pulseEnabled, float periodSeconds, string caption)
        {
            Configure(markerColor, markerShape, sizePixels, pulseEnabled, periodSeconds);
            if (string.IsNullOrEmpty(caption)) { label = string.Empty; return; }
            label = caption.Length > 3 ? caption.Substring(0, 3) : caption;
        }
    }
}
