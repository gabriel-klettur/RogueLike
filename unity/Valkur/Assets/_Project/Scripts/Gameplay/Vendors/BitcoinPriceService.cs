using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Valkur.Core.Economy;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.NPC
{
    /// <summary>
    /// Fetches Bitcoin candles for the Economy editor's chart. A VIEWER, not a gameplay input.
    ///
    /// <para><b>OPT-IN, AND OFF UNTIL SOMEONE SAYS OTHERWISE.</b> Nothing here runs unless
    /// <see cref="Enabled"/> has been set, and it is never set from boot. A game that reaches
    /// the network without being asked is a game phoning home, and the consent for that is the
    /// author's to give — so the editor shows a button and the choice persists in
    /// <c>PlayerPrefs</c>, one click for the life of the machine rather than one per session.
    /// <see cref="Fetch"/> called while disabled returns a refusal, not an exception: the
    /// editor renders it as a line of text.</para>
    ///
    /// <para><b>It never touches a price the GAME reads.</b> The chart informs an author; the
    /// only path from here into the world is the editor's explicit "seed the market from this
    /// close" button, which goes through <c>BitcoinSeries.SeedFromLastClose</c> and
    /// <c>MarketService.SetSeed</c> — read once, written to the save, never re-read. Wiring a
    /// live feed into pricing would make prices client-authoritative (a proxy or a system
    /// clock moves them), irreproducible from a bug report, and absent offline.</para>
    ///
    /// <para><b>Binance's public klines endpoint, because it needs no key and no account.</b>
    /// An API key in a game client is a key the player has, so any endpoint requiring one was
    /// out. The response is an array OF ARRAYS, which <c>JsonUtility</c> cannot express at all
    /// — it goes through <see cref="MiniJsonRuntime"/>, the project's hardened parser, rather
    /// than a hand-rolled split that would be the fourth unterminated-input hang in this
    /// codebase's history.</para>
    /// </summary>
    public sealed class BitcoinPriceService : MonoBehaviour
    {
        /// <summary>PlayerPrefs key holding the author's opt-in. Machine state, never saved to a run.</summary>
        public const string EnabledPrefKey = "valkur.economy.btcFeedEnabled";

        /// <summary>The symbol charted. Not authorable — this is a Bitcoin chart, not a terminal.</summary>
        private const string Symbol = "BTCUSDT";

        /// <summary>
        /// Candles per request. 120 is chosen for the DRAWING, not the data: the chart is a few
        /// hundred pixels wide, so beyond roughly one candle per two pixels the extra points
        /// cost bandwidth and mesh vertices and change nothing anyone can see.
        /// </summary>
        private const int CandleLimit = 120;

        /// <summary>
        /// How long a fetched series stays fresh. Shorter than the shortest timeframe on
        /// purpose — a 1h chart refetched every five minutes shows the live candle moving,
        /// which is the only part of it that changes between hours.
        /// </summary>
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

        /// <summary>Given up on after this. A dev tool must never hang the editor it is drawn in.</summary>
        private const int TimeoutSeconds = 10;

        private readonly Dictionary<BitcoinTimeframe, BitcoinSeries> _cache =
            new Dictionary<BitcoinTimeframe, BitcoinSeries>();

        private readonly HashSet<BitcoinTimeframe> _inFlight = new HashSet<BitcoinTimeframe>();

        /// <summary>Last failure, for the status line. Empty when the last attempt succeeded.</summary>
        public string LastError { get; private set; } = string.Empty;

        /// <summary>Whether the author has opted in to reaching the network.</summary>
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(EnabledPrefKey, 0) != 0;
            set { PlayerPrefs.SetInt(EnabledPrefKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>The cached series for a timeframe, or null when nothing has been fetched.</summary>
        public BitcoinSeries Cached(BitcoinTimeframe tf) =>
            _cache.TryGetValue(tf, out var s) ? s : null;

        /// <summary>Whether a request for this timeframe is currently out.</summary>
        public bool IsFetching(BitcoinTimeframe tf) => _inFlight.Contains(tf);

        /// <summary>Whether the cached series is old enough to be worth refetching.</summary>
        public bool IsStale(BitcoinTimeframe tf)
        {
            var s = Cached(tf);
            return s == null || s.AgeUtc > CacheTtl;
        }

        /// <summary>
        /// Fetch a timeframe unless one is already in flight or the cache is still fresh.
        /// <paramref name="force"/> ignores the TTL but never the opt-in or the in-flight guard.
        ///
        /// <para>Fire-and-forget by design: the editor polls <see cref="Cached"/> when it
        /// repaints. Returning a Task the caller must await would put an async lifetime inside
        /// a MonoBehaviour that can be destroyed mid-request, which is how a "destroyed object"
        /// exception arrives several seconds after the thing that caused it.</para>
        /// </summary>
        public void Fetch(BitcoinTimeframe tf, bool force = false)
        {
            if (!Enabled)
            {
                LastError = "El feed está desactivado. Actívalo para consultar la red.";
                return;
            }
            if (_inFlight.Contains(tf)) return;
            if (!force && !IsStale(tf)) return;

            _inFlight.Add(tf);
            _ = FetchAsync(tf);
        }

        private async Task FetchAsync(BitcoinTimeframe tf)
        {
            string url = "https://api.binance.com/api/v3/klines" +
                         $"?symbol={Symbol}&interval={IntervalOf(tf)}&limit={CandleLimit}";
            try
            {
                string body = await GetAsync(url);
                var candles = ParseKlines(body);
                if (candles.Count == 0)
                {
                    LastError = "La respuesta no contenía velas.";
                }
                else
                {
                    _cache[tf] = new BitcoinSeries(tf, candles, DateTime.UtcNow, "Binance");
                    LastError = string.Empty;
                }
            }
            catch (Exception ex)
            {
                // Caught rather than logged as an error: being offline is an ordinary state for
                // this feature, and CLAUDE.md's cardinal rule is that the console stays clean.
                // A warning that fires every time a laptop is on a train trains the reader to
                // scroll past the console.
                LastError = ex.Message;
            }
            finally
            {
                _inFlight.Remove(tf);
            }
        }

        /// <summary>
        /// The Binance interval token for a timeframe. A switch rather than a lowercased
        /// enum name, so renaming the enum cannot silently change what is requested.
        /// </summary>
        private static string IntervalOf(BitcoinTimeframe tf)
        {
            switch (tf)
            {
                case BitcoinTimeframe.Hour1: return "1h";
                case BitcoinTimeframe.Hour4: return "4h";
                default: return "1d";
            }
        }

        /// <summary>
        /// GET, awaited through a completion source.
        ///
        /// <para><c>UnityWebRequest.SendWebRequest</c> must be called on the main thread and
        /// its <c>AsyncOperation</c> is not awaitable — the same reason
        /// <c>OpenAiChatProvider</c> wraps it this way. Sharing the pattern rather than
        /// inventing a second one keeps both on the transport that works on every platform
        /// this ships to, WebGL included, where <c>HttpClient</c> does not.</para>
        /// </summary>
        private static async Task<string> GetAsync(string url)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = TimeoutSeconds;

                var completion = new TaskCompletionSource<bool>();
                var op = request.SendWebRequest();
                op.completed += _ => completion.TrySetResult(true);
                await completion.Task;

                if (request.result != UnityWebRequest.Result.Success)
                    throw new Exception($"{request.responseCode} {request.error}");

                return request.downloadHandler.text;
            }
        }

        /// <summary>
        /// Binance klines: an array of arrays, each
        /// <c>[openTime, open, high, low, close, volume, closeTime, ...]</c> with every price
        /// as a STRING.
        ///
        /// <para>Parsed with <see cref="MiniJsonRuntime"/>, and every number read through
        /// <see cref="ToDouble"/> with <see cref="CultureInfo.InvariantCulture"/> — a machine
        /// with a comma decimal separator would otherwise read "61234.51" as 6123451 and draw a
        /// chart that is internally consistent and a hundred times wrong. A malformed row is
        /// SKIPPED rather than failing the batch: a partial chart is better than none, and the
        /// row count is not something a caller depends on.</para>
        /// </summary>
        internal static List<BitcoinCandle> ParseKlines(string json)
        {
            var result = new List<BitcoinCandle>();
            if (string.IsNullOrWhiteSpace(json)) return result;

            if (!(MiniJsonRuntime.Deserialize(json) is List<object> rows)) return result;

            foreach (var row in rows)
            {
                if (!(row is List<object> c) || c.Count < 5) continue;
                try
                {
                    result.Add(new BitcoinCandle(
                        (long)ToDouble(c[0]),
                        ToDouble(c[1]), ToDouble(c[2]), ToDouble(c[3]), ToDouble(c[4])));
                }
                catch
                {
                    // One bad row is not a bad response.
                }
            }
            return result;
        }

        /// <summary>
        /// A JSON value as a double, whether it arrived as a number or as a quoted string.
        /// Binance sends prices quoted and times bare, so both shapes appear in one row.
        /// </summary>
        private static double ToDouble(object value)
        {
            if (value is double d) return d;
            if (value is long l) return l;
            if (value is int i) return i;
            return double.Parse(Convert.ToString(value, CultureInfo.InvariantCulture),
                                NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
