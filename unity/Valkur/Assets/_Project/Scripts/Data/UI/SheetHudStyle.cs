using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// The geometry and timings the CHARACTER and RECORDS tabs share. Colours come from
    /// <see cref="HudTheme"/> and the pixel grid from <see cref="PlayerHudStyle"/>, so the two
    /// land on the same grid as the talents board beside them.
    ///
    /// <para><b>One asset for two panels</b>, unlike the talents board's own. They are two views
    /// of the same window — same rect, same stone, same header, same footer — and the only thing
    /// that differs is what goes inside. Two assets would be two places to change a padding and
    /// one of them would be forgotten.</para>
    ///
    /// <para>Under <c>Resources/UI/</c> because both panels are created on bare GameObjects by
    /// <c>CharacterSheetController</c> and have no inspector slot — the
    /// <c>ChatSystem._catalog</c> defect.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "SheetHudStyle", menuName = "Valkur/UI/Sheet HUD Style", order = 44)]
    public class SheetHudStyle : ScriptableObject
    {
        public const string ResourcePath = "UI/SheetHudStyle";

        [Header("Window (texels)")]
        [Range(2, 12)] public int paddingTexels = 6;
        [Range(9, 20)] public int titleBarTexels = 14;
        [Range(8, 20)] public int footerTexels = 12;

        [Tooltip("Total window size. Both tabs are the same rect as the talents board so the " +
                 "strip above them never appears to move when the player switches.")]
        [Range(320, 560)] public int widthTexels = 454;
        [Range(180, 340)] public int heightTexels = 272;

        [Header("CHARACTER")]
        [Tooltip("The left column: portrait, level, experience and the two currencies.")]
        [Range(90, 190)] public int identityWidthTexels = 130;

        [Tooltip("Height of one stat row.")]
        [Range(10, 22)] public int statRowTexels = 14;

        [Tooltip("Height of the stacked breakdown bar inside a stat row.")]
        [Range(2, 6)] public int breakdownBarTexels = 3;

        [Tooltip("Width of the value column at the right of a stat row.")]
        [Range(28, 70)] public int valueColumnTexels = 46;

        [Header("RECORDS")]
        [Tooltip("Height of one lifetime card.")]
        [Range(20, 48)] public int cardTexels = 32;

        [Tooltip("Height of one row in the kill board and the run history.")]
        [Range(8, 16)] public int listRowTexels = 10;

        [Tooltip("How many recent runs the history shows before it says how many more there are.")]
        [Range(4, 20)] public int recentRunsShown = 10;

        [Tooltip("How many entries the kill board shows.")]
        [Range(3, 10)] public int topKillsShown = 5;

        [Header("Motion (seconds)")]
        [Range(0.04f, 0.3f)] public float fadeSeconds = 0.12f;

        [Tooltip("How long a stat row stays knocked one texel aside after its value changed.")]
        [Range(0.05f, 0.5f)] public float statKnockSeconds = 0.22f;

        [Header("Motes (R8: events only)")]
        [Range(8, 64)] public int moteCapacity = 32;
        [Range(2, 12)] public int motesPerStatChange = 6;
        [Range(0.2f, 0.6f)] public float moteLifeSeconds = 0.4f;

        [Header("Backdrop")]
        [Range(0f, 0.85f)] public float veilAlpha = 0.55f;

        private static SheetHudStyle s_cached;
        private static bool s_looked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_cached = null;
            s_looked = false;
        }

        public static SheetHudStyle Active
        {
            get
            {
                if (s_cached != null) return s_cached;
                if (!s_looked)
                {
                    s_looked = true;
                    s_cached = Resources.Load<SheetHudStyle>(ResourcePath);
                }
                if (s_cached == null)
                {
                    s_cached = CreateInstance<SheetHudStyle>();
                    s_cached.name = "SheetHudStyle (defaults)";
                    s_cached.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_cached;
            }
        }
    }
}
