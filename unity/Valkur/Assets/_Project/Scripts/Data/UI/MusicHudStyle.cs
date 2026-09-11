using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Every judgement the music panel makes that is its OWN: its size in texels, where it docks,
    /// how long things take, its motes and the colour of its resonance. The shared tokens —
    /// stone, outline, recess, gold, text — are read from <see cref="PlayerHudStyle"/> (the HUD's
    /// theme until a shared one exists), so the panel cannot drift into a third gold.
    ///
    /// <para><b>Why this exists.</b> The panel it replaces carried 62 colour literals across six
    /// files, a gold two hundredths away from the theme's, and no way to tune a single one of
    /// them without a recompile.</para>
    ///
    /// <para>Under <c>Resources/UI/</c> for the reason every HUD style is: the panel is
    /// <c>AddComponent</c>-ed by <c>HUDBootstrap</c> and has no inspector slot. The tray icon is
    /// referenced HERE for the same reason — the old panel loaded it with
    /// <c>AssetDatabase</c>, which does not exist in a build, so the bar showed a grey square
    /// and the player could not find the music.</para>
    ///
    /// <para><b>Geometry is in TEXELS</b>, drawn at a whole number of screen pixels per texel by
    /// <see cref="PlayerHudStyle.HudPixelScaleFor"/>, like every panel of the HUD.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "MusicHudStyle", menuName = "Valkur/UI/Music HUD Style", order = 42)]
    public class MusicHudStyle : ScriptableObject
    {
        /// <summary>Path passed to <c>Resources.Load</c>.</summary>
        public const string ResourcePath = "UI/MusicHudStyle";

        [Header("Tray")]
        [Tooltip("The icon the HUD tray shows to open and close the panel.")]
        public Sprite trayIcon;

        [Header("Layout (texels)")]
        [Tooltip("Panel width. The title, the zone line, the groove and the transport all derive " +
                 "from it. 126 texels is 252 px at the reference scale: exactly the HUD tray under " +
                 "the panel, so the two read as one column.")]
        [Range(112, 180)] public int widthTexels = 126;
        [Tooltip("Height of the compact plaque.")]
        [Range(38, 60)] public int plaqueTexels = 42;
        [Tooltip("Extra height the resonance adds on top of the plaque when it is open.")]
        [Range(28, 60)] public int resonanceTexels = 40;
        [Tooltip("Bars in the resonance's spectrum. Each is 3 texels wide with a 1-texel gap.")]
        [Range(8, 40)] public int bandCount = 28;
        [Tooltip("Blocks per bar. Each is 1 texel tall with a 1-texel gap.")]
        [Range(4, 14)] public int bandBlocks = 10;

        [Header("Dock (HUD canvas units, from the bottom-right corner)")]
        [Tooltip("Default gap to the right edge of the screen, before the player drags the panel.")]
        public float dockRight = 16f;
        [Tooltip("Default gap to the bottom. 104 clears the HUD tray (16 inset + 80 buttons + 8).")]
        public float dockBottom = 104f;

        [Header("Timing")]
        [Range(0.02f, 0.6f)] public float fadeSeconds = 0.12f;
        [Tooltip("How long the title's shine takes to cross it when a new track starts.")]
        [Range(0.1f, 1f)] public float shineSeconds = 0.32f;
        [Tooltip("How long the medallion's rim stays lit after a new track starts.")]
        [Range(0.1f, 2f)] public float medallionFlashSeconds = 0.8f;
        [Tooltip("Analysis rate of the resonance. The bars are smoothed between frames.")]
        [Range(10f, 60f)] public float spectrumHz = 30f;

        [Header("Yielding to the fight")]
        [Tooltip("Alpha the panel drops to while the player is being hit: the song does not " +
                 "compete with the health bar for attention.")]
        [Range(0.2f, 1f)] public float combatAlpha = 0.55f;
        [Tooltip("Seconds without a blow before the panel comes back to full alpha.")]
        [Range(0.5f, 10f)] public float combatHoldSeconds = 3f;

        [Header("Motes (events only)")]
        [Range(0, 64)] public int moteCapacity = 24;
        [Range(0, 12)] public int notesOnTrack = 6;
        [Range(0, 8)] public int motesOnSkip = 3;
        [Range(0, 8)] public int motesOnSeek = 4;
        [Range(0, 8)] public int notesOnUnmute = 2;
        [Range(0.2f, 1f)] public float moteLifeSeconds = 0.6f;

        [Header("Resonance colour")]
        [Tooltip("A copper ramp: warm, not the theme's gold (gold is importance), not blue " +
                 "(blue is mana), not green or red (health).")]
        public Color bandShadow = new Color(0.42f, 0.24f, 0.13f, 1f);
        public Color bandBody = new Color(0.79f, 0.50f, 0.25f, 1f);
        public Color bandLight = new Color(0.96f, 0.79f, 0.54f, 1f);
        [Tooltip("The slow-falling peak marker over each bar.")]
        public Color bandPeak = new Color(0.96f, 0.79f, 0.54f, 0.45f);
        [Tooltip("Envelope columns of the part of the song still to come.")]
        public Color envelopeAhead = new Color(0.30f, 0.26f, 0.34f, 1f);
        [Tooltip("Envelope columns of the part already played.")]
        public Color envelopePlayed = new Color(0.62f, 0.42f, 0.24f, 1f);
        [Tooltip("The volume notches that are on.")]
        public Color notchOn = new Color(0.80f, 0.81f, 0.86f, 1f);

        // -- Derived ---------------------------------------------------------

        /// <summary>Panel height in texels, with or without the resonance.</summary>
        public int HeightTexels(bool resonance) => plaqueTexels + (resonance ? resonanceTexels : 0);

        // -- Resolution ------------------------------------------------------

        private static MusicHudStyle s_cached;
        private static bool s_looked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetMusicHudStyleStatics()
        {
            s_cached = null;
            s_looked = false;
        }

        /// <summary>The shipped asset, or a defaults-only instance when it is missing.</summary>
        public static MusicHudStyle Active
        {
            get
            {
                if (s_cached != null) return s_cached;
                if (!s_looked)
                {
                    s_looked = true;
                    s_cached = Resources.Load<MusicHudStyle>(ResourcePath);
                }
                if (s_cached == null)
                {
                    s_cached = CreateInstance<MusicHudStyle>();
                    s_cached.name = "MusicHudStyle (defaults)";
                    s_cached.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_cached;
            }
        }
    }
}
