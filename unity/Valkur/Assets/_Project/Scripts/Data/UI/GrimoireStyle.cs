using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Every decision the grimoire window makes about how it looks and how long things take,
    /// in one asset.
    ///
    /// <para>It lives under <c>Resources/UI/</c> for the reason every other tuning asset in
    /// this project does: <c>SpellTreeHUD</c> is created on a bare GameObject by the character
    /// sheet and has no inspector slot, so a <c>[SerializeField]</c> on it could never be
    /// filled — the <c>ChatSystem._catalog</c> defect.</para>
    ///
    /// <para><b>What is NOT here:</b> the colours every HUD surface shares. Those come from
    /// <see cref="HudTheme"/> — stone, outline, recess, gold, text — and the one colour that
    /// varies per school is the school's own <c>accent</c>, which is authored on the
    /// <c>SpellTree</c>. A grimoire that declared its own stone would be the sixth dialect on
    /// screen, which is what its audit was about.</para>
    ///
    /// <para>Sizes are in TEXELS (HUD_VISUAL_LANGUAGE.md R1), drawn at a whole number of
    /// screen pixels per texel.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "GrimoireStyle", menuName = "Valkur/UI/Grimoire Style")]
    public sealed class GrimoireStyle : ScriptableObject
    {
        public const string ResourcePath = "UI/GrimoireStyle";

        // ── The window, in texels ────────────────────────────────────────────

        [Header("Window")]
        [Range(180, 420)] public int widthTexels = 340;
        [Range(120, 300)] public int heightTexels = 208;
        [Range(2, 12)]    public int paddingTexels = 6;
        [Range(9, 16)]    public int titleBarTexels = 11;
        [Range(8, 14)]    public int footerTexels = 11;

        [Header("School rail")]
        [Tooltip("Width of the vertical school rail. Vertical rather than a strip of tabs " +
                 "because nine horizontal tabs truncated EIGHT of the nine shipped school " +
                 "names, and a rail scales to a twelfth school with no redesign.\n\n" +
                 "Sized for the LONGEST name at ONE type size, which is what the rail is for. " +
                 "At 62 the name box was 37 texels and, measured against the shipped nine, " +
                 "only 'Fulgor' fitted at a readable size: the per-label auto-fit then solved " +
                 "each one separately and produced NINE different sizes, three of them wrapped " +
                 "onto their own counter. " +
                 "100 rather than the 96 that first fitted: at 96 the longest shipped name " +
                 "cleared its box by ONE texel, which is a fit by luck. The rail is the piece " +
                 "that can afford the width - it is clamped to a third of the inner column and " +
                 "sits at two thirds of that - and the board is the piece whose content SCALES " +
                 "to whatever it is given, so width taken from the board costs nothing that " +
                 "cannot be zoomed back.")]
        [Range(40, 120)] public int railWidthTexels = 100;
        [Range(10, 24)] public int railRowTexels = 18;
        [Range(0, 4)]   public int railRowGapTexels = 1;

        [Header("Detail card")]
        [Range(70, 160)] public int cardWidthTexels = 92;
        [Range(20, 48)]  public int cardIconTexels = 32;

        [Header("Board")]
        [Tooltip("Diameter of a node's socket, in texels. " +
                 "26 is MEASURED, not chosen: it is the largest size at which all nine shipped " +
                 "schools still fit their column at zoom 1, so a node never changes size when " +
                 "the player clicks another school. At 28 the widest school (arcane) drops to " +
                 "0.935 and draws the same 52 px while giving up that uniformity.")]
        [Range(12, 40)] public int nodeTexels = 26;

        [Tooltip("Icon inset inside the socket, per side.")]
        [Range(2, 8)] public int nodeIconInsetTexels = 4;

        [Tooltip("Thickness of a prerequisite link. Three rather than two: a two-texel line " +
                 "beside a 52 px socket reads as a hairline, and the chain is the half of the " +
                 "board that says where the player can go.")]
        [Range(1, 4)] public int linkTexels = 3;

        [Tooltip("Height of the caption under a node — its name, and the reason it is locked.")]
        [Range(4, 12)] public int captionTexels = 7;

        // ── The board's own tones ────────────────────────────────────────────
        // Every one of these was a literal inside a view. They are FACTORS and ALPHAS rather
        // than colours because the hue always comes from the school's own accent or from
        // HudTheme: a grimoire that declared its own stone would be a sixth dialect on screen,
        // which is what its audit was about.

        [Header("Node")]
        [Tooltip("How far a locked socket is dimmed from its school's accent.")]
        [Range(0.1f, 1f)] public float lockedSocketFactor = 0.42f;

        [Tooltip("How far the plate under a LEARNED node is dimmed from the accent.")]
        [Range(0.1f, 1f)] public float learnedPlateFactor = 0.35f;

        [Range(0f, 1f)] public float learnedPlateAlpha = 0.95f;
        [Range(0f, 1f)] public float lockedPlateAlpha = 0.92f;

        [Tooltip("A locked node's art is DRAINED, not hidden: the player should be able to " +
                 "recognise the spell they are saving for.")]
        public Color lockedIconTint = new Color(0.62f, 0.63f, 0.70f, 0.70f);

        [Range(0.1f, 1f)] public float roleMarkLitFactor = 0.95f;
        [Range(0.1f, 1f)] public float roleMarkDimFactor = 0.45f;

        [Tooltip("Halo alpha on a node the player can buy right now — the low and high ends " +
                 "of the only thing on the board that moves at rest.")]
        [Range(0f, 1f)] public float haloBreathLow = 0.14f;
        [Range(0f, 1f)] public float haloBreathHigh = 0.38f;

        [Range(0f, 1f)] public float haloLearnedAlpha = 0.16f;

        [Header("Chain")]
        [Tooltip("A chain whose parent is bought: the frontier of what the player can reach.")]
        [Range(0f, 1f)] public float linkOpenAlpha = 0.85f;

        [Tooltip("A chain one purchase away from opening.")]
        [Range(0f, 1f)] public float linkNextAlpha = 0.34f;

        [Tooltip("A chain somewhere else entirely.")]
        [Range(0f, 1f)] public float linkDarkAlpha = 0.55f;

        [Range(0f, 1f)] public float linkFlowAlpha = 0.95f;

        [Header("Card")]
        [Tooltip("How far a disabled Learn button is dimmed from the colour it would have had.")]
        [Range(0.1f, 1f)] public float disabledButtonFactor = 0.35f;

        // ── The role filter ──────────────────────────────────────────────────

        [Header("Role filter")]
        [Range(8, 20)] public int filterChipTexels = 13;
        [Range(1, 4)]  public int filterChipGapTexels = 2;

        [Tooltip("How far a node the filter excludes is faded. It DIMS rather than hides: a " +
                 "tree missing branches does not read as a tree, which is the same call the " +
                 "Controls editor makes for its own search.")]
        [Range(0f, 1f)] public float filteredAlpha = 0.20f;

        // ── Motion (HUD_VISUAL_LANGUAGE.md R7) ───────────────────────────────

        [Header("Motion")]
        [Range(0f, 0.5f)] public float openSeconds = 0.12f;
        [Range(0f, 0.5f)] public float closeSeconds = 0.09f;
        [Range(0, 8)]     public int openRiseTexels = 4;

        [Tooltip("How long the prerequisite chain stays lit after a purchase. It is an EVENT: " +
                 "a link that flows forever is movement at rest, which R7 forbids and which " +
                 "is what makes the purchase burst stop meaning anything.")]
        [Range(0f, 1.2f)] public float chainFlowSeconds = 0.35f;

        [Tooltip("Period of the slow breath on the ONE node the player can afford right now. " +
                 "The only thing on the board that moves while nothing happens, and it moves " +
                 "because it is an invitation to act.")]
        [Range(0.8f, 4f)] public float availablePulseSeconds = 2.2f;

        [Range(0f, 0.6f)] public float refusalSeconds = 0.28f;
        [Range(0, 3)]     public int refusalShakeTexels = 1;
        [Range(0f, 0.6f)] public float schoolSwapSeconds = 0.22f;

        // ── Motes (R8) ───────────────────────────────────────────────────────

        [Header("Motes")]
        [Range(16, 160)] public int moteCapacity = 96;
        [Range(6, 30)]   public int learnMotes = 16;
        [Range(1, 8)]    public int affordableMotes = 3;
        [Range(2, 16)]   public int schoolSwapMotes = 9;
        [Range(2, 16)]   public int pointsGainedMotes = 6;
        [Range(8, 40)]   public int capstoneMotes = 24;
        [Range(0.15f, 0.9f)] public float moteLifeSeconds = 0.55f;
        [Range(0.3f, 1.2f)]  public float capstoneMoteLifeSeconds = 0.9f;

        // ── Sound ────────────────────────────────────────────────────────────

        [Header("Sound")]
        [Range(0f, 1f)] public float volume = 0.5f;

        [Tooltip("Catalogue ids, tried first and gated on HasSfx — an unresolved id warns once " +
                 "BY DESIGN, and a speculative one must not push a warning into a console this " +
                 "project requires to be clean. Empty or unresolved falls back to a synthesised " +
                 "tone, the same answer IceWallAudio and ShieldAudio give.")]
        public string openSfxId = string.Empty;
        public string learnSfxId = string.Empty;
        public string refuseSfxId = string.Empty;
        public string schoolSfxId = string.Empty;

        // ── Shaders ──────────────────────────────────────────────────────────

        [Header("Shaders")]
        [Tooltip("The additive material the mote layer draws with. Same shader the player " +
                 "panel uses; assigned here so a player build never reaches for Shader.Find.")]
        public Shader hudFxShader;

        // ── Accessor ─────────────────────────────────────────────────────────

        private static GrimoireStyle s_cached;
        private static bool s_looked;

        /// <summary>Domain Reload is OFF, so the cache has to be cleared explicitly.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_cached = null;
            s_looked = false;
        }

        /// <summary>
        /// The shipped asset, or a defaults instance when none is present. Never null: a
        /// window that cannot draw because an asset is missing is worse than one drawn with
        /// the values its own source declares.
        /// </summary>
        public static GrimoireStyle Active
        {
            get
            {
                if (s_cached != null) return s_cached;
                if (!s_looked)
                {
                    s_looked = true;
                    s_cached = Resources.Load<GrimoireStyle>(ResourcePath);
                }
                if (s_cached == null)
                {
                    s_cached = CreateInstance<GrimoireStyle>();
                    s_cached.name = "GrimoireStyle (defaults)";
                    s_cached.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_cached;
            }
        }
    }
}
