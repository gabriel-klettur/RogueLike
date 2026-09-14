using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Every judgement the bottom-left player panel makes, in one asset.
    ///
    /// <para><b>Why this exists.</b> The panel it replaces carried ~20 colour literals spread
    /// over five files (<c>HUDManager.PlayerPanel</c>, <c>HUDManager.XpBar</c>, <c>PlayerHUD</c>,
    /// <c>XpBarHUD</c>, <c>PlayerAbilityRowHUD</c>) and two of them disagreed: the XP fill was
    /// painted yellow by the builder and repainted blue by its own driver on the first frame.
    /// Nothing could be tuned without a recompile.</para>
    ///
    /// <para>Under <c>Resources/UI/</c> beside <c>WorldBarStyle</c>, for the same reason: the
    /// panel is built at runtime by <c>HUDManager</c> and has no inspector slot. It also carries
    /// the panel's shader, which is what pulls <c>Valkur/UI/HudFx</c> into a player build — a
    /// shader found only by <c>Shader.Find</c> is stripped.</para>
    ///
    /// <para><b>Geometry is in TEXELS.</b> One texel is one unit of the panel's own pixel space,
    /// drawn as a whole number of screen pixels (<see cref="HudPixelScaleFor"/>), so every edge of
    /// the frame lands on the pixel grid at every resolution instead of being resampled.</para>
    ///
    /// <para><b>The bar colours come from <c>WorldBarStyle</c></b> when
    /// <see cref="followWorldBarPalette"/> is on — the bars over the head and the bars in the
    /// corner are the same readout at two sizes, and the player sees both at once.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerHudStyle", menuName = "Valkur/UI/Player HUD Style", order = 41)]
    public class PlayerHudStyle : ScriptableObject
    {
        /// <summary>Path passed to <c>Resources.Load</c>.</summary>
        public const string ResourcePath = "UI/PlayerHudStyle";

        [Header("Rendering")]
        [Tooltip("Valkur/UI/HudFx. Referenced here so it ships in a build; without it the panel " +
                 "falls back to UI/Default (motes stop being additive, the portrait stops greying).")]
        public Shader hudFxShader;

        [Header("Pixel grid")]
        [Tooltip("Screen pixels one texel covers at the reference resolution. The live scale is " +
                 "this times the geometric mean of the resolution ratio, ROUNDED to a whole number.")]
        [Range(1f, 4f)] public float texelPixelsAtReference = 2f;

        public Vector2 referenceResolution = new Vector2(1600f, 800f);

        [Range(1, 8)] public int minPixelScale = 1;

        [Header("Layout (texels)")]
        [Range(0, 24)] public int marginTexels = 6;
        [Range(2, 12)] public int paddingTexels = 5;
        [Range(24, 80)] public int portraitTexels = 47;
        [Range(80, 220)] public int stackWidthTexels = 132;
        [Range(1, 10)] public int columnGapTexels = 4;
        [Range(9, 24)] public int healthBarTexels = 13;
        [Range(7, 16)] public int manaBarTexels = 9;
        [Range(6, 14)] public int energyBarTexels = 8;
        [Range(14, 32)] public int slotTexels = 20;
        [Range(4, 9)] public int xpBarTexels = 5;
        [Range(0, 6)] public int rowGapTexels = 2;
        [Range(11, 25)] public int medallionTexels = 17;

        [Header("Frame")]
        public Color outline = new Color(0.03f, 0.03f, 0.05f, 1f);
        public Color stoneLight = new Color(0.17f, 0.17f, 0.22f, 1f);
        public Color stoneDark = new Color(0.08f, 0.08f, 0.11f, 1f);
        public Color gold = new Color(0.90f, 0.76f, 0.38f, 1f);
        public Color goldShade = new Color(0.52f, 0.40f, 0.17f, 1f);
        // OPAQUE on purpose. The project renders in linear space, where a 4 % gap in alpha lets
        // about 18 % of a bright surface through once it is encoded for the screen — measured on
        // the first tooltip, which showed the health bar through its "96 %" card.
        public Color recess = new Color(0.05f, 0.05f, 0.08f, 1f);
        public Color bevelLight = new Color(1f, 1f, 1f, 0.10f);

        [Header("Bars")]
        [Tooltip("Take health, mana, chip and status colours from WorldBarStyle.")]
        public bool followWorldBarPalette = true;
        public Color health = new Color(0.30f, 0.88f, 0.34f, 1f);
        public Color healthLow = new Color(0.94f, 0.27f, 0.20f, 1f);
        public Color healthChip = new Color(1f, 0.93f, 0.85f, 0.95f);
        public Color heal = new Color(0.78f, 1f, 0.72f, 1f);
        public Color mana = new Color(0.36f, 0.55f, 1f, 1f);
        public Color manaChip = new Color(0.75f, 0.86f, 1f, 0.9f);
        public Color energy = new Color(0.96f, 0.58f, 0.18f, 1f);
        public Color energyChip = new Color(1.00f, 0.84f, 0.58f, 0.9f);
        public Color xp = new Color(1f, 0.80f, 0.30f, 1f);
        public Color xpChip = new Color(1f, 0.97f, 0.80f, 1f);
        public Color dashReady = new Color(0.20f, 0.86f, 1f, 1f);
        public Color dashCharging = new Color(0.30f, 0.42f, 0.52f, 1f);
        public Color notch = new Color(0f, 0f, 0f, 0.42f);
        [Range(0.05f, 0.6f)] public float lowThreshold = 0.3f;

        [Header("Text")]
        public Color text = new Color(1f, 0.98f, 0.93f, 1f);
        public Color textDim = new Color(0.72f, 0.74f, 0.82f, 1f);

        [Header("Slots")]
        public Color cooldownShade = new Color(0.02f, 0.02f, 0.05f, 0.72f);
        public Color lockedIcon = new Color(0.30f, 0.30f, 0.36f, 0.55f);
        public Color manaShortIcon = new Color(0.46f, 0.55f, 0.95f, 1f);
        [Range(0f, 1f)] public float readyFlashSeconds = 0.32f;
        [Tooltip("Only a cooldown at least this long is celebrated when it ends. The left click's " +
                 "is half a second, so holding it would otherwise fire a ring of sparks twice a " +
                 "second — a fountain, which is exactly what event motes must never become.")]
        [Range(0f, 10f)] public float readyFlashMinCooldown = 1.5f;

        [Header("Feel")]
        [Range(1f, 40f)] public float fillLerpSpeed = 11f;
        [Range(0f, 1f)] public float chipHoldSeconds = 0.3f;
        [Range(0.05f, 2f)] public float chipDrainSeconds = 0.45f;
        [Range(0f, 0.6f)] public float hitFlashSeconds = 0.12f;
        [Range(0, 4)] public int knockTexels = 1;
        [Range(0f, 0.6f)] public float knockSeconds = 0.2f;
        [Range(0f, 1f)] public float healGlowSeconds = 0.45f;
        [Range(0.2f, 4f)] public float heartbeatHz = 1.25f;
        [Range(0f, 2f)] public float levelUpSeconds = 0.9f;
        [Range(0f, 1f)] public float refusedFlashSeconds = 0.35f;

        [Header("Accents")]
        [Tooltip("The gem set in the top edge of the frame.")]
        public Color gem = new Color(0.72f, 0.12f, 0.16f, 1f);
        public Color gemLit = new Color(1f, 0.55f, 0.55f, 1f);
        [Tooltip("The level medallion's face, centre to rim.")]
        public Color medallionFace = new Color(0.22f, 0.15f, 0.10f, 1f);
        public Color medallionFaceRim = new Color(0.07f, 0.05f, 0.05f, 1f);
        [Tooltip("What the stone drifts to while the player is a spirit.")]
        public Color spiritStone = new Color(0.72f, 0.80f, 0.95f, 1f);
        [Tooltip("The red screen edge. DARK on purpose: alpha-blended over a blue river a bright red " +
                 "reads as magenta, while a dark one reads as the edges closing in.")]
        public Color dangerEdge = new Color(0.52f, 0.02f, 0.03f, 1f);

        [Header("Flashes (alpha is strength)")]
        public Color hitBarFlash = new Color(1f, 1f, 1f, 0.55f);
        public Color hitPortraitFlash = new Color(1f, 0.25f, 0.18f, 0.38f);
        public Color healPortraitFlash = new Color(0.7f, 1f, 0.6f, 0.35f);
        public Color levelPortraitFlash = new Color(1f, 0.86f, 0.45f, 0.5f);
        public Color levelBarFlash = new Color(1f, 1f, 0.85f, 0.85f);

        [Header("Portrait")]
        [Tooltip("The backdrop behind the head: a warm glow at the head, the frame's dark at the rim.")]
        public Color backdropCentre = new Color(0.34f, 0.25f, 0.18f, 1f);
        public Color backdropEdge = new Color(0.05f, 0.05f, 0.08f, 1f);
        [Tooltip("How much colour the portrait keeps at 0 HP. Scaled in from lowThreshold down.")]
        [Range(0f, 1f)] public float portraitSaturationAtZero = 0.2f;
        [Tooltip("The body height, in texels, the portrait aims to show the character at. The " +
                 "source is downsampled by a WHOLE factor towards it, never resampled.")]
        [Range(40, 200)] public int portraitBodyTexels = 112;

        [Header("Motes (event particles)")]
        [Range(0, 128)] public int moteCapacity = 72;
        [Range(0, 24)] public int motesOnHit = 10;
        [Range(0, 24)] public int motesOnHeal = 8;
        [Range(0, 16)] public int motesOnSpend = 3;
        [Range(0, 24)] public int motesOnReady = 6;
        [Range(0, 64)] public int motesOnLevelUp = 32;
        [Range(0, 16)] public int motesOnDashReady = 6;
        [Range(0.1f, 2f)] public float moteLifeSeconds = 0.55f;
        [Range(1f, 120f)] public float moteSpeedTexels = 34f;
        [Range(0f, 400f)] public float moteGravityTexels = 90f;

        [Header("Screen edge")]
        [Tooltip("Peak alpha of the red screen edge at 0 HP. It starts at lowThreshold.")]
        [Range(0f, 1f)] public float lowHealthVignetteAlpha = 0.34f;
        [Tooltip("Peak alpha of the red edge a blow flashes, for a blow that takes a quarter of max HP.")]
        [Range(0f, 1f)] public float hitVignetteAlpha = 0.22f;
        [Range(0f, 1f)] public float hitVignetteSeconds = 0.35f;

        // -- Derived ---------------------------------------------------------

        /// <summary>Screen pixels per texel for a screen of this size.</summary>
        public int HudPixelScaleFor(int screenWidth, int screenHeight)
        {
            return HudPixelScale(screenWidth, screenHeight, referenceResolution,
                                 texelPixelsAtReference, minPixelScale);
        }

        /// <summary>
        /// The rule behind <see cref="HudPixelScaleFor"/>, with every input explicit, so the
        /// rounding is testable without a canvas: 1600x800 is 2, 1920x1080 is 3, 3840x2160 is 5.
        /// </summary>
        public static int HudPixelScale(int screenWidth, int screenHeight, Vector2 reference,
                                        float texelPixelsAtReference, int minScale)
        {
            int floor = Mathf.Max(1, minScale);
            if (screenWidth <= 0 || screenHeight <= 0 || reference.x <= 0f || reference.y <= 0f)
                return floor;
            // The geometric mean is CanvasScaler's matchWidthOrHeight = 0.5, which every other
            // HUD canvas uses — so the panel grows with the rest of the HUD and only the ROUNDING
            // is new.
            float ratio = Mathf.Sqrt((screenWidth / reference.x) * (screenHeight / reference.y));
            return Mathf.Max(floor, Mathf.RoundToInt(texelPixelsAtReference * ratio));
        }

        /// <summary>Outer panel width in texels.</summary>
        public int PanelWidthTexels =>
            paddingTexels * 2 + portraitTexels + columnGapTexels + stackWidthTexels;

        /// <summary>Height of the stack of bars and slots beside the portrait.</summary>
        public int StackHeightTexels =>
            healthBarTexels + rowGapTexels + manaBarTexels + rowGapTexels + energyBarTexels +
            rowGapTexels + 1 + slotTexels;

        /// <summary>Height of the portrait row, which the stack must match.</summary>
        public int TopRowTexels => Mathf.Max(portraitTexels, StackHeightTexels);

        /// <summary>Outer panel height in texels.</summary>
        public int PanelHeightTexels =>
            paddingTexels * 2 + TopRowTexels + rowGapTexels + 1 + xpBarTexels;

        // -- Resolution ------------------------------------------------------

        private static PlayerHudStyle s_cached;
        private static bool s_looked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPlayerHudStyleStatics()
        {
            s_cached = null;
            s_looked = false;
        }

        /// <summary>The shipped asset, or a defaults-only instance when it is missing.</summary>
        public static PlayerHudStyle Active
        {
            get
            {
                if (s_cached != null) return s_cached;
                if (!s_looked)
                {
                    s_looked = true;
                    s_cached = Resources.Load<PlayerHudStyle>(ResourcePath);
                }
                if (s_cached == null)
                {
                    s_cached = CreateInstance<PlayerHudStyle>();
                    s_cached.name = "PlayerHudStyle (defaults)";
                    s_cached.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_cached;
            }
        }
    }
}
