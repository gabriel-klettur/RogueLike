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

        [Tooltip("Total height of the energy row (the Carrera skill's stamina), frame included. " +
                 "Shares the resource row's generated art (WorldBarRow.Resource) rather than its " +
                 "own sheet entries - the two rows are the same shape at a different height.")]
        [Range(3, 8)] public int energyRowTexels = 4;

        [Tooltip("Texels between the health row and the resource row. MINUS ONE is the shipped " +
                 "value and means the two rows SHARE one outline row, so the stack reads as one " +
                 "instrument. Measured with a one-texel gap instead, the gap showed the character's " +
                 "own pixels through a slot in the middle of the readout, which read as dirt.")]
        [Range(-1, 4)] public int rowGapTexels = -1;

        [Tooltip("Blank texels between the top of the bars (the dash pip included) and the row " +
                 "of status icons above them.")]
        [Range(0, 4)] public int statusGapTexels = 1;

        [Tooltip("Blank texels between the top of the body sprite and the first row.")]
        [Range(0, 8)] public int headMarginTexels = 1;

        [Tooltip("Side of the square dash pip that sits at the right end of the resource row.")]
        [Range(3, 8)] public int pipTexels = 6;

        [Tooltip("Side of a status icon cell, its one-texel dark outline included. NINE: a " +
                 "seven-texel glyph plus the outline. Measured on the colour sheet at 6, 7 and 10: " +
                 "at 6 the snowflake collapses into a blob; at 10 with the sheet's own gold frames " +
                 "the icons were 50 px against a 30 px health bar and read as the primary thing " +
                 "over the character. The glyph alone, cropped out of its frame and given a dark " +
                 "outline, keeps all eight silhouettes at seven texels and weighs a third less.")]
        [Range(4, 12)] public int iconTexels = 9;

        [Tooltip("Blank texels between two status icons. Zero: each glyph carries its own dark " +
                 "outline, which already separates it from its neighbour.")]
        [Range(0, 4)] public int iconGapTexels = 0;

        [Tooltip("How many status icons are drawn before the row collapses into an overflow pip. " +
                 "THREE keeps the row about as wide as the health bar under it; four made it " +
                 "2.2x the character's own width and turned a readout into a second silhouette.")]
        [Range(1, 8)] public int maxIcons = 3;

        [Tooltip("Drain the bottom row of each status tile with its remaining time, in the " +
                 "status's own hue. It lives inside the tile, so it costs no height; measured as a " +
                 "row of its own it was a cream strip the width of the health bar.")]
        public bool iconDurationLines = true;

        [Tooltip("Draw the dark translucent recess behind the fill. It is what the fill's " +
                 "contrast is measured against: with it OFF the empty part of the bar showed the " +
                 "ground, and the hostile red measured 1.09:1 against cobblestone - the same " +
                 "luminance, i.e. readable by hue alone and invisible to a colour-blind player. " +
                 "Translucent rather than opaque, so the world still shows faintly through.")]
        public bool drawPlate = true;

        [Tooltip("Draw quarter marks across the health row. They are what lets the bar be read " +
                 "without relying on its colour, which is half of the colour-blind problem.")]
        public bool showQuarterNotches = false;

        [Tooltip("Narrowest bar that still gets quarter marks - below this they are noise.")]
        [Range(6, 40)] public int notchMinWidthTexels = 12;

        // -- Colour ----------------------------------------------------------

        [Header("Colour - outline, plate and rank")]
        [Tooltip("The one-texel outline round every row. Near black, and the SAME for every " +
                 "rank: it is what separates the bar from any ground behind it, pale cobblestone " +
                 "and dark foliage alike, so it cannot also be the thing that changes with rank. " +
                 "Rank lives in the metal end caps and the halo below.")]
        public Color outline = new Color(0.04f, 0.05f, 0.07f, 1f);

        [Tooltip("The recess the fill sits in: the navy of the painted sheet's own interiors, " +
                 "translucent. Its job is contrast - every fill colour must stand at least 3:1 " +
                 "against it - and WorldBarContrastTests measures that over the real ground. " +
                 "Alpha 0.88: at 0.74 the pixels of whatever stood behind the bar (the " +
                 "character's own helmet, most of the time) showed through the empty part, and " +
                 "the missing health read as texture instead of as a calm recess.")]
        public Color plate = new Color(0.08f, 0.11f, 0.16f, 0.88f);

        [Tooltip("What the outline beats toward while health is low. A ring that pulses warm is " +
                 "read from the corner of the eye; a fill that changes colour is not.")]
        public Color lowPulse = new Color(1f, 0.36f, 0.18f, 1f);

        [Tooltip("Metal of the two end caps on the player's own bar: the brass of the sheet's " +
                 "rivets. Every rank gets its own metal, so rank reads without a legend.")]
        public Color capPlayer = new Color(0.95f, 0.72f, 0.34f, 1f);
        public Color capAlly = new Color(0.42f, 0.84f, 0.68f, 1f);
        public Color capNormal = new Color(0.56f, 0.60f, 0.66f, 1f);
        public Color capElite = new Color(0.90f, 0.94f, 1f, 1f);
        public Color capBoss = new Color(1f, 0.80f, 0.24f, 1f);

        [Tooltip("A one-texel ring OUTSIDE the outline, for the ranks that must be picked out of " +
                 "a crowd. Alpha 0 draws none - an ordinary monster and the player carry no halo, " +
                 "so a halo always means something.")]
        public Color haloAlly = new Color(0.40f, 0.90f, 0.66f, 0.95f);
        public Color haloElite = new Color(0.92f, 0.95f, 1f, 0.95f);
        public Color haloBoss = new Color(1f, 0.78f, 0.20f, 1f);

        [Header("Colour - health")]
        [Tooltip("Each fill colour is ONE decision: WorldBarPalette derives the highlight, " +
                 "shadow and leading edge from it with a hue-shifted ramp.")]
        public Color healthPlayer = new Color(0.31f, 0.84f, 0.40f, 1f);
        public Color healthAlly = new Color(0.26f, 0.80f, 0.70f, 1f);
        public Color healthHostile = new Color(0.90f, 0.24f, 0.22f, 1f);

        [Tooltip("What an ALLY's bar turns into below the low threshold. A warning, not a second " +
                 "identity. Hostile bars do not switch at all: a monster about to die is not " +
                 "news the player has to be warned about, and an amber hostile read as gold " +
                 "beside the elite and boss caps.")]
        public Color healthLow = new Color(1f, 0.70f, 0.18f, 1f);

        [Tooltip("What the player's OWN bar turns into below the low threshold: green to red, the " +
                 "one colour change every player already knows. Amber was tried first and read " +
                 "as the same family as the brass caps and the gold dash stone beside it.")]
        public Color healthLowPlayer = new Color(0.94f, 0.27f, 0.20f, 1f);

        [Tooltip("The delayed chunk left behind by a blow. Bright, so the eye catches the SIZE " +
                 "of the hit rather than only its outcome.")]
        public Color healthChip = new Color(1f, 0.95f, 0.82f, 0.95f);

        [Range(0f, 1f), Tooltip("Fraction of max HP below which the bar switches to the low colour.")]
        public float lowThreshold = 0.3f;

        [Header("Colour - resources")]
        public Color mana = new Color(0.30f, 0.52f, 1f, 1f);

        [Tooltip("The ghost of mana just spent. Same job as the health chip.")]
        public Color manaSpent = new Color(0.78f, 0.88f, 1f, 0.9f);

        [Tooltip("The ORANGE fill of the energy row (Carrera skill). Green was tried first and read as a second health bar in the live capture. Distinct from mana in " +
                 "SHAPE as well as colour - the row carries quarter notches mana does not - so a " +
                 "colour-blind player does not depend on the hue. If the shipped asset predates " +
                 "this field it deserializes to transparent black; EnergyFillColour falls back to " +
                 "a built-in default rather than drawing an invisible row.")]
        public Color energy = new Color(0.96f, 0.58f, 0.18f, 1f);

        [Tooltip("The ghost of energy just drained by running. Same job as manaSpent.")]
        public Color energySpent = new Color(1.00f, 0.84f, 0.58f, 0.9f);

        [Tooltip("The dash pip when a charge is ready: the gold of the sheet's gem. Gold rather " +
                 "than cyan, because cyan beside the blue mana bar is one channel away from it.")]
        public Color dashReady = new Color(0.98f, 0.76f, 0.28f, 1f);
        public Color dashCharging = new Color(0.52f, 0.40f, 0.16f, 1f);

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

        [Header("Particles - inside the fill")]
        [Tooltip("Motes of light drifting along the fill toward its leading edge. They are what " +
                 "turns a flat strip into something that reads as a charge of energy, and they " +
                 "only exist where the fill does, so an emptying bar visibly loses its sparkle.")]
        public bool fillMotes = true;

        [Tooltip("One mote per this many texels of fill.")]
        [Range(3, 20)] public int moteEveryTexels = 6;

        [Tooltip("Drift speed of a mote, in texels per second.")]
        [Range(0f, 20f)] public float moteSpeedTexels = 2.5f;

        [Tooltip("Drift speed while the resource is regenerating. Ties the mana bar to the " +
                 "regeneration aura around the body, which used to be the only sign of it.")]
        [Range(0f, 40f)] public float moteBoostSpeedTexels = 9f;

        [Range(0.1f, 6f)] public float moteTwinkleHz = 1.6f;

        [Header("Particles - events")]
        [Tooltip("Shards thrown off the chunk of health a blow removed.")]
        [Range(0, 16)] public int sparksOnHit = 6;

        [Tooltip("Motes rising out of the fill on a heal.")]
        [Range(0, 16)] public int sparksOnHeal = 5;

        [Tooltip("Sparks where mana was spent.")]
        [Range(0, 16)] public int sparksOnSpend = 3;

        [Tooltip("Sparks thrown out of the dash pip the moment the charge is back.")]
        [Range(0, 16)] public int sparksOnDashReady = 6;

        [Range(0.1f, 2f)] public float sparkLifeSeconds = 0.5f;

        [Tooltip("Launch speed of an event spark, texels per second.")]
        [Range(1f, 60f)] public float sparkSpeedTexels = 16f;

        [Tooltip("Gravity on a shard, texels per second squared. Heal motes ignore it and rise.")]
        [Range(0f, 200f)] public float sparkGravityTexels = 55f;

        [Header("Feel - visibility")]
        [Tooltip("How fast the rig appears. Faster than it fades, on purpose: news should arrive " +
                 "at once and leave slowly.")]
        [Range(0.02f, 1f)] public float fadeInSeconds = 0.12f;

        [Tooltip("The player's bars stay up while mana or the dash charge is still coming back, " +
                 "not only while health is below full - a bar that vanishes with the mana at a " +
                 "fifth leaves the player guessing whether they can cast.")]
        public bool playerShowsWhileRecovering = true;

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

        // Fallbacks for a shipped asset written before the energy fields existed: a genuinely
        // new field takes its C# default on load, but this project has been burned before by
        // assuming a ScriptableObject re-serializes cleanly (see the "asset keeps the defaults
        // it was CREATED with" gotcha), so the alpha-0 sentinel is checked defensively rather
        // than trusted.
        private static readonly Color s_defaultEnergy = new Color(0.96f, 0.58f, 0.18f, 1f);
        private static readonly Color s_defaultEnergySpent = new Color(1.00f, 0.84f, 0.58f, 0.9f);

        /// <summary>Health row height in world units, on the texel grid by construction.</summary>
        public float HealthRowHeight => WorldBarGeometry.Texels(healthRowTexels);

        /// <summary>Resource row height in world units.</summary>
        public float ResourceRowHeight => WorldBarGeometry.Texels(resourceRowTexels);

        /// <summary>Energy row height in world units.</summary>
        public float EnergyRowHeight => WorldBarGeometry.Texels(energyRowTexels);

        /// <summary>The energy fill, falling back to a built-in default when the shipped asset
        /// predates the field (an alpha-0 colour is never a deliberate authoring choice here).</summary>
        public Color EnergyFillColour => energy.a > 0.001f ? energy : s_defaultEnergy;

        /// <summary>The energy chip, same fallback rule.</summary>
        public Color EnergySpentColour => energySpent.a > 0.001f ? energySpent : s_defaultEnergySpent;

        /// <summary>The end-cap metal for a rank, without a switch at every call site.</summary>
        public Color CapFor(WorldBarRank rank)
        {
            switch (rank)
            {
                case WorldBarRank.Ally:   return capAlly;
                case WorldBarRank.Elite:  return capElite;
                case WorldBarRank.Boss:   return capBoss;
                case WorldBarRank.Player: return capPlayer;
                default:                  return capNormal;
            }
        }

        /// <summary>The halo ring for a rank; alpha 0 means the rank carries none.</summary>
        public Color HaloFor(WorldBarRank rank)
        {
            switch (rank)
            {
                case WorldBarRank.Ally:  return haloAlly;
                case WorldBarRank.Elite: return haloElite;
                case WorldBarRank.Boss:  return haloBoss;
                default:                 return Color.clear;
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
        /// What the health fill turns into below the low threshold, per rank. A hostile rank
        /// answers its own fill colour, i.e. no switch.
        /// </summary>
        public Color LowFor(WorldBarRank rank)
        {
            switch (rank)
            {
                case WorldBarRank.Player: return healthLowPlayer;
                case WorldBarRank.Ally:   return healthLow;
                default:                  return HealthFor(rank);
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
