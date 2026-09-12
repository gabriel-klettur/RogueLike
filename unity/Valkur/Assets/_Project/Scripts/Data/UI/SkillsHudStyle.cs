using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Every judgement the talents board makes that is its OWN — geometry, timings, particle
    /// budgets — in one asset. Colours come from <see cref="HudTheme"/> (shared with every HUD
    /// surface) and the pixel grid from <see cref="PlayerHudStyle"/> (the one rule for how many
    /// screen pixels a texel covers), so the board and the player panel land on the same grid at
    /// every resolution.
    ///
    /// <para>Under <c>Resources/UI/</c> for the reason every other tuning asset in this project
    /// is: the panel is created by <c>CharacterSheetController</c> on a bare GameObject and has
    /// no inspector slot a <c>[SerializeField]</c> could ever be filled from — the
    /// <c>ChatSystem._catalog</c> defect.</para>
    ///
    /// <para><b>Geometry is in TEXELS.</b> A number here is multiplied by
    /// <c>PlayerHudStyle.HudPixelScaleFor</c> to reach the screen, never used as a pixel.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "SkillsHudStyle", menuName = "Valkur/UI/Skills HUD Style", order = 43)]
    public class SkillsHudStyle : ScriptableObject
    {
        /// <summary>Path passed to <c>Resources.Load</c>.</summary>
        public const string ResourcePath = "UI/SkillsHudStyle";

        [Header("Window (texels)")]
        [Range(2, 12)] public int paddingTexels = 6;
        [Range(9, 20)] public int titleBarTexels = 14;
        [Range(8, 20)] public int flavourTexels = 11;
        [Range(8, 20)] public int footerTexels = 13;

        [Tooltip("Width of the detail card down the right-hand side. It is where the long text " +
                 "lives, which is the whole reason nothing on the board has to be truncated.")]
        [Range(90, 200)] public int cardWidthTexels = 148;

        [Tooltip("Gap between the board and the card.")]
        [Range(2, 12)] public int cardGapTexels = 6;

        [Header("Node")]
        [Tooltip("Inset of the baked icon inside its socket.")]
        [Range(2, 8)] public int iconInsetTexels = 4;

        [Tooltip("Side of one rank pip.")]
        [Range(2, 5)] public int pipTexels = 3;

        [Tooltip("Gap between two pips.")]
        [Range(1, 3)] public int pipGapTexels = 1;

        [Tooltip("Gap between the socket's bottom edge and the pip row.")]
        [Range(1, 5)] public int pipTopGapTexels = 2;

        [Header("Motion (seconds)")]
        [Tooltip("Show / hide fade. R7 forbids an alpha jump.")]
        [Range(0.04f, 0.3f)] public float fadeSeconds = 0.12f;

        [Tooltip("How long a bought pip stays snapped one texel proud of its row.")]
        [Range(0.05f, 0.4f)] public float pipSnapSeconds = 0.18f;

        [Tooltip("How long an edge takes to light from the prerequisite to the node it opens.")]
        [Range(0.1f, 0.6f)] public float edgeLightSeconds = 0.25f;

        [Tooltip("How long the cost shakes when a purchase is refused. No particles on a refusal.")]
        [Range(0.05f, 0.4f)] public float refuseShakeSeconds = 0.2f;

        [Header("Motes (R8: events only, never ambient)")]
        [Tooltip("Pool capacity for the whole window.")]
        [Range(16, 96)] public int moteCapacity = 48;

        [Tooltip("Motes thrown when one rank is bought.")]
        [Range(4, 20)] public int motesPerRank = 10;

        [Tooltip("Motes in the ring that marks a node becoming reachable.")]
        [Range(3, 12)] public int motesPerUnlock = 6;

        [Tooltip("Motes that fall onto the points medallion when a point arrives.")]
        [Range(1, 8)] public int motesPerPoint = 3;

        [Tooltip("Life of a mote. R8 caps it at 0.6 s.")]
        [Range(0.2f, 0.6f)] public float moteLifeSeconds = 0.45f;

        [Tooltip("Delay between one node and the next while a respec drains the board.")]
        [Range(0.01f, 0.1f)] public float respecStaggerSeconds = 0.03f;

        [Header("Backdrop")]
        [Tooltip("Alpha of the veil behind the window. It is what says the window is modal, and " +
                 "what stops the world competing with the text — the old panel had neither.")]
        [Range(0f, 0.85f)] public float veilAlpha = 0.55f;

        private static SkillsHudStyle s_cached;
        private static bool s_looked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_cached = null;
            s_looked = false;
        }

        /// <summary>The shipped asset, or a defaults instance when none is present.</summary>
        public static SkillsHudStyle Active
        {
            get
            {
                if (s_cached != null) return s_cached;
                if (!s_looked)
                {
                    s_looked = true;
                    s_cached = Resources.Load<SkillsHudStyle>(ResourcePath);
                }
                if (s_cached == null)
                {
                    s_cached = CreateInstance<SkillsHudStyle>();
                    s_cached.name = "SkillsHudStyle (defaults)";
                    s_cached.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_cached;
            }
        }
    }
}
