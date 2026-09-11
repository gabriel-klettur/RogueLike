using UnityEngine;
using Valkur.Core.UI;

namespace Valkur.Data
{
    /// <summary>
    /// Every judgement the bars over an entity's head make, in one asset.
    ///
    /// <para><b>Why this exists.</b> The three world bars shipped as three components that each
    /// re-declared the other two's geometry: <c>WorldManaBar</c> and <c>WorldDashBar</c> both
    /// carried private copies of <c>healthBarMargin 0.12</c>, <c>healthBarH 0.1</c>,
    /// <c>dashBarH 0.07</c> and <c>dashGap 0.06</c> in order to stack above a bar they could not
    /// ask. Changing the health bar's height moved the health bar and left the other two where
    /// they were, silently. Their colours were likewise three separate sets of literals on three
    /// <c>AddComponent</c>-ed components — the unreachable-field shape this project has hit in
    /// <c>ChatSystem._catalog</c>, <c>DeathSequenceController</c> and <c>CurrencyWallet</c>: a
    /// <c>[SerializeField]</c> nobody can ever fill because no scene contains the object.</para>
    ///
    /// <para>Under <c>Resources/</c> for that same reason, and for that reason only: every
    /// reader is created at runtime by <c>EntitySetup</c> and has no inspector slot. One small
    /// asset; nothing outside it is pulled into the build by it.</para>
    ///
    /// <para><b>Geometry is authored in TEXELS, never in world units.</b> A texel is 1/16 of a
    /// world unit, which is exactly the grid <c>CameraSetup.SnapOrthoSize</c> keeps whole on
    /// screen — see <see cref="WorldBarGeometry"/> for why that is the difference between a
    /// crisp readout and one whose every edge sits at 3.2 pixels.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "WorldBarStyle", menuName = "Valkur/UI/World Bar Style", order = 40)]
    public class WorldBarStyle : ScriptableObject
    {
        /// <summary>Path passed to <c>Resources.Load</c>. Mirrored by AssetConventionsTests.</summary>
        public const string ResourcePath = "UI/WorldBarStyle";

        // -- Shape -----------------------------------------------------------

        [Header("Shape (texels - 16 per world unit)")]
        [Tooltip("Take the bar's width from the entity's own body instead of one constant. " +
                 "Off reproduces the historical fixed width for every creature in the game.")]
        public bool widthFollowsBody = true;

        [Tooltip("Narrowest a bar may get, whatever the body measures.")]
        [Range(8, 48)] public int minWidthTexels = 14;

        [Tooltip("Widest a bar may get. A colossus must not get a bar as wide as it is.")]
        [Range(8, 64)] public int maxWidthTexels = 30;

        [Tooltip("Width used when the entity has no measurable body sprite.")]
        [Range(8, 64)] public int fallbackWidthTexels = 20;

        [Tooltip("A bar never gets wider than this fraction of the creature's own height. It is " +
                 "what bounds art whose frames are not trimmed to their alpha: the legacy valkyrie " +
                 "strips are 128 px SQUARE cells, so her sprite measures 2.0 x 2.0 whatever she " +
                 "actually fills, and a width read straight off the rect gave her a bar 1.6x her " +
                 "drawn body. Measured on trimmed art, the dwarf is 0.656 wide over tall, so this " +
                 "changes nothing for the five wave3 characters.")]
        [Range(0.3f, 1.5f)] public float maxWidthFractionOfHeight = 0.7f;

        [Tooltip("Total height of the health row, frame included.")]
        [Range(3, 10)] public int healthRowTexels = 6;

        [Tooltip("Total height of the resource row (mana + dash pip), frame included.")]
        [Range(3, 8)] public int resourceRowTexels = 4;

        [Tooltip("Blank texels between two rows.")]
        [Range(0, 4)] public int rowGapTexels = 1;

        [Tooltip("Blank texels between the top of the body sprite and the first row.")]
        [Range(0, 8)] public int headMarginTexels = 1;

