using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Inventory;

namespace Valkur.Gameplay.NPC
{
    /// <summary>
    /// Vendor NPC that sells/buys items.
    /// Maps to Python's vendor NPC system with buy/sell price support.
    /// </summary>
    [RequireComponent(typeof(NPCInteractable))]
    public class VendorNPC : MonoBehaviour
    {
        [Header("Shop")]
        [SerializeField] private List<ShopEntry> shopInventory = new List<ShopEntry>();
        [SerializeField] private float buyPriceMultiplier = 1.0f;
        [SerializeField] private float sellPriceMultiplier = 0.5f;

        [Header("Economy Config (optional)")]
        [Tooltip("Assign to use economy-aware pricing pipeline. Falls back to simple multipliers if null.")]
        [SerializeField] private VendorConfigDefinition vendorConfig;

        private NPCInteractable _interactable;

        [System.Serializable]
        public struct ShopEntry
        {
            public ItemDefinition item;
            public int stock;
            public int priceOverride;
        }

        public IReadOnlyList<ShopEntry> ShopInventory => shopInventory;
        public float BuyMultiplier => buyPriceMultiplier;
        public float SellMultiplier => sellPriceMultiplier;
        public VendorConfigDefinition VendorConfig => vendorConfig;

        private void Awake()
        {
            _interactable = GetComponent<NPCInteractable>();

            // Auto-register on the minimap. Gold square with a slow pulse —
            // calls the player to "there's a vendor here" without the visual
            // urgency of a quest objective. The caption is the role initials
            // (e.g. "Lumber Jack" → "LJ") so the player can distinguish a
            // black smith from a chef at a glance, without reading a name.
            EntitySetup.ConfigureMinimapMarker(
                gameObject,
                color: new Color(1.0f, 0.85f, 0.3f, 1f),
                shape: EntitySetup.MinimapMarkerShape.Square,
                pixelSize: 4,
                pulse: true,
                pulsePeriod: 1.4f,
                label: DeriveRoleInitials(vendorConfig));
        }

        /// <summary>
        /// Derive the 1–2-letter role initials shown next to the vendor's
        /// minimap dot. Reads <c>persona.displayName</c> first ("Lumber Jack"
        /// → "LJ"); falls back to <c>vendorConfig.vendorKey</c>; finally to
        /// the GameObject name. Returns empty when nothing usable is set.
        /// </summary>
        private static string DeriveRoleInitials(VendorConfigDefinition cfg)
        {
            string source = null;
            if (cfg != null)
            {
                if (cfg.persona != null && !string.IsNullOrWhiteSpace(cfg.persona.displayName))
                    source = cfg.persona.displayName;
                else if (!string.IsNullOrWhiteSpace(cfg.vendorKey))
                    source = cfg.vendorKey;
            }
            return InitialsFromName(source);
        }

