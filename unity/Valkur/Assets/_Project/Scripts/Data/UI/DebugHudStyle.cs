using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Every judgement the debug HUD makes, in one asset: the tool dialect of
    /// <c>.github/HUD_VISUAL_LANGUAGE.md</c> section 6.
    ///
    /// <para><b>Same theme, different accent.</b> The surfaces (outline, stone, recess, text)
    /// FOLLOW <see cref="PlayerHudStyle"/> when <see cref="followPlayerHudTokens"/> is on, so a
    /// screenshot of a bug report looks like this game — they are copied here only so the asset
    /// can stand alone. What a tool never takes is what the game uses to MEAN something: gold,
    /// the ornamental bevel, the green of the player's health and the blue of mana. Its state
    /// colours are a triad of its own (H4), and every one of them is paired with a shape or a
    /// word, never carried by colour alone.</para>
    ///
    /// <para>Under <c>Resources/UI/</c> for the reason every HUD style is: the overlay is
    /// <c>AddComponent</c>-ed by <c>HUDBootstrap</c> and has no inspector slot. A missing asset
    /// resolves to a defaults-only instance, so the overlay never depends on it existing.</para>
    ///
    /// <para><b>Geometry is in TEXELS</b>, drawn at <see cref="PlayerHudStyle.HudPixelScaleFor"/>
    /// screen pixels each — the same rule as the player panel, so the two land on the same grid.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "DebugHudStyle", menuName = "Valkur/UI/Debug HUD Style", order = 43)]
    public class DebugHudStyle : ScriptableObject
    {
        /// <summary>Path passed to <c>Resources.Load</c>.</summary>
        public const string ResourcePath = "UI/DebugHudStyle";

        [Header("Surfaces (follow the player panel)")]
        [Tooltip("Take outline, stone, recess and text from PlayerHudStyle at runtime, so the tool and " +
                 "the game share one set of surface colours until HudTheme exists.")]
        public bool followPlayerHudTokens = true;
        public Color outline = new Color(0.03f, 0.03f, 0.05f, 1f);
        public Color panel = new Color(0.08f, 0.08f, 0.11f, 1f);
        public Color header = new Color(0.17f, 0.17f, 0.22f, 1f);
        public Color recess = new Color(0.05f, 0.05f, 0.08f, 1f);
        public Color text = new Color(1f, 0.98f, 0.93f, 1f);
        public Color textDim = new Color(0.72f, 0.74f, 0.82f, 1f);

        [Header("Accent (tool only)")]
        [Tooltip("Section titles. A desaturated slate, deliberately not gold: gold means importance in the game.")]
        public Color title = new Color(0.58f, 0.80f, 0.84f, 1f);
        [Tooltip("The dashed budget lines across the frame graph.")]
        public Color budgetLine = new Color(0.72f, 0.74f, 0.82f, 0.38f);

        [Header("State triad (H4) — never the game's green or blue")]
        public Color good = new Color(0.30f, 0.80f, 0.72f, 1f);
        [Tooltip("Orange, not amber: an amber within a few degrees of the player panel's gold read as " +
                 "'experience' beside the XP bar (measured hue gap 0.011, now 0.038).")]
        public Color warn = new Color(0.98f, 0.60f, 0.22f, 1f);
        public Color bad = new Color(0.95f, 0.32f, 0.45f, 1f);

        [Header("Factions (the minimap's own dot colours)")]
        public Color hostile = new Color(0.96f, 0.28f, 0.22f, 1f);
        public Color neutral = new Color(0.95f, 0.90f, 0.70f, 1f);
        public Color ally = new Color(0.46f, 0.86f, 0.98f, 1f);

        [Header("Layout (texels)")]
        [Range(96, 220)] public int widthTexels = 150;
        [Range(1, 8)] public int paddingTexels = 3;
        [Range(7, 12)] public int rowTexels = 8;
        [Range(8, 14)] public int headerTexels = 10;
        [Range(12, 48)] public int graphTexels = 26;
        [Range(0, 6)] public int sectionGapTexels = 2;
        [Range(24, 96)] public int chipGraphWidthTexels = 48;

        [Header("Frame budget")]
        [Tooltip("At or under this a frame is GOOD. 17.5 ms keeps a 60 Hz frame with vsync jitter in the green.")]
        [Range(4f, 40f)] public float goodMs = 17.5f;
        [Tooltip("At or under this a frame is a WARNING; above it, BAD. 34 ms is a 30 Hz frame.")]
        [Range(8f, 80f)] public float warnMs = 34f;
        [Tooltip("Top of the frame graph. FIXED, never auto-scaled: an auto-scaling graph redraws every " +
                 "frame the moment one spike arrives, and the budget lines stop meaning a place.")]
        [Range(20f, 200f)] public float graphCeilingMs = 50f;

        [Header("Readouts")]
        [Range(0.05f, 1f)] public float rebuildSeconds = 0.2f;
        [Range(2f, 40f)] public float nearbyRadius = 15f;
        [Range(1, 8)] public int nearbyRows = 4;
        [Range(0f, 0.5f)] public float fadeSeconds = 0.12f;

        [Header("Motes (events only, R8)")]
        [Range(0, 128)] public int moteCapacity = 48;
        [Range(0, 16)] public int motesOnHitch = 5;
        [Range(0, 16)] public int motesOnGc = 2;
        [Range(0, 16)] public int motesOnError = 4;
        [Range(0, 16)] public int motesOnStateChange = 8;
        [Range(0, 24)] public int motesOnCopy = 8;
        [Range(0.1f, 0.6f)] public float moteLifeSeconds = 0.5f;
        [Range(1f, 80f)] public float moteSpeedTexels = 26f;

        // -- Derived -----------------------------------------------------------

        /// <summary>The state colour for a frame of <paramref name="ms"/>.</summary>
        public Color ForFrame(float ms) => ms <= goodMs ? good : ms <= warnMs ? warn : bad;

        /// <summary>0 good, 1 warning, 2 bad. The word that goes with the colour.</summary>
        public int GradeOf(float ms) => ms <= goodMs ? 0 : ms <= warnMs ? 1 : 2;

        /// <summary>Surface colours with the player panel's tokens folded in when following.</summary>
        public void ResolveSurfaces(out Color outlineC, out Color panelC, out Color headerC,
                                    out Color recessC, out Color textC, out Color textDimC)
        {
            outlineC = outline; panelC = panel; headerC = header;
            recessC = recess; textC = text; textDimC = textDim;
            if (!followPlayerHudTokens) return;
            var p = PlayerHudStyle.Active;
            if (p == null) return;
            outlineC = p.outline;
            panelC = p.stoneDark;
            headerC = p.stoneLight;
            recessC = p.recess;
            textC = p.text;
            textDimC = p.textDim;
        }

        // -- Resolution --------------------------------------------------------

        private static DebugHudStyle s_cached;
        private static bool s_looked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDebugHudStyleStatics()
        {
            s_cached = null;
            s_looked = false;
        }

        /// <summary>The shipped asset, or a defaults-only instance when it is missing.</summary>
        public static DebugHudStyle Active
        {
            get
            {
                if (s_cached != null) return s_cached;
                if (!s_looked)
                {
                    s_looked = true;
                    s_cached = Resources.Load<DebugHudStyle>(ResourcePath);
                }
                if (s_cached == null)
                {
                    s_cached = CreateInstance<DebugHudStyle>();
                    s_cached.name = "DebugHudStyle (defaults)";
                    s_cached.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_cached;
            }
        }
    }
}