        [Tooltip("Side of the square dash pip that sits at the right end of the resource row.")]
        [Range(3, 8)] public int pipTexels = 6;

        [Tooltip("Side of a status icon. The hand-drawn glyphs need EIGHT to be told apart - " +
                 "measured by reducing them to 5, 6, 8 and 10 and looking: at 5 all eight are " +
                 "indistinct, at 6 three of them read, at 8 all of them do. Their ornate frames " +
                 "are dropped in the reduction, which is what buys that: the glyph is only " +
                 "46-64% of the drawn icon, so cropping to it nearly doubles the resolution " +
                 "available to the part that carries the meaning. " +
                 "TEN with the COLOUR sheet, which keeps its gold frames: measured at 8, 10, 12 " +
                 "and 14, colour does most of the identifying - at 8 the eight are told apart by " +
                 "hue alone with the silhouettes gone to mush, and at 10 by hue AND shape. The " +
                 "only genuinely confusable pair is Poison against Root, both green, and 10 is " +
                 "where their silhouettes separate.")]
        [Range(4, 12)] public int iconTexels = 10;

        [Tooltip("Blank texels between two status icons.")]
        [Range(0, 4)] public int iconGapTexels = 1;

        [Tooltip("How many status icons are drawn before the row collapses into an overflow pip. " +
                 "A row wider than the creature it describes stops being a readout.")]
        [Range(1, 8)] public int maxIcons = 4;

        [Tooltip("Draw the dark recess behind the fill. OFF leaves the bar's empty part fully " +
                 "transparent, so the world shows through and the FRAME becomes the scale the eye " +
                 "measures the fill against - which is how a glass-tube bar reads, and what the " +
                 "hollow hand-drawn frames were made for. The cost is real and was stated before " +
                 "it was chosen: over pale ground an empty bar has no contrast of its own.")]
        public bool drawPlate = false;

        [Tooltip("Draw quarter marks across the health row. They are what lets the bar be read " +
                 "without relying on its colour, which is half of the colour-blind problem.")]
        public bool showQuarterNotches = false;

        [Tooltip("Narrowest bar that still gets quarter marks - below this they are noise.")]
        [Range(6, 40)] public int notchMinWidthTexels = 12;

        // -- Colour ----------------------------------------------------------

        [Header("Colour - plate and frame")]
        [Tooltip("The recess the fill sits in. Deliberately well above the frame's value: the " +
                 "first live capture had them at 0.07 and 0.05, one percent of a channel apart, " +
                 "and the whole readout rendered as a single black slab with no frame in it - " +
                 "the exact failure the old bars had, reintroduced by picking two dark colours.")]
        public Color plate = new Color(0.15f, 0.15f, 0.19f, 0.97f);

        [Tooltip("Frame of an ordinary creature's bar. Near black, so it reads as an outline " +
                 "against both the plate inside it and the world behind it.")]
        public Color frameNormal = new Color(0.02f, 0.02f, 0.03f, 0.98f);

        [Tooltip("Frame of an ally's bar.")]
        public Color frameAlly = new Color(0.35f, 0.80f, 0.45f, 0.95f);

        [Tooltip("Frame of an elite's bar.")]
        public Color frameElite = new Color(0.80f, 0.80f, 0.88f, 0.95f);

        [Tooltip("Frame of a boss's bar.")]
        public Color frameBoss = new Color(0.95f, 0.78f, 0.25f, 1f);

        [Tooltip("Frame of the player's own bar.")]
        public Color framePlayer = new Color(0.02f, 0.02f, 0.04f, 0.98f);

        [Header("Colour - health")]
        public Color healthPlayer = new Color(0.30f, 0.88f, 0.34f, 1f);
        public Color healthAlly = new Color(0.36f, 0.85f, 0.52f, 1f);
        public Color healthHostile = new Color(0.86f, 0.24f, 0.22f, 1f);

        [Tooltip("What a bar turns into below the low threshold. A warning, not a second identity.")]
        public Color healthLow = new Color(0.96f, 0.72f, 0.16f, 1f);

