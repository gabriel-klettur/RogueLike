using System.Globalization;
using System.Text;
using UnityEngine;
using Valkur.Data.WorldGen;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Generation;

namespace Valkur.Gameplay
{
    /// <summary>
    /// <c>seedworld</c>: the Seed World lab from the console. Seed World is kept apart from the game
    /// until it is refined, so building or entering a generated world needs the lab switched on
    /// (<see cref="SeedWorldLab"/>), every trip into one is a trip FROM Pepitoria with a return
    /// ticket (<see cref="WorldExcursion"/>), and <c>volver</c> always works — the switch that took a
    /// player somewhere must never be what strands them there.
    /// </summary>
    public partial class DevConsole
    {
        private void RegisterSeedWorldCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name     = "seedworld",
                Aliases  = new[] { "sw" },
                Usage    = "seedworld [lab on|off | nueva [semilla] | volver]",
                Help     = "laboratorio de mundos generados: estado, encender/apagar, mundo nuevo, volver a Pepitoria",
                Category = "world",
                // Log(...) and args[1]: the handler receives the command NAME in args[0] and
                // discards whatever the lambda returns.
                Handler  = args => Log(CmdSeedWorld(args))
            });
        }

        private string CmdSeedWorld(string[] args)
        {
            string sub = args != null && args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
            switch (sub)
            {
                case "":
                case "estado":
                    return DescribeSeedWorldLab();

                case "lab":
                {
                    string value = args.Length > 2 ? args[2].ToLowerInvariant() : string.Empty;
                    if (value == "on" || value == "si") SeedWorldLab.SetEnabled(true);
                    else if (value == "off" || value == "no") SeedWorldLab.SetEnabled(false);
                    else return "Uso: seedworld lab on|off";
                    return SeedWorldLab.Enabled
                        ? "Laboratorio Seed World ENCENDIDO en esta maquina."
                        : "Laboratorio Seed World APAGADO." + (WorldExcursion.IsAway
                            ? " Sigues fuera de Pepitoria: 'seedworld volver' te trae de vuelta."
                            : string.Empty);
                }

                case "nueva":
                case "new":
                {
                    if (!SeedWorldLab.Enabled) return SeedWorldLab.OffMessage;
                    int seed;
                    if (args.Length > 2)
                    {
                        string text = string.Join(" ", args, 2, args.Length - 2);
                        if (!WorldSeed.TryParse(text, out seed)) seed = WorldSeed.Hash(text);
                    }
                    else seed = WorldSeed.NewRandom(new System.Random(System.Environment.TickCount));

                    var settings = new WorldGenSettings { seed = seed };
                    var outcome = SeedWorldLauncher.BuildAndLoad(settings, SeedWorldNewGame.SlotFor(seed), live: true);
                    return SeedWorldLauncher.Describe(outcome);
                }

                case "volver":
                case "home":
                    return SeedWorldLauncher.ReturnHome();

                default:
                    return "Uso: seedworld [lab on|off | nueva [semilla] | volver]";
            }
        }

        private static string DescribeSeedWorldLab()
        {
            var sb = new StringBuilder();
            sb.Append("Laboratorio ").Append(SeedWorldLab.Enabled ? "ENCENDIDO" : "APAGADO").Append(". ");

            if (WorldExcursion.TryGetHome(out var home, out var zone))
                sb.Append("Fuera de Pepitoria, en '").Append(WorldExcursion.Destination)
                  .Append("'; la vuelta te deja en ").Append(string.IsNullOrEmpty(zone) ? "?" : zone)
                  .Append(" (").Append(home.x.ToString("0.#", CultureInfo.InvariantCulture)).Append(", ")
                  .Append(home.y.ToString("0.#", CultureInfo.InvariantCulture)).Append("). ");
            else
                sb.Append("En Pepitoria. ");

            var st = SeedWorldLiveStreamer.Instance;
            if (st == null) return sb.Append("Streamer sin crear (se crea al entrar en un mundo).").ToString();
            if (st.World == null)
                return sb.Append($"Slot '{st.ActiveSlot}': no es un mundo en vivo ({st.LastOpenError ?? "sin abrir"}).").ToString();

            var plan = st.World.Plan;
            var stats = st.World.Stats;
            return sb.Append($"Slot '{st.ActiveSlot}' EN VIVO, semilla {plan.Settings.seed}: {plan.ZonesX}x{plan.ZonesY} zonas, " +
                             $"{st.LoadedZoneCount} pintadas. Generadas {st.ZonesGenerated}, leidas de disco {st.ZonesFromDisk}, " +
                             $"descargadas {st.ZonesUnloaded}. Ultima zona {st.LastZoneMs} ms, peor {st.WorstZoneMs} ms. " +
                             $"Cortes sin transicion {stats.HardCuts}, tiles sin sprite {stats.MissingTiles}.").ToString();
        }
    }
}