        private static string InitialsFromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            // Split on whitespace, underscore, or hyphen so "vendor_blacksmith"
            // and "Black-Smith" both yield "BS". Empty fragments are dropped.
            var parts = name.Split(new[] { ' ', '_', '-' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return string.Empty;

            // Single token: take the first two letters ("Gatita" → "GA"). Avoids
            // a one-letter caption that's hard to read at minimap scale.
            if (parts.Length == 1)
            {
                var w = parts[0];
                return w.Length >= 2
                    ? string.Concat(char.ToUpperInvariant(w[0]), char.ToUpperInvariant(w[1]))
                    : char.ToUpperInvariant(w[0]).ToString();
            }

            // Multi-token: first letter of the first two tokens
            // ("Lumber Jack" → "LJ", "vendor_chef_gatita" → "VC"; tweak prefix
            // filtering downstream if the "vendor" stem is unwanted).
            return string.Concat(char.ToUpperInvariant(parts[0][0]), char.ToUpperInvariant(parts[1][0]));
        }

        /// <summary>
        /// Hands this vendor its configuration after the component was added at spawn.
        ///
        /// Both the config and the shop list are <c>[SerializeField] private</c>, which is
        /// right for a vendor authored in a scene and unreachable for one that
        /// <c>EntitySetup</c> builds from a <c>MonsterDefinition</c> — and building them
        /// that way is the only path any entity in this game takes. It also re-applies the
        /// minimap marker, because <c>Awake</c> has already run by the time the caller can
        /// reach this method and derived its caption from a config that was still null.
        ///
        /// The shop list is seeded from <c>inventorySeed</c> ONLY when it is empty, so a
        /// vendor whose stock was authored by hand keeps it.
        /// </summary>
        public void Configure(VendorConfigDefinition config)
        {
            if (config == null) return;
            vendorConfig = config;

            if (shopInventory.Count == 0 && config.inventorySeed != null)
            {
                foreach (var slot in config.inventorySeed)
                {
                    if (slot.item == null) continue;
                    shopInventory.Add(new ShopEntry
                    {
                        item = slot.item,
                        stock = Mathf.Max(1, slot.quantity),
                        priceOverride = 0,
                    });
                }
            }

            EntitySetup.ConfigureMinimapMarker(
                gameObject,
                color: new Color(1.0f, 0.85f, 0.3f, 1f),
                shape: EntitySetup.MinimapMarkerShape.Square,
                pixelSize: 4,
                pulse: true,
                pulsePeriod: 1.4f,
                label: DeriveRoleInitials(vendorConfig));
        }

        private void OnEnable()
        {
            _interactable.OnInteract += HandleInteract;
        }

        private void OnDisable()
        {
            _interactable.OnInteract -= HandleInteract;
        }

        private void HandleInteract(NPCInteractable npc)
        {
            var playerGo = Valkur.Core.EntityRegistry.PlayerTransform?.gameObject;
            if (playerGo == null)
            {
                Debug.LogWarning("[VendorNPC] No player found to open shop.");
                return;
            }

            var shopUI = VendorShopUI.Instance;
            if (shopUI == null)
            {
                Debug.LogWarning("[VendorNPC] VendorShopUI singleton not found in scene.");
                return;
            }

            var playerInventory = playerGo.GetComponent<Inventory.Inventory>();
            var playerWallet = playerGo.GetComponent<CurrencyWallet>();
            shopUI.OpenShop(this, playerInventory, playerWallet);
        }

        public int GetBuyPrice(ItemDefinition item)
        {
            // Economy-aware pipeline when VendorConfigDefinition is assigned
            if (vendorConfig != null && VendorEconomyService.Instance != null)
                return VendorEconomyService.Instance.GetBuyPrice(vendorConfig, item);

            foreach (var entry in shopInventory)
            {
                if (entry.item == item && entry.priceOverride > 0)
                    return entry.priceOverride;
            }
            return Mathf.RoundToInt(item.buyPrice * buyPriceMultiplier);
        }

        public int GetSellPrice(ItemDefinition item)
        {
            // Economy-aware pipeline when VendorConfigDefinition is assigned
            if (vendorConfig != null && VendorEconomyService.Instance != null)
                return VendorEconomyService.Instance.GetSellPrice(vendorConfig, item);

            if (item == null) return 0;
            int basePrice = item.sellPrice > 0 ? item.sellPrice : item.buyPrice;
            return Mathf.RoundToInt(basePrice * sellPriceMultiplier);
        }

        public bool TryBuyItem(ItemDefinition item, Inventory.Inventory playerInventory, ref int playerGold)
        {
            int price = GetBuyPrice(item);
            if (playerGold < price) return false;
            if (playerInventory.IsFull) return false;

            // Check stock
            for (int i = 0; i < shopInventory.Count; i++)
            {
                if (shopInventory[i].item == item && shopInventory[i].stock > 0)
                {
                    var entry = shopInventory[i];
                    entry.stock--;
                    shopInventory[i] = entry;

                    playerGold -= price;
                    playerInventory.AddItem(item);
                    return true;
                }
            }
            return false;
        }

        public bool TrySellItem(ItemDefinition item, Inventory.Inventory playerInventory, ref int playerGold)
        {
            if (!playerInventory.HasItem(item)) return false;

            int price = GetSellPrice(item);
            playerInventory.RemoveItem(item);
            playerGold += price;
            return true;
        }

        /// <summary>Buy an item using a CurrencyWallet. Automatically handles refunds on failure.</summary>
        public bool TryBuyItem(ItemDefinition item, Inventory.Inventory playerInventory, CurrencyWallet wallet)
        {
            int price = GetBuyPrice(item);
            if (!wallet.TrySpend(price)) return false;

            if (playerInventory.IsFull)
            {
                wallet.Add(price);
                return false;
            }

            for (int i = 0; i < shopInventory.Count; i++)
            {
                if (shopInventory[i].item == item && shopInventory[i].stock > 0)
                {
                    var entry = shopInventory[i];
                    entry.stock--;
                    shopInventory[i] = entry;
                    playerInventory.AddItem(item);

                    // Hooked HERE, at the single point a purchase actually succeeds, rather
                    // than at the Buy button — a trade agreed in conversation goes through
                    // this same method and must look the same as one made at the counter.
                    TradeFlourishFX.Spent(Valkur.Core.EntityRegistry.PlayerTransform, price);
                    return true;
                }
            }

            wallet.Add(price); // refund — item not in stock
            return false;
        }

        /// <summary>Sell an item to this vendor using a CurrencyWallet.</summary>
        public bool TrySellItem(ItemDefinition item, Inventory.Inventory playerInventory, CurrencyWallet wallet)
        {
            if (!playerInventory.HasItem(item)) return false;
            int price = GetSellPrice(item);
            playerInventory.RemoveItem(item);
            wallet.Add(price);

            TradeFlourishFX.Earned(Valkur.Core.EntityRegistry.PlayerTransform, price);
            return true;
        }

        /// <summary>
        /// Add <paramref name="qty"/> units of stock for <paramref name="item"/>.
        /// Used by the DevConsole 'restockvendorfood' command to replenish consumable inventory.
        /// No-ops when the item is not in this vendor's shop list.
        /// </summary>
        public void RestockItem(ItemDefinition item, int qty)
        {
            if (item == null || qty <= 0) return;
            for (int i = 0; i < shopInventory.Count; i++)
            {
                if (shopInventory[i].item == item)
                {
                    var entry = shopInventory[i];
                    entry.stock += qty;
                    shopInventory[i] = entry;
                    return;
                }
            }
        }
    }
}
