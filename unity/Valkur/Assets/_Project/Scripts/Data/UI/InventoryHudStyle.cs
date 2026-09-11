using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Every judgement the inventory window makes that is its OWN — geometry, timings, particle
    /// budgets, sounds — in one asset. Colours come from <see cref="HudTheme"/> (shared with every
    /// HUD surface) and the pixel grid from <see cref="PlayerHudStyle"/> (the one rule for how
    /// many screen pixels a texel covers), so the window and the player panel beside it land on the
    /// same grid at every resolution.
    ///
    /// <para>Under <c>Resources/UI/</c> because <c>InventoryUI</c> is <c>AddComponent</c>-ed by
    /// <c>EntitySetup</c> and has no inspector slot. It also carries the tray button's sprite: the
    /// old window loaded it through <c>AssetDatabase</c>, which does not exist in a player build,
    /// so the button was a grey square there.</para>
    ///
    /// <para><b>Geometry is in TEXELS.</b></para>
    /// </summary>
    [CreateAssetMenu(fileName = "InventoryHudStyle", menuName = "Valkur/UI/Inventory HUD Style", order = 42)]
    public class InventoryHudStyle : ScriptableObject
    {
        /// <summary>Path passed to <c>Resources.Load</c>.</summary>
        public const string ResourcePath = "UI/InventoryHudStyle";

        [Header("Tray")]
        [Tooltip("The inventory's button in the bottom-right tray.")]
        public Sprite trayIcon;

        [Header("Layout (texels)")]
        [Range(2, 12)] public int paddingTexels = 6;
        [Range(9, 16)] public int titleBarTexels = 11;
        [Range(16, 32)] public int slotTexels = 22;
        [Range(1, 4)] public int slotGapTexels = 2;
        [Range(2, 6)] public int iconInsetTexels = 3;
        [Range(3, 8)] public int bagColumns = 5;
        [Range(11, 16)] public int tabTexels = 13;
        [Range(2, 8)] public int sectionGapTexels = 4;
        [Range(8, 14)] public int footerTexels = 11;
        [Range(80, 140)] public int cardWidthTexels = 116;

        [Header("Feel")]
        [Range(0f, 0.5f)] public float openSeconds = 0.12f;
        [Range(0f, 0.5f)] public float closeSeconds = 0.09f;
        [Range(0, 8)] public int openRiseTexels = 4;
        [Range(0f, 0.6f)] public float slotFlashSeconds = 0.2f;
        [Range(0f, 0.6f)] public float refusalSeconds = 0.3f;
        [Range(0, 3)] public int refusalShakeTexels = 1;
        [Range(0f, 1.5f)] public float countSeconds = 0.5f;
        [Range(0f, 1f)] public float dragSourceAlpha = 0.35f;
        [Range(0f, 1f)] public float filteredAlpha = 0.22f;
        [Range(0f, 1f)] public float silhouetteAlpha = 0.28f;
        [Tooltip("Seconds the pointer rests on a slot before its card opens.")]
        [Range(0f, 0.6f)] public float cardDelaySeconds = 0.12f;

        [Header("Motes (event particles)")]
        [Range(0, 128)] public int moteCapacity = 72;
        [Range(0, 16)] public int motesOnPickup = 4;
        [Range(0, 32)] public int motesOnRare = 14;
        [Range(0, 48)] public int motesOnLegendary = 26;
        [Range(0, 24)] public int motesOnEquip = 10;
        [Range(0, 16)] public int motesOnUnequip = 5;
        [Range(0, 16)] public int motesOnConsume = 7;
        [Range(0, 16)] public int motesOnCoins = 8;
        [Tooltip("A change in gold at least this large throws sparks; smaller ones only glint.")]
        [Range(1, 500)] public int coinBurstThreshold = 50;
        [Range(0, 8)] public int motesOnSettle = 3;
        [Range(0.1f, 2f)] public float moteLifeSeconds = 0.6f;
        [Range(1f, 120f)] public float moteSpeedTexels = 30f;
        [Range(0f, 300f)] public float moteGravityTexels = 60f;

        [Header("Sound")]
        [Tooltip("Catalogue ids, tried first (gated on HasSfx). Empty, or an id with no clip, falls " +
                 "back to the synthesised sound for that event.")]
        public string sfxOpen = "inv_open";
        public string sfxPickup = "";
        public string sfxEquip = "";
        public string sfxUnequip = "";
        public string sfxRefuse = "";
        public string sfxCoin = "";
        public string sfxDrop = "";
        public string sfxConsume = "";
        public string sfxSort = "";
        [Range(0f, 1f)] public float volume = 0.55f;

        // -- Derived ---------------------------------------------------------

        /// <summary>Width of the bag grid, in texels.</summary>
        public int GridWidthTexels => bagColumns * slotTexels + (bagColumns - 1) * slotGapTexels;

        /// <summary>Height of a grid of <paramref name="rows"/> slot rows.</summary>
        public int GridHeightTexels(int rows) => rows * slotTexels + Mathf.Max(0, rows - 1) * slotGapTexels;

        /// <summary>Outer width of the window.</summary>
        public int PanelWidthTexels => paddingTexels * 2 + GridWidthTexels;

        // -- Resolution ------------------------------------------------------

        private static InventoryHudStyle s_cached;
        private static bool s_looked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInventoryHudStyleStatics()
        {
            s_cached = null;
            s_looked = false;
        }

        /// <summary>The shipped asset, or a defaults-only instance when it is missing.</summary>
        public static InventoryHudStyle Active
        {
            get
            {
                if (s_cached != null) return s_cached;
                if (!s_looked)
                {
                    s_looked = true;
                    s_cached = Resources.Load<InventoryHudStyle>(ResourcePath);
                }
                if (s_cached == null)
                {
                    s_cached = CreateInstance<InventoryHudStyle>();
                    s_cached.name = "InventoryHudStyle (defaults)";
                    s_cached.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_cached;
            }
        }
    }
}
