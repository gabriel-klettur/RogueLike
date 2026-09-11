using Valkur.Core;

namespace Valkur.Gameplay.Quests
{
    /// <summary>
    /// "Have N coins in the purse at once." The counter is the live balance, so it
    /// goes DOWN when the player spends — which is the point: this is a SAVINGS
    /// objective, not a lifetime-earnings one, and a merchant asking the player to
    /// prove they are good for the money should not be satisfied by somebody who
    /// earned it and spent it.
    ///
    /// <para>Polled rather than bound to <c>CurrencyWallet.OnCoinsChanged</c>, which
    /// is a STATIC event on a class living under Domain Reload OFF — a handler left
    /// behind by a quest that ended keeps a dead objective alive for the session,
    /// which is the leak <c>GameEvents.Clear</c> exists to undo for the events it
    /// owns. Polling a purse costs one field read a few times a second.</para>
    /// </summary>
    public sealed class EarnCoinsObjective : ObjectiveBase
    {
        public override bool IsPollable => true;

        public EarnCoinsObjective(string id, string description, int coins)
            : base(id, description, coins)
        {
        }

        public override void Poll()
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) return;
            var wallet = player.GetComponent<CurrencyWallet>();
            if (wallet == null) return;
            SetCurrent(wallet.Coins);
        }
    }
}
