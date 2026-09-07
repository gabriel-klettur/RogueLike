using System.Globalization;
using System.Text;
using UnityEngine;
using Valkur.Core.Economy;
using Valkur.Gameplay.NPC;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>market</c>, <c>marketday</c>, <c>marketseed</c> and <c>coins</c> commands.
    ///
    /// <para><b>Why this exists.</b> The economic cycle is written by a day tick, shaped by a
    /// seed, read by the price pipeline and persisted through the save metadata bag — four
    /// places, each of which can fail in a way the other three hide. Without a probe, "did the
    /// market advance overnight" and "did the seed survive the reload" are not answerable
    /// without waiting for a day to pass and then doing arithmetic on a shop window. The
    /// audit that produced this layer scored the economy 3/10 on instrumentation for exactly
    /// that: there was no way to measure it, so there was no way to know it was broken.</para>
    ///
    /// <para><c>market forecast</c> is the one worth having open while tuning. A cycle whose
    /// phases the player is supposed to READ has to be legible to whoever balances it first,
    /// and a table of the next fortnight is what turns "does 0.25 amplitude feel like
    /// anything" into a question with an answer.</para>
    ///
    /// <para><b>On seeding a market from the real world.</b> <c>marketseed &lt;n&gt;</c> is
    /// deliberately the ONLY door, and it writes the number into the save. That is the safe
    /// shape for an outside index — including a crypto or equity price, if that is wanted:
    /// read once, cached, and never consulted again while playing. A price that re-read a live
    /// feed could be moved by the player with a proxy or a system clock, could not be
    /// reproduced from a bug report, would not exist offline, and would make the same save
    /// load as a different game tomorrow. What an index can honestly decide is the FLAVOUR of
    /// a stretch of days; the rules stay in <see cref="MarketCycle"/>, authored and bounded.
    /// </para>
    ///
    /// Registered from <c>DevConsole.cs::RegisterDefaults()</c>.
    /// </summary>
    public partial class DevConsole
    {
        /// <summary>Days printed by <c>market forecast</c> when no count is given.</summary>
        private const int MARKET_FORECAST_DEFAULT_DAYS = 14;

        /// <summary>Widest forecast the console will print, so one typo cannot flood it.</summary>
        private const int MARKET_FORECAST_MAX_DAYS = 60;

        private void RegisterEconomyCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name     = "market",
                Usage    = "market [forecast [days]|on|off]",
                Help     = "report the economic cycle: phase, pressure, price multipliers",
                Category = "economy",
                Handler  = args => CmdMarket(args)
            });

            RegisterCommand(new ConsoleCommand
            {
                Name     = "marketday",
                Usage    = "marketday [+n]",
                Help     = "advance the market clock by n days (default 1) without waiting",
                Category = "economy",
                Handler  = args => CmdMarketDay(args)
            });

            RegisterCommand(new ConsoleCommand
            {
                Name     = "marketseed",
                Usage    = "marketseed <n> [source]",
                Help     = "reseed the cycle; the seed is saved and never re-read while playing",
                Category = "economy",
                Handler  = args => CmdMarketSeed(args)
            });

            RegisterCommand(new ConsoleCommand
            {
                Name     = "coins",
                Usage    = "coins [n]",
                Help     = "report the purse, or set it",
                Category = "economy",
                Handler  = args => CmdCoins(args)
            });
        }

        private MarketService RequireMarket()
        {
            if (MarketService.HasInstance) return MarketService.Instance;
            Log("No MarketService in the scene. Prices are resolving at a flat multiplier.");
            return null;
        }

        private void CmdMarket(string[] args)
        {
            var market = RequireMarket();
            if (market == null) return;

            if (args.Length > 0)
            {
                string sub = args[0].ToLowerInvariant();
                if (sub == "on" || sub == "off")
                {
                    market.SetCycleEnabled(sub == "on");
                    Log($"Economic cycle {(market.CycleEnabled ? "ON" : "OFF")}.");
                    return;
                }
                if (sub == "forecast")
                {
                    int days = MARKET_FORECAST_DEFAULT_DAYS;
                    if (args.Length > 1 &&
                        int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                        days = Mathf.Clamp(parsed, 1, MARKET_FORECAST_MAX_DAYS);
                    LogForecast(market, days);
                    return;
                }
            }

            var now = market.Current;
            var sb = new StringBuilder();
            sb.AppendLine($"Market: {now.Phase}  day {market.Day} " +
                          $"(day {now.DayInCycle + 1} of a {now.CycleLength}-day cycle)");
            sb.AppendLine($"  pressure {now.Pressure:+0.00;-0.00; 0.00}   " +
                          $"you pay x{market.BuyMultiplier:0.00}   you receive x{market.SellMultiplier:0.00}");
            sb.Append($"  seed {market.Seed} (source: {market.SeedSource})   " +
                      $"cycle {(market.CycleEnabled ? "on" : "OFF")}");
            Log(sb.ToString());
        }

        private void LogForecast(MarketService market, int days)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Forecast, {days} day(s) from day {market.Day} (seed {market.Seed}):");
            for (int i = 0; i < days; i++)
            {
                var m = MarketCycle.Resolve(market.Seed, market.Day + i);
                string here = i == 0 ? " <- today" : "";
                sb.AppendLine($"  d{market.Day + i,-5} {m.Phase,-7} " +
                              $"pay x{m.BuyMultiplier:0.00}  get x{m.SellMultiplier:0.00}{here}");
            }
            Log(sb.ToString().TrimEnd());
        }

        private void CmdMarketDay(string[] args)
        {
            var market = RequireMarket();
            if (market == null) return;

            int days = 1;
            if (args.Length > 0)
            {
                string raw = args[0].TrimStart('+');
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out days))
                {
                    Log($"Not a number: '{args[0]}'.");
                    return;
                }
            }

            if (days <= 0)
            {
                // Refused rather than clamped. The market clock is monotonic ON PURPOSE — a
                // rewindable one lets a Peak be farmed by reloading, which is the single
                // exploit this whole layer has to be immune to.
                Log("The market clock only moves forward. Reseed with 'marketseed' instead.");
                return;
            }

            market.AdvanceDay(days);
            CmdMarket(System.Array.Empty<string>());
        }

        private void CmdMarketSeed(string[] args)
        {
            var market = RequireMarket();
            if (market == null) return;

            if (args.Length == 0)
            {
                Log($"Seed {market.Seed} (source: {market.SeedSource}). Usage: marketseed <n> [source]");
                return;
            }

            if (!int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
            {
                Log($"Not a number: '{args[0]}'.");
                return;
            }
            if (seed == 0)
            {
                Log("0 is the 'nobody set a seed' sentinel and is refused. Pick any other integer.");
                return;
            }

            string source = args.Length > 1 ? args[1] : "console";
            market.SetSeed(seed, source);
            CmdMarket(System.Array.Empty<string>());
        }

        private void CmdCoins(string[] args)
        {
            var player = Valkur.Core.EntityRegistry.PlayerTransform;
            var wallet = player != null ? player.GetComponent<CurrencyWallet>() : null;
            if (wallet == null) { Log("Player has no CurrencyWallet."); return; }

            if (args.Length == 0)
            {
                Log($"Purse: {wallet.Coins} coin(s).");
                return;
            }

            if (!int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
            {
                Log($"Not a number: '{args[0]}'.");
                return;
            }

            wallet.SetBalance(Mathf.Max(0, amount));
            Log($"Purse: {wallet.Coins} coin(s).");
        }
    }
}