        [Tooltip("The delayed chunk left behind by a blow. Bright, so the eye catches the SIZE " +
                 "of the hit rather than only its outcome.")]
        public Color healthChip = new Color(1f, 0.93f, 0.85f, 0.95f);

        [Range(0f, 1f), Tooltip("Fraction of max HP below which the bar switches to the low colour.")]
        public float lowThreshold = 0.3f;

        [Header("Colour - resources")]
        public Color mana = new Color(0.36f, 0.55f, 1f, 1f);

        [Tooltip("The ghost of mana just spent. Same job as the health chip.")]
        public Color manaSpent = new Color(0.75f, 0.86f, 1f, 0.9f);

        public Color dashReady = new Color(0.20f, 0.86f, 1f, 1f);
        public Color dashCharging = new Color(0.32f, 0.55f, 0.68f, 1f);

        [Header("Colour - quarter marks")]
        public Color notch = new Color(0f, 0f, 0f, 0.55f);

        [Header("Colour - status icons, in StatusEffectKind order")]
        [Tooltip("Burn, Poison, Stun, Freeze, Slow, Root, Vulnerable, Marked. Indexed by the " +
                 "enum's integer value, which is why that enum may only ever be APPENDED to.")]
        public Color[] statusTints =
        {
            new Color(1.00f, 0.52f, 0.16f, 1f),  // Burn
            new Color(0.55f, 0.90f, 0.30f, 1f),  // Poison
            new Color(1.00f, 0.90f, 0.35f, 1f),  // Stun
            new Color(0.60f, 0.88f, 1.00f, 1f),  // Freeze
            new Color(0.70f, 0.75f, 0.90f, 1f),  // Slow
            new Color(0.72f, 0.55f, 0.32f, 1f),  // Root
            new Color(1.00f, 0.40f, 0.65f, 1f),  // Vulnerable
            new Color(0.72f, 0.45f, 0.95f, 1f),  // Marked
        };

        // -- Hand-painted art -------------------------------------------------

        [Header("Skin - hand-painted art, one slot per piece")]
        [Tooltip("Leave every slot empty to draw the procedural art WorldBarArt generates. Fill " +
                 "one and that piece alone switches over, so a sheet can be painted and judged a " +
                 "piece at a time. Valkur > UI > Import World Bar Skin fills these from a sheet.")]
        public WorldBarSkin skin = new WorldBarSkin();

        // -- Feel ------------------------------------------------------------

        [Header("Feel - the blow")]
        [Tooltip("How long the chip holds its old width before it starts to drain. This pause is " +
                 "the whole readability of the effect: without it the chip is a blur.")]
        [Range(0f, 1f)] public float chipHoldSeconds = 0.22f;

        [Tooltip("How long the chip then takes to catch up with the fill.")]
        [Range(0.05f, 2f)] public float chipDrainSeconds = 0.35f;

        [Tooltip("Peak sideways displacement of the whole rig when its owner is hit, in TEXELS. " +
                 "One texel is one screen pixel at the snapped camera, so this is small on purpose.")]
        [Range(0f, 4f)] public float hitShakeTexels = 1.5f;

        [Range(0f, 1f)] public float hitShakeSeconds = 0.18f;

        [Tooltip("How long the plate flashes on a blow.")]
        [Range(0f, 0.6f)] public float hitFlashSeconds = 0.09f;

        [Header("Feel - the fill")]
        [Tooltip("How fast the fill chases its target. The drawn width is still quantised to a " +
                 "whole pixel, so this is smooth without being blurry.")]
        [Range(1f, 40f)] public float fillLerpSpeed = 12f;

        [Tooltip("Seconds of overshoot after a heal.")]
        [Range(0f, 1f)] public float healPulseSeconds = 0.3f;

        [Header("Feel - low health")]
        [Tooltip("Beats per second of the frame's pulse at zero health. Scaled down toward the " +
                 "low threshold, so the rhythm itself reports how bad it is.")]
        [Range(0f, 4f)] public float heartbeatHz = 2.2f;

