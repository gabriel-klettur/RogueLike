using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Every decision the minimap and the full world map make about how they LOOK, in one
    /// asset under <c>Resources/UI/</c>.
    ///
    /// <para><b>Why under Resources.</b> Two reasons, and the second is the one that bites.
    /// The minimap is <c>AddComponent</c>-ed by <c>HUDBootstrap</c> and has no inspector slot
    /// (the <c>ChatSystem._catalog</c> defect). And the map is drawn by two custom shaders that
    /// nothing else references: <c>Shader.Find</c> finds them in the Editor and returns null
    /// in a player build, because a shader nobody references is stripped. The two
    /// <see cref="Shader"/> fields below are what pull them into the build.</para>
    ///
    /// <para><b>The defaults are a complete look.</b> <see cref="Active"/> never returns null
    /// — with no asset on disk it hands back a throwaway instance carrying these
    /// initialisers — so every field here must be a value that ships, not a placeholder.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "MinimapStyle", menuName = "Valkur/UI/Minimap Style", order = 41)]
    public class MinimapStyle : ScriptableObject
    {
        /// <summary>Path passed to <c>Resources.Load</c>.</summary>
        public const string ResourcePath = "UI/MinimapStyle";

        // -- Shaders -----------------------------------------------------------

        [Header("Shaders (referenced here so a player build keeps them)")]
        [Tooltip("Valkur/UI/MinimapComposite — terrain, fog, vision, grid, sonar, day/night.")]
        public Shader compositeShader;

        [Tooltip("Valkur/UI/MinimapAdditive — glows, pulses and particles over the map.")]
        public Shader additiveShader;

        // -- Terrain bake ------------------------------------------------------

        [Header("Terrain bake")]
        [Tooltip("Pixels per world unit of the baked terrain atlas. 8 keeps the map crisp at the " +
                 "closest zoom (radius 8) and costs ~34 MB for the shipped 400x250 world.")]
        [Range(2, 16)] public int bakePixelsPerUnit = 8;

        [Tooltip("Pixels per world unit the bake camera renders at before box-filtering down to " +
                 "bakePixelsPerUnit. 32 is the tiles' own PPU, so every source texel is averaged " +
                 "instead of point-sampled — lower is faster and noisier.")]
        [Range(8, 64)] public int renderPixelsPerUnit = 32;

        [Tooltip("Side of one bake chunk in world units. A chunk is one Camera.Render; measured " +
                 "at 3.7 ms for 32 units at 32 PPU.")]
        [Range(8, 64)] public int chunkUnits = 32;

        [Tooltip("Largest atlas dimension in pixels. A world too big for it lowers the bake PPU " +
                 "rather than failing.")]
        public int maxAtlasSize = 4096;

        [Tooltip("Milliseconds per frame the bake may spend. At least one chunk always bakes, so " +
                 "a slow machine still makes progress.")]
        public float bakeBudgetMs = 4f;

        [Tooltip("Seconds between scans for buildings that moved, appeared or vanished.")]
        public float buildingScanSeconds = 1f;

        // -- Terrain look ------------------------------------------------------

        [Header("Terrain look")]
        [Tooltip("Colour saturation applied to the baked terrain.")]
        public float saturation = 1.10f;
        [Tooltip("Contrast around mid grey applied to the baked terrain.")]
        public float contrast = 1.08f;
        [Tooltip("Overall brightness of the terrain. Below 1 so glyphs read over it.")]
        public float brightness = 0.92f;
        [Tooltip("Strength of the ink line drawn along luminance edges — walls, roofs, paths.")]
        [Range(0f, 1f)] public float edgeInk = 0.45f;
        [Tooltip("Brightness of explored ground the player cannot currently see.")]
        [Range(0.2f, 1f)] public float rememberedBrightness = 0.62f;
        [Tooltip("Saturation of explored ground the player cannot currently see.")]
        [Range(0f, 1f)] public float rememberedSaturation = 0.55f;
        [Tooltip("Cartographic grid line strength. Zero turns it off.")]
        [Range(0f, 0.3f)] public float gridStrength = 0.05f;
        [Tooltip("World units between grid lines.")]
        public float gridSpacing = 10f;
        [Tooltip("Travelling glint over water, detected from the baked colour. Zero turns it off.")]
        [Range(0f, 1.5f)] public float waterShimmer = 0.6f;
        [Tooltip("Colour of explored places the world has nothing drawn at.")]
        public Color voidColor = new Color(0.035f, 0.04f, 0.06f, 1f);

        // -- Fog ---------------------------------------------------------------

        [Header("Fog of war")]
        [Tooltip("World radius around the player that counts as explored, and as currently seen.")]
        public float revealRadius = 14f;
        [Tooltip("Unexplored ink, where the cloud noise is thin.")]
        public Color fogInkLight = new Color(0.17f, 0.19f, 0.26f, 1f);
        [Tooltip("Unexplored ink, where the cloud noise is thick.")]
        public Color fogInkDark = new Color(0.035f, 0.04f, 0.065f, 1f);
        [Tooltip("Glow along the frontier between explored and unexplored.")]
        public Color frontierGlow = new Color(0.98f, 0.78f, 0.40f, 0.55f);
        [Tooltip("Strength of the drifting cloud pattern in the unexplored ink.")]
        [Range(0f, 1f)] public float cloudStrength = 0.65f;
        [Tooltip("World-space scale of the cloud pattern. Smaller is bigger clouds.")]
        public float cloudScale = 0.09f;
        [Tooltip("Cloud drift speed in world units per second.")]
        public float cloudSpeed = 0.35f;
        [Tooltip("How much of the terrain shows through unexplored ink. A faint echo reads as " +
                 "'unknown' rather than 'nothing'; high values spoil the layout.")]
        [Range(0f, 0.3f)] public float unexploredEcho = 0.045f;
        [Tooltip("Strength of the diagonal cartographer's hatching over unexplored ground.")]
        [Range(0f, 1f)] public float hatchStrength = 0.38f;
        [Tooltip("World units between hatch lines.")]
        public float hatchSpacing = 2.4f;

        // -- World response ----------------------------------------------------

        [Header("Time and weather")]
        [Tooltip("How much of the day/night light colour reaches the map.")]
        [Range(0f, 1f)] public float dayNightTint = 0.55f;
        [Tooltip("Desaturation while it rains, at Heavy.")]
        [Range(0f, 1f)] public float rainDesaturation = 0.30f;
        [Tooltip("Whitening while it snows, at Heavy.")]
        [Range(0f, 1f)] public float snowWhiten = 0.22f;

        // -- Glyphs ------------------------------------------------------------

        [Header("Glyph colours")]
        public Color playerColor     = new Color(1.00f, 0.97f, 0.88f, 1f);
        public Color enemyColor      = new Color(0.96f, 0.28f, 0.22f, 1f);
        public Color eliteColor      = new Color(1.00f, 0.55f, 0.18f, 1f);
        public Color bossColor       = new Color(0.86f, 0.12f, 0.16f, 1f);
        public Color allyColor       = new Color(0.35f, 0.88f, 1.00f, 1f);
        public Color neutralColor    = new Color(0.95f, 0.90f, 0.70f, 1f);
        public Color vendorColor     = new Color(1.00f, 0.84f, 0.32f, 1f);
        public Color questOfferColor = new Color(1.00f, 0.82f, 0.15f, 1f);
        public Color questTurnInColor= new Color(0.55f, 1.00f, 0.40f, 1f);
        public Color objectiveColor  = new Color(0.55f, 0.82f, 1.00f, 1f);
        public Color portalColor     = new Color(0.50f, 0.72f, 1.00f, 1f);
        public Color doorColor       = new Color(0.98f, 0.78f, 0.42f, 1f);
        public Color exitColor       = new Color(0.45f, 0.95f, 1.00f, 1f);
        public Color altarColor      = new Color(0.85f, 0.95f, 1.00f, 1f);
        public Color corpseColor     = new Color(0.80f, 0.78f, 0.85f, 1f);
        public Color waypointColor   = new Color(0.95f, 0.45f, 1.00f, 1f);

        [Header("Glyph sizes (reference pixels on the minimap)")]
        public float playerSize    = 17f;
        public float enemySize     = 7f;
        public float eliteSize     = 11f;
        public float bossSize      = 17f;
        public float allySize      = 11f;
        public float neutralSize   = 7f;
        public float vendorSize    = 14f;
        public float questSize     = 15f;
        public float portalSize    = 14f;
        public float doorSize      = 11f;
        public float altarSize     = 13f;
        public float corpseSize    = 13f;
        public float waypointSize  = 15f;
        public float edgePinSize   = 11f;

        // -- Chrome ------------------------------------------------------------

        [Header("Chrome")]
        public Color ringGold      = new Color(0.90f, 0.74f, 0.38f, 1f);
        public Color ringHighlight = new Color(1.00f, 0.93f, 0.68f, 1f);
        public Color ringShadow    = new Color(0.34f, 0.24f, 0.10f, 1f);
        public Color damageFlash   = new Color(1.00f, 0.22f, 0.16f, 1f);
        public Color northColor    = new Color(1.00f, 0.42f, 0.30f, 1f);
        public Color cardinalColor = new Color(0.96f, 0.86f, 0.56f, 1f);

        // -- Life --------------------------------------------------------------

        [Header("Life")]
        [Tooltip("Seconds between sonar pings from the player. Zero turns the ping off.")]
        public float sonarPeriod = 3.4f;
        [Tooltip("Peak brightness of the sonar ring.")]
        [Range(0f, 1f)] public float sonarStrength = 0.22f;
        [Tooltip("Motes drawn per second along freshly revealed ground, at most.")]
        public float revealMotesPerSecond = 36f;
        [Tooltip("Ambient motes drifting over the map at night.")]
        [Range(0, 24)] public int nightMotes = 9;
        [Tooltip("Seconds the ring stays red after the player is hit.")]
        public float damageFlashSeconds = 0.5f;

        // -- Resolution --------------------------------------------------------

        private static MinimapStyle s_cached;
        private static bool s_looked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetMinimapStyleStatics()
        {
            s_cached = null;
            s_looked = false;
        }

        /// <summary>The shipped style, or a throwaway instance carrying the defaults. Never null.</summary>
        public static MinimapStyle Active
        {
            get
            {
                if (!s_looked)
                {
                    s_cached = Resources.Load<MinimapStyle>(ResourcePath);
                    s_looked = true;
                }
                if (s_cached == null)
                {
                    s_cached = CreateInstance<MinimapStyle>();
                    s_cached.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_cached;
            }
        }

        /// <summary>Resolve a shader: the referenced one first, then by name (Editor fallback).</summary>
        public static Shader ResolveShader(Shader referenced, string name)
        {
            if (referenced != null) return referenced;
            return Shader.Find(name);
        }
    }
}
