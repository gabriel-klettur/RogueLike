using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core.Economy;
using Valkur.Gameplay.NPC;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.Economy
{
    public partial class EconomyRuntimeEditor
    {
        /// <summary>Height of the chart rect, in pixels. Tall enough for a shape, short enough to sit above the readouts.</summary>
        private const float CHART_H = 190f;

        /// <summary>
        /// How often the open Bitcoin tab repaints while a request is out. The chart itself
        /// only changes when a fetch lands, so this exists for the "Consultando..." line and
        /// for the age counter — at one second the age reads as a clock rather than a
        /// stopwatch, which is the right resolution for something measured in minutes.
        /// </summary>
        private const float BTC_POLL_SECONDS = 1f;

        private SparklineGraphic _chart;
        private TextMeshProUGUI _btcPrice;
        private TextMeshProUGUI _btcMeta;
        private Button _btcToggleButton;
        private float _btcPollClock;
        private BitcoinPriceService _btc;

        /// <summary>
        /// The price service, created on demand and parented to this editor.
        ///
        /// <para>Created HERE rather than in scene bootstrap, and that is deliberate: nothing
        /// that can reach the network should exist in a session where nobody asked for it. An
        /// author who never opens this tab never has the component, never mind the request.</para>
        /// </summary>
        private BitcoinPriceService Btc
        {
            get
            {
                if (_btc == null)
                {
                    var go = new GameObject("BitcoinPriceService");
                    go.transform.SetParent(transform, false);
                    _btc = go.AddComponent<BitcoinPriceService>();
                }
                return _btc;
            }
        }

        // ── Tab: Bitcoin ────────────────────────────────────────────────────

        private void BuildBitcoinTab(Transform parent)
        {
            EditorUIHelpers.BuildSectionHeader(parent, "Bitcoin (BTCUSDT)");

            if (!BitcoinPriceService.Enabled)
            {
                AddHint(parent,
                    "El feed esta DESACTIVADO. Activarlo hace que el juego consulte api.binance.com " +
                    "cuando abras esta pestana. Es una decision tuya, no del arranque: nada aqui " +
                    "sale a la red sin que lo pidas, y la eleccion se recuerda en esta maquina.");
            }

            var bar = MakeRow(parent, "BtcToolbar", 28f);
            _btcToggleButton = EditorUIHelpers.MakeButton(bar.transform,
                BitcoinPriceService.Enabled ? "FEED ON" : "FEED OFF", ToggleFeed, 28f, 11f);
            EditorUIHelpers.MakeButton(bar.transform, "ACTUALIZAR", RefreshFeed, 28f, 11f);

            var tfBar = MakeRow(parent, "BtcTimeframes", 28f);
            AddTimeframe(tfBar.transform, BitcoinTimeframe.Hour1, "1H");
            AddTimeframe(tfBar.transform, BitcoinTimeframe.Hour4, "4H");
            AddTimeframe(tfBar.transform, BitcoinTimeframe.Day1, "DIARIO");

            _btcPrice = EditorUIHelpers.AddLabel(parent, "—", 20f);
            var priceElement = _btcPrice.gameObject.AddComponent<LayoutElement>();
            priceElement.preferredHeight = 30f;
            priceElement.minHeight = 30f;
            priceElement.flexibleHeight = 0f;

            BuildChart(parent);

            _btcMeta = EditorUIHelpers.AddLabel(parent, string.Empty, 10.5f);
            _btcMeta.enableWordWrapping = true;
            var metaElement = _btcMeta.gameObject.AddComponent<LayoutElement>();
            metaElement.preferredHeight = 44f;
            metaElement.minHeight = 24f;
            metaElement.flexibleHeight = 0f;

            EditorUIHelpers.BuildSeparator(parent);
            EditorUIHelpers.BuildSectionHeader(parent, "Puente al juego");

            AddHint(parent,
                "Sembrar lee el ultimo cierre UNA VEZ, lo convierte en semilla y lo escribe en el " +
                "save. Nada vuelve a consultar el feed mientras juegas: por eso la partida sigue " +
                "siendo reproducible desde dos enteros, sigue funcionando sin red, y el jugador no " +
                "puede mover sus propios precios con un proxy o cambiando el reloj del sistema.");

            EditorUIHelpers.MakeButton(parent, "SEMBRAR MERCADO CON ESTE CIERRE", SeedFromBitcoin, 30f, 11f);

            RefreshBitcoinReadout();
            if (BitcoinPriceService.Enabled) Btc.Fetch(_timeframe);
        }

        private void AddTimeframe(Transform parent, BitcoinTimeframe tf, string label)
        {
            var button = EditorUIHelpers.MakeButton(parent, label, () =>
            {
                _timeframe = tf;
                RebuildBody();
            }, 28f, 11f);
            UIButton.SetTint(button, _timeframe == tf ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL);
        }

        /// <summary>
        /// The chart rect and its stroke.
        ///
        /// <para><c>raycastTarget = false</c> on the graphic: it fills a large rect inside a
        /// scroll view, and a graphic that answers raycasts eats the drag that scrolls the
        /// panel — the author would find the list simply refusing to move over the chart, with
        /// nothing on screen suggesting why.</para>
        /// </summary>
        private void BuildChart(Transform parent)
        {
            var frame = EditorUIHelpers.CreateUI("ChartFrame", parent);
            var frameElement = frame.AddComponent<LayoutElement>();
            frameElement.preferredHeight = CHART_H;
            frameElement.minHeight = CHART_H;
            frameElement.flexibleHeight = 0f;

            var bg = frame.AddComponent<Image>();
            bg.color = UITheme.BG_SURFACE;
            bg.raycastTarget = false;

            var chartGo = EditorUIHelpers.CreateUI("Chart", frame.transform);
            _chart = chartGo.AddComponent<SparklineGraphic>();
            _chart.color = UITheme.ACCENT;
            _chart.raycastTarget = false;
            EditorUIHelpers.StretchFill(chartGo);
        }

        private void ToggleFeed()
        {
            bool now = !BitcoinPriceService.Enabled;
            BitcoinPriceService.Enabled = now;
            if (_btcToggleButton != null)
            {
                var text = _btcToggleButton.GetComponentInChildren<TextMeshProUGUI>();
                if (text != null) text.text = now ? "FEED ON" : "FEED OFF";
                UIButton.SetTint(_btcToggleButton, now ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL);
            }

            SetStatus(now
                ? "Feed activado. Se consultara api.binance.com al abrir esta pestana."
                : "Feed desactivado. No se hara ninguna peticion de red.");

            if (now) Btc.Fetch(_timeframe, force: true);
            else RebuildBody();
        }

        private void RefreshFeed()
        {
            if (!BitcoinPriceService.Enabled)
            {
                SetStatus("El feed esta desactivado. Activalo primero.");
                return;
            }
            Btc.Fetch(_timeframe, force: true);
            RefreshBitcoinReadout();
        }

        /// <summary>
        /// Polls the fetch while the tab is open.
        ///
        /// <para>Polling rather than a callback because the request completes on a
        /// continuation that may land in any frame, and a callback into UI that the author may
        /// have just torn down by switching tabs is how a destroyed-object exception arrives
        /// seconds after its cause. A poll reads whatever is there when the panel is there.</para>
        /// </summary>
        private void TickBitcoinPanel()
        {
            if (_tab != Tab.Bitcoin || _chart == null) return;

            _btcPollClock += Time.unscaledDeltaTime;
            if (_btcPollClock < BTC_POLL_SECONDS) return;
            _btcPollClock = 0f;

            RefreshBitcoinReadout();
        }

        private void RefreshBitcoinReadout()
        {
            if (_chart == null) return;

            var series = BitcoinPriceService.Enabled ? Btc.Cached(_timeframe) : null;

            if (series == null || series.IsEmpty)
            {
                _chart.SetValues(Array.Empty<float>());
                if (_btcPrice != null) _btcPrice.text = "—";
                if (_btcMeta != null)
                {
                    _btcMeta.text = !BitcoinPriceService.Enabled
                        ? "Feed desactivado."
                        : (Btc.IsFetching(_timeframe)
                            ? "Consultando..."
                            : (string.IsNullOrEmpty(Btc.LastError)
                                ? "Sin datos todavia."
                                : $"<color=#c05a5a>Sin datos: {Btc.LastError}</color>"));
                }
                return;
            }

            _chart.SetValues(series.NormalisedCloses());

            double change = series.ChangePercent;
            // Green up, red down, and the SIGN is printed as well as coloured — a colour alone
            // is unreadable to a colour-blind author and unreadable in a screenshot.
            string colour = change >= 0d ? "#4c9a5a" : "#c05a5a";
            if (_btcPrice != null)
            {
                _btcPrice.text =
                    $"${series.LastClose.ToString("N0", CultureInfo.InvariantCulture)}   " +
                    $"<color={colour}>{(change >= 0d ? "+" : "")}{change.ToString("0.00", CultureInfo.InvariantCulture)}%</color>";
            }

            if (_btcMeta != null)
            {
                series.Extent(out double low, out double high);
                var age = series.AgeUtc;
                // The AGE is always shown. A cached chart with no timestamp is the worst
                // failure mode this panel has: it looks live, and an author making a decision
                // off it has no way to know it is from the last time the machine had a
                // connection.
                string ageText = age.TotalMinutes < 1d
                    ? "hace segundos"
                    : (age.TotalHours < 1d
                        ? $"hace {(int)age.TotalMinutes} min"
                        : $"hace {age.TotalHours:0.#} h");

                _btcMeta.text =
                    $"{series.Candles.Count} velas · {TimeframeLabel(series.Timeframe)} · " +
                    $"min ${low.ToString("N0", CultureInfo.InvariantCulture)} / " +
                    $"max ${high.ToString("N0", CultureInfo.InvariantCulture)}\n" +
                    $"{series.Source}, {ageText}" +
                    (Btc.IsFetching(_timeframe) ? " · actualizando..." : string.Empty);
            }
        }

        private static string TimeframeLabel(BitcoinTimeframe tf)
        {
            switch (tf)
            {
                case BitcoinTimeframe.Hour1: return "1 hora";
                case BitcoinTimeframe.Hour4: return "4 horas";
                default: return "diario";
            }
        }

        /// <summary>
        /// The one and only path from a real-world price into the game.
        ///
        /// <para>Reads the last close ONCE, quantises it to whole units so two fetches seconds
        /// apart give the same answer, hashes it into a seed and hands that to
        /// <c>MarketService.SetSeed</c>, which writes it to the save. Nothing re-reads the
        /// feed. Every property the cycle is built on survives: the run stays replayable from
        /// two integers, a bug report stays reproducible, the game still works offline, and a
        /// player cannot move their own prices with a proxy or a system clock.</para>
        /// </summary>
        private void SeedFromBitcoin()
        {
            var series = BitcoinPriceService.Enabled ? Btc.Cached(_timeframe) : null;
            if (series == null || series.IsEmpty)
            {
                SetStatus("No hay cierre que sembrar. Activa el feed y pulsa ACTUALIZAR.");
                return;
            }
            if (!MarketService.HasInstance)
            {
                SetStatus("No hay MarketService en la escena.");
                return;
            }

            int seed = series.SeedFromLastClose();
            if (seed == 0)
            {
                SetStatus("El cierre no da una semilla valida.");
                return;
            }

            MarketService.Instance.SetSeed(seed, $"btc:{TimeframeLabel(series.Timeframe)}");
            RefreshDerivedLabels();
            SetStatus($"Mercado sembrado desde el cierre ${series.LastClose:N0} " +
                      $"(semilla {seed}). Escrito en el save; el feed no se vuelve a leer.");
        }
    }
}
