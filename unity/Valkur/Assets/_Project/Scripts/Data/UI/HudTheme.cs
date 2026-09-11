using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// The tokens every HUD surface shares — stone, gold, outline, recess, text, the state colours
    /// and the item-rarity ramp — written ONCE. R2 of <c>.github/HUD_VISUAL_LANGUAGE.md</c>.
    ///
    /// <para><b>Why it exists.</b> <c>PlayerHudStyle.gold</c> is (0.90, 0.76, 0.38) and
    /// <c>MinimapStyle.ringGold</c> is (0.90, 0.74, 0.38): the same gold with two hundredths of
    /// drift, which is how a theme starts to come apart. A surface style keeps what is its OWN —
    /// sizes, timings, budgets — and reads the shared colours from here.</para>
    ///
    /// <para><b>Rarity is shape as well as colour.</b> <see cref="RarityCorners"/> is the number of
    /// corner marks a slot draws for each tier, so two tiers a colour-blind player cannot tell
    /// apart by hue still differ by how many corners are lit.</para>
    ///
    /// <para>Under <c>Resources/UI/</c> for the reason every HUD style is: the surfaces that read
    /// it are built at runtime and have no inspector slot to be wired from.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "HudTheme", menuName = "Valkur/UI/HUD Theme", order = 40)]
    public class HudTheme : ScriptableObject
    {
        /// <summary>Path passed to <c>Resources.Load</c>.</summary>
        public const string ResourcePath = "UI/HudTheme";

        [Header("Material")]
        public Color outline = new Color(0.03f, 0.03f, 0.05f, 1f);
        public Color stoneLight = new Color(0.17f, 0.17f, 0.22f, 1f);
        public Color stoneDark = new Color(0.08f, 0.08f, 0.11f, 1f);
        [Tooltip("Anything that holds a value. OPAQUE: in linear space a 4 % gap lets ~18 % through.")]
        public Color recess = new Color(0.05f, 0.05f, 0.08f, 1f);
        public Color gold = new Color(0.90f, 0.76f, 0.38f, 1f);
        public Color goldShade = new Color(0.52f, 0.40f, 0.17f, 1f);
        public Color gem = new Color(0.72f, 0.12f, 0.16f, 1f);
        public Color gemLit = new Color(1f, 0.55f, 0.55f, 1f);

        [Header("Text")]
        public Color text = new Color(1f, 0.98f, 0.93f, 1f);
        public Color textDim = new Color(0.72f, 0.74f, 0.82f, 1f);
        public Color textDisabled = new Color(0.46f, 0.47f, 0.54f, 1f);

        [Header("State")]
        public Color success = new Color(0.45f, 0.92f, 0.45f, 1f);
        public Color warning = new Color(0.98f, 0.72f, 0.26f, 1f);
        public Color danger = new Color(0.98f, 0.36f, 0.30f, 1f);
        public Color info = new Color(0.55f, 0.75f, 1f, 1f);

        [Header("Rarity (means rarity and nothing else)")]
        public Color rarityCommon = new Color(0.80f, 0.80f, 0.82f, 1f);
        public Color rarityUncommon = new Color(0.40f, 0.88f, 0.42f, 1f);
        public Color rarityRare = new Color(0.36f, 0.62f, 1f, 1f);
        public Color rarityEpic = new Color(0.76f, 0.46f, 1f, 1f);
        public Color rarityLegendary = new Color(1f, 0.62f, 0.18f, 1f);

        /// <summary>The colour of a rarity tier.</summary>
        public Color RarityColour(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Uncommon: return rarityUncommon;
                case ItemRarity.Rare: return rarityRare;
                case ItemRarity.Epic: return rarityEpic;
                case ItemRarity.Legendary: return rarityLegendary;
                default: return rarityCommon;
            }
        }

        /// <summary>How many corner marks a slot draws for a tier: 0, 1, 2, 4, 4.</summary>
        public static int RarityCorners(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Uncommon: return 1;
                case ItemRarity.Rare: return 2;
                case ItemRarity.Epic:
                case ItemRarity.Legendary: return 4;
                default: return 0;
            }
        }

        /// <summary>Player-facing name of a tier.</summary>
        public static string RarityName(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Uncommon: return "Poco común";
                case ItemRarity.Rare: return "Raro";
                case ItemRarity.Epic: return "Épico";
                case ItemRarity.Legendary: return "Legendario";
                default: return "Común";
            }
        }

        // -- Resolution ------------------------------------------------------

        private static HudTheme s_cached;
        private static bool s_looked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetHudThemeStatics()
        {
            s_cached = null;
            s_looked = false;
        }

        /// <summary>The shipped asset, or a defaults-only instance when it is missing.</summary>
        public static HudTheme Active
        {
            get
            {
                if (s_cached != null) return s_cached;
                if (!s_looked)
                {
                    s_looked = true;
                    s_cached = Resources.Load<HudTheme>(ResourcePath);
                }
                if (s_cached == null)
                {
                    s_cached = CreateInstance<HudTheme>();
                    s_cached.name = "HudTheme (defaults)";
                    s_cached.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_cached;
            }
        }
    }
}
