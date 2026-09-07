using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core.Economy;
using Valkur.Data;
using Valkur.Gameplay.NPC;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.Economy
{
    public partial class EconomyRuntimeEditor
    {
        private readonly List<TextMeshProUGUI> _derivedLabels = new List<TextMeshProUGUI>();
        private readonly List<Func<string>> _derivedSources = new List<Func<string>>();

        /// <summary>
        /// Tear the body down and rebuild it for the active tab.
        ///
        /// <para><c>DestroyImmediate</c> in Edit Mode: <c>Object.Destroy</c> is an outright
        /// ERROR there, not a warning, and seven Controls-editor tests went red on the log line
        /// alone with every assertion passing. Any editor path that destroys UI needs this
        /// branch.</para>
        /// </summary>
        private void RebuildBody()
        {
            for (int i = 0; i < _bodyObjects.Count; i++)
            {
                if (_bodyObjects[i] == null) continue;
                if (Application.isPlaying) Destroy(_bodyObjects[i]);
                else DestroyImmediate(_bodyObjects[i]);
            }
            _bodyObjects.Clear();
            _fieldResync.Clear();
            _derivedLabels.Clear();
            _derivedSources.Clear();

            if (_bodyContent2 == null) return;

            // Everything built from here is parented to a holder rather than to the content
            // directly, so the teardown above is one destroy per section instead of a walk
            // over live children while the layout group is reading them.
            var holder = EditorUIHelpers.CreateUI($"Body_{_tab}", _bodyContent2);
            var layout = holder.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 3f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            holder.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            _bodyObjects.Add(holder);

            switch (_tab)
            {
                case Tab.Market: BuildMarketTab(holder.transform); break;
                case Tab.Faucet: BuildFaucetTab(holder.transform); break;
                case Tab.Vendors: BuildVendorsTab(holder.transform); break;
                case Tab.Bitcoin: BuildBitcoinTab(holder.transform); break;
            }

            RefreshDerivedLabels();
        }

        /// <summary>
        /// A line whose text is COMPUTED rather than typed — the current phase, what a sample
        /// monster now pays, the resolved price of a sample item. These are what turn a
        /// number into a decision: "amplitude 0.25" means nothing, "hoy pagas x1.22" does.
        /// </summary>
        private void AddDerived(Transform parent, Func<string> source)
        {
            var label = EditorUIHelpers.AddLabel(parent, source(), 11f);
            label.enableWordWrapping = true;
            var element = label.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 30f;
            element.minHeight = 16f;
            element.flexibleHeight = 0f;

            _derivedLabels.Add(label);
            _derivedSources.Add(source);
        }

        private void RefreshDerivedLabels()
        {
            for (int i = 0; i < _derivedLabels.Count && i < _derivedSources.Count; i++)
            {
                if (_derivedLabels[i] == null) continue;
                _derivedLabels[i].text = _derivedSources[i]();
            }
        }

        // ── Tab: the cycle ──────────────────────────────────────────────────

        private void BuildMarketTab(Transform parent)
        {
            EditorUIHelpers.BuildSectionHeader(parent, "Estado de este save");
            AddDerived(parent, DescribeMarket);

            var stateBar = MakeRow(parent, "MarketStateBar", 28f);
            EditorUIHelpers.MakeButton(stateBar.transform, "+1 DIA", () => AdvanceMarketDay(1), 28f, 11f);
            EditorUIHelpers.MakeButton(stateBar.transform, "+7 DIAS", () => AdvanceMarketDay(7), 28f, 11f);
            EditorUIHelpers.MakeButton(stateBar.transform, "CICLO ON/OFF", ToggleCycle, 28f, 11f);

            AddHint(parent, "La semilla y el dia viven en el SAVE, no en un asset: cambiarlos " +
                            "afecta a esta partida y a ninguna otra. El reloj solo avanza — un " +
                            "reloj rebobinable permite farmear el pico recargando.");

            // 0 is the "nobody set a seed" sentinel and SetSeed refuses it. Without excluding
            // it here the history would record "Semilla: 4242 -> 0", the box would snap back,
            // and the panel would have reported a change it could not make — the same
            // authored-and-inert shape as the stance chips that offered eight toggles of which
            // six had no reader. A refusal the author can see beats a label that lies.
            AddIntField(parent, "Semilla",
                "0 no es una semilla: es el centinela de 'nadie la ha puesto'. Usa cualquier " +
                "otro entero, o siembra desde la pestana de Bitcoin.",
                () => MarketService.HasInstance ? MarketService.Instance.Seed : 0,
                v =>
                {
                    if (v == 0 || !MarketService.HasInstance) return;
                    MarketService.Instance.SetSeed(v, "editor");
                },
                int.MinValue, int.MaxValue);

            EditorUIHelpers.BuildSeparator(parent);
            EditorUIHelpers.BuildSectionHeader(parent, "Forma del ciclo (compartida)");

            AddFloatField(parent, "Amplitud",
                "Cuanto mueve un precio la fase mas extrema. Por debajo de 0.10 nadie nota que " +
                "el ciclo existe y la capa es decoracion; por encima de 0.35 un valle deja de " +
                "significar 'mala semana para vender' y pasa a significar 'hoy no juegues'.",
                () => _tuning.amplitude, v => _tuning.amplitude = v, 0.02f, MarketCycle.AmplitudeCeiling);

            AddIntField(parent, "Dias minimos por ciclo",
                "Un ciclo mas corto que ~4 dias da menos de un dia por fase y el jugador no " +
                "llega a actuar sobre una antes de que pase.",
                () => _tuning.minCycleDays, v => _tuning.minCycleDays = v, 4, 40);

            AddIntField(parent, "Dias maximos por ciclo",
                "Lo que importa es el RANGO, no cada extremo: un periodo fijo es un calendario, " +
                "y un jugador que puede contar dias no esta leyendo un mercado.",
                () => _tuning.maxCycleDays, v => _tuning.maxCycleDays = v, 4, 60);

            EditorUIHelpers.BuildSeparator(parent);
            EditorUIHelpers.BuildSectionHeader(parent, "Proximos dias");
            AddDerived(parent, DescribeForecast);
        }

        private string DescribeMarket()
        {
            if (!MarketService.HasInstance) return "No hay MarketService en la escena.";
            var m = MarketService.Instance;
            var now = m.Current;
            return $"{now.Phase}  ·  dia {m.Day}  ·  dia {now.DayInCycle + 1} de un ciclo de {now.CycleLength}\n" +
                   $"pagas x{now.BuyMultiplier:0.00}   recibes x{now.SellMultiplier:0.00}   " +
                   $"ciclo {(m.CycleEnabled ? "ON" : "OFF")}   semilla {m.Seed} ({m.SeedSource})";
        }

        private string DescribeForecast()
        {
            if (!MarketService.HasInstance) return string.Empty;
            var m = MarketService.Instance;
            var shape = m.Shape;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 10; i++)
            {
                var c = MarketCycle.Resolve(m.Seed, m.Day + i, shape);
                sb.Append(i == 0 ? "hoy" : $"+{i}")
                  .Append(' ').Append(c.Phase)
                  .Append(" x").Append(c.BuyMultiplier.ToString("0.00", CultureInfo.InvariantCulture))
                  .Append(i == 9 ? "" : "   ");
            }
            return sb.ToString();
        }

        private void AdvanceMarketDay(int days)
        {
            if (!MarketService.HasInstance) { SetStatus("No hay MarketService."); return; }
            MarketService.Instance.AdvanceDay(days);
            RefreshDerivedLabels();
            SetStatus($"Mercado avanzado {days} dia(s). Esto vive en el save.");
        }

        private void ToggleCycle()
        {
            if (!MarketService.HasInstance) { SetStatus("No hay MarketService."); return; }
            var m = MarketService.Instance;
            m.SetCycleEnabled(!m.CycleEnabled);
            RefreshDerivedLabels();
            SetStatus($"Ciclo {(m.CycleEnabled ? "activado" : "desactivado")}.");
        }

        // ── Tab: the faucet ─────────────────────────────────────────────────

        private void BuildFaucetTab(Transform parent)
        {
            EditorUIHelpers.BuildSectionHeader(parent, "Heuristica de recompensa");
            AddHint(parent, "Se usa cuando el monstruo no autoriza coinReward. Un monstruo con " +
                            "coinReward -1 no paga nada y con >0 manda su numero.");

            AddIntField(parent, "HP por moneda",
                "Mas alto = mundo mas pobre.",
                () => _tuning.coinPerHp, v => _tuning.coinPerHp = v, 1, 500);

            AddIntField(parent, "Poder por moneda",
                "Separado del divisor de HP porque un lanzador de cristal y un saco de vida no " +
                "deberian valer lo mismo.",
                () => _tuning.coinPerPower, v => _tuning.coinPerPower = v, 1, 100);

            AddFloatField(parent, "Varianza minima",
                "La varianza existe para que un pago se lea como botin y no como un sueldo. " +
                "Con ambos extremos en 1.0 es un sueldo.",
                () => _tuning.coinVarianceMin, v => _tuning.coinVarianceMin = v, 0.1f, 1f);

            AddFloatField(parent, "Varianza maxima", null,
                () => _tuning.coinVarianceMax, v => _tuning.coinVarianceMax = v, 1f, 3f);

            EditorUIHelpers.BuildSeparator(parent);
            EditorUIHelpers.BuildSectionHeader(parent, "Lo que pagaria el bestiario");
            AddDerived(parent, DescribeFaucet);

            EditorUIHelpers.BuildSeparator(parent);
            EditorUIHelpers.BuildSectionHeader(parent, "Bolsa inicial");
            AddIntField(parent, "Monedas de respaldo",
                "Solo para una clase autorizada antes de que PlayerDefinition.startingCoins " +
                "existiera. La definicion manda cuando es positiva.",
                () => _tuning.fallbackStartingCoins, v => _tuning.fallbackStartingCoins = v, 0, 100000);
        }

        /// <summary>
        /// What the current divisors pay for a handful of REAL monsters.
        ///
        /// <para>This is the whole point of the tab: "hp/40 + power/4" is not a number an
        /// author can judge, and "un barbol paga 4, el jefe 52" is. The samples are computed
        /// through <c>DeathDropSystem.ComputeCoinReward</c> rather than reimplemented, so the
        /// readout cannot drift from what the game actually pays.</para>
        /// </summary>
        private string DescribeFaucet()
        {
            var sb = new System.Text.StringBuilder();
            string[] keys = { "barbol", "knight_red", "dark_vampire", "barbol_boss" };
            int shown = 0;

            foreach (var def in ShippedMonsters())
            {
                if (Array.IndexOf(keys, def.monsterKey) < 0) continue;
                if (shown > 0) sb.Append("   ");
                sb.Append(def.monsterKey).Append(' ')
                  .Append(DeathDropSystem.ComputeCoinReward(def, 0));
                shown++;
            }
            return shown == 0 ? "Sin catalogo de monstruos cargado." : sb.ToString();
        }

        private static IEnumerable<MonsterDefinition> ShippedMonsters()
        {
#if UNITY_EDITOR
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:MonsterDefinition"))
            {
                var def = UnityEditor.AssetDatabase.LoadAssetAtPath<MonsterDefinition>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (def != null) yield return def;
            }
#else
            yield break;
#endif
        }

        // ── Tab: vendors ────────────────────────────────────────────────────

        private void BuildVendorsTab(Transform parent)
        {
            if (_vendors.Count == 0)
            {
                EditorUIHelpers.AddLabel(parent, "No hay VendorConfigDefinition cargados.", 11f);
                return;
            }

            EditorUIHelpers.BuildSectionHeader(parent, "Vendedor");

            var strip = EditorUIHelpers.CreateUI("VendorStrip", parent);
            var layout = strip.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 2f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            strip.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            for (int i = 0; i < _vendors.Count; i++)
            {
                int index = i;
                var cfg = _vendors[i];
                var button = EditorUIHelpers.MakeButton(strip.transform, cfg.vendorKey,
                    () => { _selectedVendor = index; RebuildBody(); }, ROW_H + 2f, 11f);
                UIButton.SetTint(button, index == _selectedVendor ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL);
            }

            var vendor = SelectedVendor;
            if (vendor == null) return;

            EditorUIHelpers.BuildSeparator(parent);
            EditorUIHelpers.BuildSectionHeader(parent, "Bolsa y reposicion");

            AddIntField(parent, "Monedas del vendedor",
                "0 = bolsa ilimitada, que es el comportamiento historico y tiene que serlo: una " +
                "clave ausente de un .asset ya enviado deserializa a 0, y leer eso como bolsa " +
                "VACIA haria que todos los vendedores se negaran a comprar en silencio.",
                () => vendor.coinFloat, v => vendor.coinFloat = v, 0, 1000000);

            AddFloatField(parent, "Segundos de reposicion",
                "De vacio a lleno. Se repone PROPORCIONALMENTE, asi que este numero significa " +
                "lo mismo en una tienda de 4 huecos y en una de 77. 0 = nunca se repone.",
                () => vendor.restockSeconds, v => vendor.restockSeconds = v, 0f, 7200f);

            EditorUIHelpers.BuildSeparator(parent);
            EditorUIHelpers.BuildSectionHeader(parent, "Margenes del grupo");

            var group = vendor.economyGroup;
            if (group == null)
            {
                EditorUIHelpers.AddLabel(parent,
                    "Sin grupo economico: sus margenes resuelven a 1 en silencio.\n" +
                    "Ejecuta Valkur > Economy > Seed Economy Groups.", 11f);
                return;
            }

            AddFloatField(parent, "Compra fuera de su oficio",
                "Lo que el jugador PAGA por algo que este personaje no trabaja.",
                () => group.defaultMargin.buyMultiplier,
                v => { var m = group.defaultMargin; m.buyMultiplier = v; group.defaultMargin = m; },
                0.1f, 3f);

            AddFloatField(parent, "Venta fuera de su oficio",
                "Lo que el jugador RECIBE por lo mismo. Debe quedar por debajo de la compra o " +
                "el vendedor es una imprenta de monedas.",
                () => group.defaultMargin.sellMultiplier,
                v => { var m = group.defaultMargin; m.sellMultiplier = v; group.defaultMargin = m; },
                0.1f, 3f);

            for (int i = 0; i < group.typeMargins.Count; i++)
            {
                int index = i;
                var entry = group.typeMargins[i];
                EditorUIHelpers.BuildSeparator(parent);
                EditorUIHelpers.AddLabel(parent, $"Especialidad: {entry.itemType}", 11f);

                AddFloatField(parent, $"  compra {entry.itemType}", null,
                    () => group.typeMargins[index].margin.buyMultiplier,
                    v =>
                    {
                        var e = group.typeMargins[index];
                        var m = e.margin; m.buyMultiplier = v; e.margin = m;
                        group.typeMargins[index] = e;
                    }, 0.1f, 3f);

                AddFloatField(parent, $"  venta {entry.itemType}", null,
                    () => group.typeMargins[index].margin.sellMultiplier,
                    v =>
                    {
                        var e = group.typeMargins[index];
                        var m = e.margin; m.sellMultiplier = v; e.margin = m;
                        group.typeMargins[index] = e;
                    }, 0.1f, 3f);
            }
        }
    }
}
