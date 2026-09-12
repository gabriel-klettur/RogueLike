using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Every judgement the ten pre-game screens make, in one asset.
    ///
    /// <para><b>Why this exists.</b> The menus carried 67 raw <c>new Color(</c> literals across
    /// fifteen files and zero references to any theme — four palettes living side by side (the
    /// panels' near-black blue, the class cards' grey, the audio slider's cyan and the loading
    /// bar's pure green), five different row heights and five different panel widths for five
    /// screens of the same menu. Nothing could be tuned without a recompile and nothing agreed
    /// with anything else.</para>
    ///
    /// <para><b>Under <c>Resources/UI/</c></b> for the reason every other tuning asset in this
    /// project is: <c>MainMenuUI</c> is created by <c>AddComponent</c> from its own
    /// <c>AutoBootstrap</c> and has no inspector slot to be wired from — the
    /// <c>ChatSystem._catalog</c> defect. It also carries the menu's shader, which is what pulls
    /// <c>Valkur/UI/HudFx</c> into a player build: a shader found only by <c>Shader.Find</c> is
    /// stripped.</para>
    ///
    /// <para><b>It does not re-declare the game's palette.</b> The neutral, gold and text colours
    /// are read from <see cref="HudTheme"/> when <see cref="followHudTheme"/> is on, so the menu
    /// and the HUD are the same game. What lives here is what only the menu has: the title's
    /// particles, the carousel's pacing, the scrim, and the sizes of screens the HUD has no
    /// equivalent of.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "MenuStyle", menuName = "Valkur/UI/Menu Style", order = 48)]
    public class MenuStyle : ScriptableObject
    {
        /// <summary>Path passed to <c>Resources.Load</c>.</summary>
        public const string ResourcePath = "UI/MenuStyle";

        [Header("Rendering")]
        [Tooltip("Valkur/UI/HudFx. Referenced here so it ships in a build; without it the " +
                 "title's particles and the menu motes stop being additive and paint over the " +
                 "art instead of lighting it.")]
        public Shader hudFxShader;

        [Tooltip("The canvas the whole menu is authored against. Matches HudLayout so the menu " +
                 "and the HUD scale identically.")]
        public Vector2 referenceResolution = new Vector2(1600f, 800f);

        // ── Palette ──────────────────────────────────────────────────────────

        [Header("Palette")]
        [Tooltip("Take neutral / gold / text from HudTheme so the menu is the same game as the " +
                 "HUD. Turn off only to try a palette; the shipped answer is on.")]
        public bool followHudTheme = true;

        [Tooltip("Used when followHudTheme is off, and as the fallback if the theme is missing.")]
        public Color panel = new Color(0.055f, 0.058f, 0.075f, 0.965f);

        public Color panelEdge = new Color(0.015f, 0.015f, 0.025f, 1f);
        public Color panelBevel = new Color(0.16f, 0.16f, 0.21f, 1f);
        public Color header = new Color(0.035f, 0.035f, 0.048f, 1f);
        public Color gold = new Color(0.91f, 0.76f, 0.36f, 1f);
        public Color goldDim = new Color(0.55f, 0.44f, 0.19f, 1f);
        public Color textPrimary = new Color(0.93f, 0.93f, 0.96f, 1f);
        public Color textDim = new Color(0.60f, 0.62f, 0.69f, 1f);
        public Color textMuted = new Color(0.40f, 0.42f, 0.48f, 1f);

        [Tooltip("Text ON the selected row. Deliberately NOT the gold: the shipped menu wrote " +
                 "gold on a gold pill and measured 4.15:1, WORSE than the 9.61:1 of a row that " +
                 "was not selected — the row the player is looking at was the hardest to read.")]
        public Color textOnSelection = new Color(0.055f, 0.045f, 0.02f, 1f);

        public Color danger = new Color(0.90f, 0.30f, 0.28f, 1f);
        public Color success = new Color(0.38f, 0.80f, 0.42f, 1f);

        // ── Layout ───────────────────────────────────────────────────────────

        [Header("Layout")]
        [Range(220f, 520f)] public float menuPanelWidth = 328f;
        [Range(360f, 900f)] public float panelWidth = 560f;
        [Range(520f, 1200f)] public float widePanelWidth = 820f;

        [Tooltip("ONE row height for every list in the menu. The shipped screens used 42, 52, " +
                 "40, 44, 37 and 31 px for the same kind of row.")]
        [Range(28f, 72f)] public float rowHeight = 46f;

        [Range(0f, 16f)] public float rowGap = 4f;
        [Range(8f, 40f)] public float panelPadding = 18f;
        [Range(28f, 88f)] public float titleBarHeight = 54f;
        [Range(20f, 72f)] public float hintBarHeight = 34f;
        [Range(2f, 8f)] public float accentBarWidth = 4f;

        [Tooltip("Distance from the top of the canvas to the top of every sub-panel. ONE anchor: " +
                 "the shipped Controls panel centred itself instead and covered the title.")]
        [Range(160f, 520f)] public float panelTopOffset = 296f;

        // ── Type ─────────────────────────────────────────────────────────────

        [Header("Type")]
        [Tooltip("Resources path of the TMP font asset every menu label uses, e.g. " +
                 "\"UI/Fonts/Valkur_Title SDF\". Empty keeps the TMP default, which is what " +
                 "all 113 of the shipped labels used.")]
        public string menuFontResource = string.Empty;

        // The font is a RESOURCE PATH and not a TMP_FontAsset on purpose. Valkur.Data sees only
        // Valkur.Core — no TextMeshPro, no UI — and that is what lets an EditMode fixture load a
        // catalogue without dragging the UI stack in behind it. Typing this field would have made
        // the data layer depend on the drawing layer for one reference. It is the same call
        // LoadoutStateSheets.state makes (a string, because AnimState lives in Gameplay) and the
        // same one SpellDefinition.previewAnimState makes. MenuTypography, in Valkur.UI, is the
        // one place that resolves it.

        [Range(14f, 40f)] public float rowFontSize = 22f;
        [Range(16f, 48f)] public float titleFontSize = 28f;
        [Range(10f, 24f)] public float hintFontSize = 15f;
        [Range(9f, 20f)] public float detailFontSize = 14f;

        // ── The particle title ───────────────────────────────────────────────

        [Header("Title (particles)")]
        [Tooltip("Height of a capital, in canvas units. The whole title is laid out from this.")]
        [Range(60f, 300f)] public float titleCapHeight = 132f;

        [Tooltip("How thick a stroke of the title is. Above ~0.16 of the cap height the counters " +
                 "of A, R and U close up and the word stops being readable.")]
        // 21, not the 17 this was tuned at: the sampler used to draw a bar WIDER than its
        // authored width (at five rows, 1.225x), so 17 reached the screen as 20.8. The
        // sampler is exact now, and this number carries the weight that was tuned by eye.
        [Range(4f, 40f)] public float titleStrokeWidth = 21f;

        [Tooltip("Distance between two points along a stroke. Smaller is denser AND more " +
                 "expensive: the whole title is one mesh rebuilt every frame it moves.")]
        [Range(2f, 14f)] public float titlePointSpacing = 4.0f;

        [Tooltip("Points side by side across a stroke. Two is a pair of rails; five reads solid.")]
        [Range(1, 8)] public int titleRowsAcross = 5;

        [Tooltip("Hard ceiling on the title's points. Reached by thinning the cloud evenly, never " +
                 "by truncating it — a truncated cloud draws the first letters and drops the last.")]
        [Range(200, 6000)] public int titleMaxPoints = 3600;

        [Tooltip("Gap between glyphs, in em units.")]
        [Range(0f, 0.4f)] public float titleTracking = 0.17f;

        [Tooltip("How wide one mote of the title is drawn, in canvas units. THIS is what decides " +
                 "whether the word reads as matter or as a constellation, and it is not the " +
                 "point count: the shipped mote was the atlas dot at its own 2x2 texels against a " +
                 "sampling grid whose pitch is about 4, so three quarters of every stroke was " +
                 "empty. A mote a little wider than the pitch overlaps its neighbours and the " +
                 "stroke closes.")]
        [Range(1f, 12f)] public float titleMoteSize = 3.9f;

        [Tooltip("The few per cent of motes drawn as sparks are drawn this much larger. They are " +
                 "the glints inside the letter, so they have to clear the body or they are lost " +
                 "in it.")]
        [Range(1f, 3f)] public float titleSparkScale = 1.45f;

        [Tooltip("How much thinner a HORIZONTAL stroke is than a vertical one. This is the stress " +
                 "every typeface with a personality has, and the shipped title had none: all 45 " +
                 "glyphs drew every stroke at one width, which is exactly what makes a " +
                 "hand-built alphabet read as a default one. 0 restores that.")]
        [Range(0f, 1f)] public float titleWeightContrast = 0.62f;

        [Tooltip("How much a stroke swells at its two ends, as a fraction of its width. A cut " +
                 "that is wider where the chisel entered and left reads as CARVED rather than as " +
                 "drawn — the cheapest character an alphabet like this can carry.")]
        [Range(0f, 0.8f)] public float titleTerminalFlare = 0.34f;

        [Tooltip("Distance from the top of the canvas to the title's cap line.")]
        [Range(20f, 260f)] public float titleTopOffset = 92f;

        [Range(0.2f, 4f)] public float titleAssembleSeconds = 1.45f;

        [Tooltip("How far a mote starts from where it lands, as a fraction of the screen.")]
        [Range(0.05f, 2f)] public float titleScatterRadius = 0.62f;

        [Tooltip("How much a settled mote drifts around its point. This is the whole of what " +
                 "keeps the word ALIVE without it moving.")]
        [Range(0f, 6f)] public float titleShimmerAmplitude = 1.5f;

        [Range(0.05f, 3f)] public float titleShimmerSpeed = 0.55f;

        [Tooltip("Seconds between two sweeps of light across the word. 0 turns the sweep off.")]
        [Range(0f, 30f)] public float titleSweepInterval = 6.5f;

        [Range(0.2f, 4f)] public float titleSweepSeconds = 1.1f;

        public Color titleCore = new Color(1f, 0.96f, 0.86f, 1f);
        public Color titleMid = new Color(1f, 0.80f, 0.38f, 1f);
        public Color titleEdge = new Color(0.98f, 0.48f, 0.16f, 1f);

        [Tooltip("The colour a mote has while it is still flying in. Cooler than the settled " +
                 "word, so the assembly reads as embers gathering rather than as the title " +
                 "sliding in from off-screen.")]
        public Color titleEmber = new Color(0.55f, 0.68f, 1f, 1f);

        [Tooltip("How many embers rise off the settled word per second. Events, not a field: " +
                 "this is the one loop the menu is allowed, and it is below the attention floor.")]
        [Range(0f, 60f)] public float titleEmberRate = 7f;

        [Tooltip("A soft darkening behind the word, as a fraction of its height. The carousel " +
                 "puts a painted face under the title every few seconds; without it the word is " +
                 "legible on some frames and not on others, which is the same defect the footer " +
                 "had before the bottom ramp existed.")]
        [Range(0f, 1f)] public float titleHaloStrength = 0.74f;

        [Tooltip("How far the halo reaches past the word, as a fraction of its own size.")]
        [Range(0f, 1.5f)] public float titleHaloPadding = 0.42f;

        // ── The title's LOOK ─────────────────────────────────────────────────

        [Header("Title look")]
        [Tooltip("Which entry of titleLooks the word wears. An empty name, or one no entry " +
                 "answers to, falls back to the loose title* fields above — which is what makes " +
                 "this addition unable to change the shipped title by accident.")]
        public string titleLook = "ascua";

        [Tooltip("The named looks. Adding one is a data edit; the renderer never learns that a " +
                 "second look exists, because all of them are the same three-tone ramp sampled " +
                 "at a temperature.")]
        public TitleLook[] titleLooks = new TitleLook[0];

        /// <summary>
        /// The look the word should wear, resolved by name — and NEVER null.
        ///
        /// <para>The fallback is the legacy block above rather than a hard-coded default, so a
        /// style asset authored before looks existed keeps rendering exactly what it always
        /// did: <c>coolAcross = 1</c>, no vertical cooling and no flicker is precisely the
        /// shipped hot-metal title.</para>
        /// </summary>
        public TitleLook ResolveTitleLook()
        {
            if (titleLooks != null && !string.IsNullOrEmpty(titleLook))
            {
                for (int i = 0; i < titleLooks.Length; i++)
                {
                    var look = titleLooks[i];
                    if (look != null && string.Equals(look.name, titleLook,
                                                      System.StringComparison.OrdinalIgnoreCase))
                        return look;
                }
            }
            return LegacyTitleLook();
        }

        /// <summary>The loose <c>title*</c> fields, as a look. The shipped appearance, verbatim.</summary>
        public TitleLook LegacyTitleLook() => new TitleLook
        {
            name = "ascua",
            hot = titleCore,
            warm = titleMid,
            cool = titleEdge,
            coolAcross = 1f,
            coolUpward = 0f,
            flickerAmount = 0f,
            shimmerAmplitude = titleShimmerAmplitude,
            shimmerSpeed = titleShimmerSpeed,
            driftAspect = 0.45f,
            riseBias = 0f,
            glowGain = 1f,
            haloStrength = titleHaloStrength,
            haloPadding = titleHaloPadding,
            haloTint = Color.black,
            ember = titleEmber,
            sweepInterval = titleSweepInterval,
            sweepSeconds = titleSweepSeconds,
        };

        // ── Background ───────────────────────────────────────────────────────

        [Header("Background")]
        [Tooltip("Seconds a carousel image is held. The shipped value was 2.0 — each portrait " +
                 "stood still for 1.4 s, which is a slideshow, not a title screen.")]
        [Range(2f, 20f)] public float carouselHold = 7f;

        [Range(0.2f, 4f)] public float carouselFade = 1.3f;

        [Tooltip("Slow push on the background while it is held (1.04 = 4 % over the whole hold). " +
                 "0 turns the Ken Burns move off.")]
        [Range(1f, 1.2f)] public float carouselZoom = 1.045f;

        [Tooltip("How far down the art is biased, as a fraction of the crop. The art is 3:2 and " +
                 "the window is 2:1, so a third of the height is cropped: centred, it takes the " +
                 "characters' heads off.")]
        [Range(-0.5f, 0.5f)] public float carouselVerticalBias = -0.16f;

        [Tooltip("The one veil over the art. The shipped menu stacked a second 55 % black under " +
                 "every sub-screen, so an open panel sat on an 80 % dark painting.")]
        [Range(0f, 1f)] public float scrimBase = 0.34f;

        [Tooltip("What the veil deepens to while a sub-screen is open.")]
        [Range(0f, 1f)] public float scrimPanel = 0.62f;

        [Range(0f, 1f)] public float vignetteStrength = 0.55f;

        // ── Motion ───────────────────────────────────────────────────────────

        [Header("Motion")]
        [Range(0.04f, 0.6f)] public float panelFadeSeconds = 0.16f;

        [Tooltip("The panel opens from this scale. 1 turns the open animation off.")]
        [Range(0.8f, 1f)] public float panelOpenScale = 0.965f;

        [Tooltip("How long the selection bar takes to slide to the row that was picked. The " +
                 "shipped menu had no transition at all: the highlight teleported.")]
        [Range(0.02f, 0.4f)] public float selectionSlideSeconds = 0.09f;

        [Range(0f, 120f)] public int moteCapacity = 96;

        // ── Audio ────────────────────────────────────────────────────────────

        [Header("Audio")]
        [Tooltip("Catalogue ids are tried first and synthesised when absent — the AudioCatalog " +
                 "holds no ui_* id today, and PlaySfxById warns once per missing id by design.")]
        public string sfxMove = "ui_move";

        public string sfxConfirm = "ui_confirm";
        public string sfxCancel = "ui_cancel";
        public string sfxRefuse = "ui_refuse";
        public string sfxStart = "ui_start";
        [Range(0f, 1f)] public float sfxVolume = 0.5f;

        // ── Accessibility ────────────────────────────────────────────────────

        [Header("Accessibility")]
        [Tooltip("Honoured by the title, the motes, the carousel push and the panel animations. " +
                 "The setting the player sets lives in GameSettings; this is the default.")]
        public bool reduceMotionDefault = false;

        // ── Access ───────────────────────────────────────────────────────────

        private static MenuStyle s_cached;
        private static bool s_looked;

        /// <summary>
        /// Static mutable state with Domain Reload off. Direct assignments, because
        /// <c>DomainReloadStaticResetTests</c> reads this method's raw IL.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_cached = null;
            s_looked = false;
        }

        /// <summary>
        /// The shipped style, or a default instance when the asset is missing. Never null — a
        /// menu that cannot find its style must still draw, and a NullReference at the title
        /// screen is the worst place in the game to have one.
        /// </summary>
        public static MenuStyle Active
        {
            get
            {
                if (s_cached != null) return s_cached;
                if (!s_looked)
                {
                    s_looked = true;
                    s_cached = Resources.Load<MenuStyle>(ResourcePath);
                }
                if (s_cached == null)
                {
                    s_cached = CreateInstance<MenuStyle>();
                    s_cached.name = "MenuStyle (fallback)";
                    s_cached.hideFlags = HideFlags.DontSave;
                }
                return s_cached;
            }
        }

        /// <summary>Lets a test install a style without an asset on disk.</summary>
        public static void OverrideActiveForTests(MenuStyle style)
        {
            s_cached = style;
            s_looked = true;
        }

        // ── Resolved palette ─────────────────────────────────────────────────
        //
        // Read through these, never through the fields, so followHudTheme actually decides
        // something. A caller that reads `style.gold` directly is the shape that made the
        // shipped menu disagree with the HUD about what gold is.

        private HudTheme Theme => followHudTheme ? HudTheme.Active : null;

        public Color Panel => Theme != null ? WithAlpha(Theme.stoneDark, panel.a) : panel;
        public Color PanelEdge => Theme != null ? Theme.outline : panelEdge;
        public Color PanelBevel => Theme != null ? Theme.stoneLight : panelBevel;
        public Color Gold => Theme != null ? Theme.gold : gold;
        public Color GoldDim => Theme != null ? Theme.goldShade : goldDim;
        public Color TextPrimary => Theme != null ? Theme.text : textPrimary;
        public Color TextDim => Theme != null ? Theme.textDim : textDim;
        public Color TextMuted => Theme != null ? Theme.textDisabled : textMuted;
        public Color Danger => Theme != null ? Theme.danger : danger;
        public Color Success => Theme != null ? Theme.success : success;

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
