using System.Globalization;
using UnityEngine;
using Valkur.Data.WorldGen;
using Valkur.Gameplay.World.Generation;

namespace Valkur.Gameplay
{
    /// <summary>
    /// <c>seedworld</c>: the live Seed World from the console. Reports what the streamer is doing
    /// (which slot, how many zones painted, how long a zone takes) and starts a NEW world per run —
    /// <c>seedworld nueva [semilla]</c> builds a live slot from the default settings and walks the
    /// player into it, which is the "a new world every game" half of phase 5 until a menu offers it.
    /// </summary>
    public partial class DevConsole
    {
        private void RegisterSeedWorldCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name     = "seedworld",
                Aliases  = new[] { "sw" },
                Usage    = "seedworld [nueva [semilla]]",
                Help     = "estado del mundo en vivo, o empieza uno nuevo con esa semilla (aleatoria si falta)",
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
                    return DescribeSeedWorldStreamer();

                case "nueva":
                case "new":
                {
                    int seed;
                    if (args.Length > 2)
                    {
                        string text = string.Join(" ", args, 2, args.Length - 2);
                        if (!WorldSeed.TryParse(text, out seed)) seed = WorldSeed.Hash(text);
                    }
                    else seed = WorldSeed.NewRandom(new System.Random(System.Environment.TickCount));

                    var settings = new WorldGenSettings { seed = seed };
                    string slot = "partida_" + ((uint)seed).ToString(CultureInfo.InvariantCulture);
                    var outcome = SeedWorldLauncher.BuildAndLoad(settings, slot, live: true);
                    return SeedWorldLauncher.Describe(outcome);
                }

                default:
                    return "Uso: seedworld [nueva [semilla]]";
            }
        }

        private static string DescribeSeedWorldStreamer()
        {
            var st = SeedWorldLiveStreamer.Instance;
            if (st == null) return "No hay streamer de Seed World en la escena.";
            if (st.World == null)
                return $"Slot '{st.ActiveSlot}': no es un mundo en vivo ({st.LastOpenError ?? "sin abrir"}).";

            var plan = st.World.Plan;
            var stats = st.World.Stats;
            return $"Slot '{st.ActiveSlot}' EN VIVO, semilla {plan.Settings.seed}: {plan.ZonesX}x{plan.ZonesY} zonas, " +
                   $"{st.LoadedZoneCount} pintadas. Generadas {st.ZonesGenerated}, leidas de disco {st.ZonesFromDisk}, " +
                   $"descargadas {st.ZonesUnloaded}. Ultima zona {st.LastZoneMs} ms, peor {st.WorstZoneMs} ms. " +
                   $"Cortes sin transicion {stats.HardCuts}, tiles sin sprite {stats.MissingTiles}.";
        }
    }
}
