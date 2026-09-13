using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Every decision of the sky's CONSEQUENCES on the ground — the cloud shadows, the sun
    /// shadows and the contact blob under every creature — in one asset the Inspector reaches.
    ///
    /// At the root of <c>Resources/</c> beside <c>DayNightProfile.asset</c>, for the reason every
    /// tuning asset in this project sits there: its readers (<c>CloudShadowLayer</c>,
    /// <c>SunShadowCaster</c>) are <c>AddComponent</c>-ed by the bootstrap and by
    /// <c>EntitySetup</c>, so a <c>[SerializeField]</c> on them could never be filled.
    ///
    /// In a top-down world the sky is never drawn; it exists only through what it does to the
    /// ground, and these numbers are that.
    /// </summary>
    [CreateAssetMenu(fileName = "SkyStyle", menuName = "Valkur/World/Sky Style")]
    public class SkyStyle : ScriptableObject
    {
        // ── Cloud shadows ────────────────────────────────────────────────────────

        [Header("Cloud shadows")]
        [Tooltip("Cycles of the noise field per world unit. 0.035 makes a cloud ~30 units across, " +
                 "about a screen; smaller numbers make bigger, slower-reading clouds.")]
        public float cloudScale = 0.035f;

        [Tooltip("How much of the weather wind the clouds take, as a fraction. Clouds ride HIGHER " +
                 "than the leaves and read as slower, so well under 1.")]
        public float cloudSpeedFactor = 0.11f;

        [Tooltip("A slow vertical drift in noise units/s so a dead-calm day still moves.")]
        public float cloudDriftY = 0.012f;

        [Tooltip("Noise threshold above which the sky is cloud on a clear day. Higher = fewer clouds. " +
                 "The noise runs roughly 0.2..0.8.")]
        [Range(0f, 1f)] public float coverageClear = 0.64f;

        [Tooltip("The same threshold under heavy rain or snow: the sky closes over.")]
        [Range(0f, 1f)] public float coverageOvercast = 0.40f;

        [Tooltip("Width of the soft edge around the threshold. A cloud's shadow has no hard edge.")]
        [Range(0.01f, 0.5f)] public float cloudSoftness = 0.14f;

        [Tooltip("How dark the ground goes under a cloud at noon, 0..1. Multiplied by the daylight " +
                 "and reduced under an overcast sky, where the grade already does the darkening.")]
        [Range(0f, 1f)] public float cloudStrength = 0.36f;

        // ── Sun shadows ──────────────────────────────────────────────────────────

        [Header("Sun shadows")]
        [Tooltip("Alpha of the projected silhouette at full sun.")]
        [Range(0f, 1f)] public float sunShadowAlpha = 0.45f;

        [Tooltip("The shadow's colour, LINEAR. Almost black with a blue lean: ground in shadow is lit " +
                 "only by the sky. Measured: (0.04, 0.05, 0.12) read as a pale blue wash on the warm " +
                 "cobbles — the blue channel ended ABOVE the ground's — so it is a tenth of that.")]
        public Color sunShadowColor = new Color(0.010f, 0.012f, 0.030f, 1f);

        [Tooltip("Horizontal shear per unit of height at the horizon. 1.15 lays a 2-unit character " +
                 "2.3 units sideways at dawn.")]
        public float skewMax = 1.15f;

        [Tooltip("Vertical scale of the silhouette on the ground at noon (short).")]
        public float squashNoon = 0.22f;

        [Tooltip("Vertical scale at the horizon (long).")]
        public float squashHorizon = 0.62f;

        [Tooltip("Whether placed buildings and trees cast a projected shadow. Costs one extra " +
                 "sprite draw per building half in view.")]
        public bool buildingShadows = true;

        [Tooltip("How much rain or snow dims the sun shadows, 0..1. At 1 a heavy storm removes them.")]
        [Range(0f, 1f)] public float weatherDimming = 0.85f;

        // ── Contact blob ─────────────────────────────────────────────────────────

        [Header("Contact blob")]
        [Tooltip("Alpha of the soft ellipse under every creature's feet. It is the shadow of " +
                 "'standing on the ground', present at noon, at night and indoors.")]
        [Range(0f, 1f)] public float blobAlpha = 0.28f;

        [Tooltip("Width of the blob as a fraction of the body sprite's width.")]
        public float blobWidthFactor = 0.78f;

        [Tooltip("Height of the blob as a fraction of its own width.")]
        public float blobHeightFactor = 0.42f;

        // ── The sun's day window ─────────────────────────────────────────────────

        [Header("Sun window")]
        [Tooltip("Normalized time the sun rises. Mirrors DayNightCycle.DAWN_START.")]
        [Range(0f, 1f)] public float sunrise = 0.18f;

        [Tooltip("Normalized time the sun sets. Mirrors DayNightCycle.NIGHT_START.")]
        [Range(0f, 1f)] public float sunset = 0.84f;

        // ── Access ───────────────────────────────────────────────────────────────

        private static SkyStyle s_active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_active = null;

        /// <summary>
        /// The shipped asset, or a defaults instance when it is missing, so a stripped
        /// Resources folder costs the tuning and never the layer.
        /// </summary>
        public static SkyStyle Active
        {
            get
            {
                if (s_active != null) return s_active;
                s_active = Resources.Load<SkyStyle>("SkyStyle");
                if (s_active == null)
                {
                    s_active = CreateInstance<SkyStyle>();
                    s_active.name = "SkyStyle (defaults)";
                    s_active.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_active;
            }
        }
    }
}
