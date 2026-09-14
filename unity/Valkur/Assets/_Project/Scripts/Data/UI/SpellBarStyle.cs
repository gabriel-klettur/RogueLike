using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Every decision the bottom-centre action bar makes that is its OWN: geometry, the two
    /// postures' accents, the verbs' tints, timings and mote budgets.
    ///
    /// <para><b>What it deliberately does NOT carry:</b> stone, outline, recess, gold, text and
    /// cooldown colours. Those are the player panel's and are read from <see cref="PlayerHudStyle"/>,
    /// because the bar is drawn with the panel's kit, in the panel's texel space, beside the panel
    /// — the audit that rebuilt it found the old bar dressed in the tile editor's theme precisely
    /// because it had a palette of its own to drift in.</para>
    ///
    /// <para>Under <c>Resources/UI/</c> for the reason every HUD style there is: the bar is built
    /// at runtime by <c>HUDManager</c> and has no inspector slot. It also carries the tray icon,
    /// which is what makes it ship in a player build — the old bar loaded it through
    /// <c>AssetDatabase</c>, which answers null outside the Editor.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "SpellBarStyle", menuName = "Valkur/UI/Spell Bar Style", order = 42)]
    public class SpellBarStyle : ScriptableObject
    {
        /// <summary>Path passed to <c>Resources.Load</c>.</summary>
        public const string ResourcePath = "UI/SpellBarStyle";

        [Header("Geometry (texels)")]
        [Tooltip("Slot edge. Rounded DOWN to an even number, so a slot's centre is a whole texel " +
                 "and the flip, which scales about that centre, never lands between texels.")]
        [Range(16, 32)] public int slotTexels = 22;
        [Range(1, 6)] public int slotGapTexels = 2;
        [Tooltip("Extra space between two groups: the hand reads 1-5 and 6-0 as two blocks.")]
        [Range(0, 12)] public int groupGapTexels = 5;
        [Tooltip("Spells per group on the War face.")]
        [Range(2, 8)] public int warGroupSize = 5;
        [Tooltip("Slots per row before the bar wraps into a second row above the first.")]
        [Range(4, 16)] public int maxSlotsPerRow = 10;
        [Tooltip("Stone around the slots. Six clears the panel stone's corner rivets (four and " +
                 "five texels in); less and a gold rivet peeks out from under the corner slots.")]
        [Range(6, 12)] public int paddingTexels = 6;
        [Tooltip("Minimum gap to the player panel on the left, when the bar is too wide to centre.")]
        [Range(0, 20)] public int clearanceTexels = 6;

        [Header("Postures")]
        [Tooltip("The War face's accent: the frame's gem, the posture slot's glow, the flip's motes.")]
        public Color warAccent = new Color(0.95f, 0.42f, 0.32f, 1f);
        [Tooltip("The Peace face's accent.")]
        public Color peaceAccent = new Color(0.45f, 0.85f, 0.55f, 1f);

        [Header("Peace verbs (glyph tints)")]
        public Color interactTint = new Color(0.94f, 0.86f, 0.59f, 1f);
        public Color inventoryTint = new Color(0.86f, 0.63f, 0.39f, 1f);
        public Color mapTint = new Color(0.92f, 0.84f, 0.63f, 1f);
        public Color craftingTint = new Color(0.78f, 0.80f, 0.86f, 1f);
        public Color questsTint = new Color(1f, 0.84f, 0.35f, 1f);
        public Color talentsTint = new Color(0.96f, 0.71f, 0.35f, 1f);
        public Color grimoireTint = new Color(0.73f, 0.55f, 1f, 1f);
        [Tooltip("How much of its tint a verb keeps while there is nothing to act on.")]
        [Range(0f, 1f)] public float idleVerbStrength = 0.38f;

        [Header("Feel")]
        [Tooltip("How long each half of the posture flip takes, per slot.")]
        [Range(0.04f, 0.6f)] public float flipHalfSeconds = 0.13f;
        [Tooltip("Delay between one slot flipping and the next, left to right.")]
        [Range(0f, 0.1f)] public float flipStaggerSeconds = 0.022f;

        [Tooltip("Half of the quick turn to the Shift page and back, in seconds. Much shorter than " +
                 "the posture flip: it answers a key held under the player's fingers, and a page " +
                 "that lagged behind Shift would read as the bar being slow.")]
        [Range(0.02f, 0.3f)] public float pageHalfSeconds = 0.055f;

        [Tooltip("Delay between neighbouring slots in the page turn.")]
        [Range(0f, 0.05f)] public float pageStaggerSeconds = 0.006f;
        [Tooltip("How long the frame's gem glows after a spell leaves the hands.")]
        [Range(0f, 1f)] public float gemPulseSeconds = 0.35f;
        [Range(0f, 1f)] public float showFadeSeconds = 0.12f;

        [Header("Motes (event particles)")]
        [Range(0, 128)] public int moteCapacity = 64;
        [Range(0, 16)] public int motesOnCast = 4;
        [Range(0, 24)] public int motesOnReady = 6;
        [Range(0, 48)] public int motesOnFlip = 22;
        [Range(0, 48)] public int motesOnLearn = 20;
        [Range(0, 16)] public int motesOnVerb = 5;

        [Header("Tray")]
        [Tooltip("The HUD tray button that shows and hides the bar. A reference, so it ships.")]
        public Sprite trayIcon;

        /// <summary>The slot edge actually used: even, and never below 16.</summary>
        public int SlotTexels => Mathf.Max(16, slotTexels & ~1);

        /// <summary>The accent of a posture.</summary>
        public Color AccentFor(Valkur.Core.Stance stance) =>
            stance == Valkur.Core.Stance.Peace ? peaceAccent : warAccent;

        // -- Resolution ------------------------------------------------------

        private static SpellBarStyle s_cached;
        private static bool s_looked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSpellBarStyleStatics()
        {
            s_cached = null;
            s_looked = false;
        }

        /// <summary>The shipped asset, or a defaults-only instance when it is missing.</summary>
        public static SpellBarStyle Active
        {
            get
            {
                if (s_cached != null) return s_cached;
                if (!s_looked)
                {
                    s_looked = true;
                    s_cached = Resources.Load<SpellBarStyle>(ResourcePath);
                }
                if (s_cached == null)
                {
                    s_cached = CreateInstance<SpellBarStyle>();
                    s_cached.name = "SpellBarStyle (defaults)";
                    s_cached.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_cached;
            }
        }
    }
}
