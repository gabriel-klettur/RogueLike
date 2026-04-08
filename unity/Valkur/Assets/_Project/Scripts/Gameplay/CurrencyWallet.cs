using UnityEngine;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Tracks the player's coin (currency) balance.
    /// Maps Python's GoldComponent / gold field on player entity.
    ///
    /// Fires OnCoinsChanged when balance changes.
    /// Add to the Player GameObject via EntitySetup.
    /// </summary>
    public class CurrencyWallet : MonoBehaviour
    {
        [SerializeField, Tooltip("Starting coin balance.")]
        private int startingCoins;

        // ── State ─────────────────────────────────────────────────────
        private int _coins;

        public int Coins => _coins;

        // ── Events ────────────────────────────────────────────────────

        /// <summary>Fires after balance changes. Args: newBalance, delta.</summary>
        public static System.Action<int, int> OnCoinsChanged;

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            _coins = startingCoins;
        }

        // ── Public API ────────────────────────────────────────────────

        /// <summary>Add coins (positive) to the wallet.</summary>
        public void Add(int amount)
        {
            if (amount <= 0) return;
            _coins += amount;
            OnCoinsChanged?.Invoke(_coins, amount);
        }

        /// <summary>
        /// Try to spend coins. Returns true and deducts on success.
        /// Returns false without modifying balance if insufficient funds.
        /// </summary>
        public bool TrySpend(int cost)
        {
            if (cost <= 0) return true;
            if (_coins < cost) return false;

            _coins -= cost;
            OnCoinsChanged?.Invoke(_coins, -cost);
            return true;
        }

        /// <summary>Force-set balance (e.g. save/load restore).</summary>
        public void SetBalance(int amount)
        {
            int delta = amount - _coins;
            _coins = Mathf.Max(0, amount);
            OnCoinsChanged?.Invoke(_coins, delta);
        }
    }
}