        [Range(0f, 1f)] public float heartbeatDepth = 0.55f;

        [Header("Feel - the dash")]
        [Tooltip("How long the pip flashes when the charge comes back. Before this the one " +
                 "instant the dash readout exists for produced no pixel at all.")]
        [Range(0f, 1f)] public float dashReadyFlashSeconds = 0.25f;

        [Header("Feel - visibility")]
        [Tooltip("Seconds of nothing happening before the player's own bars fade out. They are " +
                 "duplicated by the corner HUD, so a permanent copy over the head is a quarter of " +
                 "the character's silhouette spent on news the player already has.")]
        [Range(0f, 30f)] public float idleFadeDelay = 4f;

        [Range(0.05f, 3f)] public float fadeSeconds = 0.45f;

        [Tooltip("Monsters hide their bar at full health. Turning this off shows every creature's " +
                 "bar at all times, which is what a debug session usually wants.")]
        public bool hostilesHideAtFullHealth = true;

        [Tooltip("The player's own bars fade out too once nothing has happened for the idle " +
                 "delay. Off pins them on screen forever, which is what the game shipped with - " +
                 "three permanent bars over the head repeating what the corner HUD already says.")]
        public bool playerBarsHideWhenIdle = true;

        // -- Derived ---------------------------------------------------------

        /// <summary>Health row height in world units, on the texel grid by construction.</summary>
        public float HealthRowHeight => WorldBarGeometry.Texels(healthRowTexels);

        /// <summary>Resource row height in world units.</summary>
        public float ResourceRowHeight => WorldBarGeometry.Texels(resourceRowTexels);

        /// <summary>The frame colour for a rank, without a switch at every call site.</summary>
        public Color FrameFor(WorldBarRank rank)
        {
            switch (rank)
            {
                case WorldBarRank.Ally:   return frameAlly;
                case WorldBarRank.Elite:  return frameElite;
                case WorldBarRank.Boss:   return frameBoss;
                case WorldBarRank.Player: return framePlayer;
                default:                  return frameNormal;
            }
        }

        /// <summary>The health colour for a rank.</summary>
        public Color HealthFor(WorldBarRank rank)
        {
            switch (rank)
            {
                case WorldBarRank.Player: return healthPlayer;
                case WorldBarRank.Ally:   return healthAlly;
                default:                  return healthHostile;
            }
        }

        /// <summary>
        /// Tint for a status kind. Falls back to white rather than throwing, so appending a
        /// value to <c>StatusEffectKind</c> costs a colourless icon and not an exception in
        /// the middle of a fight.
        /// </summary>
        public Color StatusTint(int kindIndex)
        {
            if (statusTints == null || kindIndex < 0 || kindIndex >= statusTints.Length)
                return Color.white;
            return statusTints[kindIndex];
        }

        // -- Resolution ------------------------------------------------------

        private static WorldBarStyle s_cached;
        private static bool s_looked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetWorldBarStyleStatics()
        {
            s_cached = null;
            s_looked = false;
        }

        /// <summary>
        /// The shipped style, or a throwaway instance carrying the defaults. Never null, so no
        /// caller branches - and the defaults are a complete, shippable look on their own.
        /// </summary>
        public static WorldBarStyle Active
        {
            get
            {
                if (!s_looked)
                {
                    s_cached = Resources.Load<WorldBarStyle>(ResourcePath);
                    s_looked = true;
                }
                if (s_cached == null)
                {
                    s_cached = CreateInstance<WorldBarStyle>();
                    // HideAndDontSave for the reason DeathTuning records: this instance is never
                    // an asset, and without it an EditMode run that touches a bar leaves a
                    // ScriptableObject Unity reports as leaked on an unrelated fixture.
                    s_cached.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_cached;
            }
        }

        /// <summary>Drop the cache so the next read re-resolves. For editors that write the asset.</summary>
        public static void InvalidateCache()
        {
            s_cached = null;
            s_looked = false;
        }
    }
}
