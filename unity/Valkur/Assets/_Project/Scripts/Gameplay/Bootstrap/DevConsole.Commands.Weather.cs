using Valkur.Gameplay.World.Weather;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>weather</c> command family.
    ///
    /// The Time &amp; Weather panel (F2) can already set every level, so this is not a second
    /// UI — it is the programmatic seam. <c>DevConsole.Execute</c> is public, so a PlayMode
    /// test or an agent driving <c>execute_code</c> can put the world into a heavy wind-driven
    /// storm and take a measurement without anyone touching the Game view, which is the only
    /// way to verify a look that only exists while something is falling.
    ///
    /// Everything here is ZONE-SCOPED, like the panel: a bare command authors the zone the
    /// player is standing in. <c>weather in &lt;zone&gt; ...</c> reaches a zone from outside it,
    /// which the panel deliberately cannot do — the panel shows what you can see.
    ///
    /// Registered from <c>DevConsole.cs::RegisterDefaults()</c> under the "weather" category.
    /// </summary>
    public partial class DevConsole
    {
        private void RegisterWeatherCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name     = "weather",
                Aliases  = new[] { "wx" },
                Usage    = "weather [wind|rain|snow|storm|clear [all]] [off|light|medium|heavy]",
                Help     = "show or set this zone's weather; 'storm' = heavy rain + heavy wind",
                Category = "weather",
                Handler  = args => CmdWeather(args)
            });

            RegisterCommand(new ConsoleCommand
            {
                Name     = "weatherzones",
                Aliases  = new[] { "wxzones" },
                Usage    = "weatherzones",
                Help     = "list every zone that has weather authored, and what it is",
                Category = "weather",
                Handler  = _ => CmdWeatherZones()
            });

            RegisterCommand(new ConsoleCommand
            {
                Name     = "weatherin",
                Aliases  = new[] { "wxin" },
                Usage    = "weatherin <zone> <wind|rain|snow|clear> [off|light|medium|heavy]",
                Help     = "author a zone's weather without standing in it",
                Category = "weather",
                Handler  = args => CmdWeatherIn(args)
            });

            RegisterCommand(new ConsoleCommand
            {
                Name     = "lightning",
                Usage    = "lightning [on|off]",
                Help     = "fire a strike now, or toggle storm lightning",
                Category = "weather",
                Handler  = args => CmdLightning(args)
            });

            RegisterCommand(new ConsoleCommand
            {
                Name     = "snow",
                Usage    = "snow [0..1|on|off]",
                Help     = "report or set how much snow is LYING on the world (not the fall)",
                Category = "weather",
                Handler  = args => CmdSnowCover(args)
            });

            RegisterCommand(new ConsoleCommand
            {
                Name     = "wind",
                Usage    = "wind [flip]",
                Help     = "report the live wind field, or reverse its direction",
                Category = "weather",
                Handler  = args => CmdWind(args)
            });
        }

        // ── weather ──────────────────────────────────────────────────────────────────

        private void CmdWeather(string[] args)
        {
            var manager = WeatherManager.Instance;
            if (manager == null)
            {
                Log("[weather] No WeatherManager in the scene.");
                return;
            }

            if (args == null || args.Length < 2) { ReportWeather(manager); return; }

            string what = args[1].ToLowerInvariant();

            if (what == "clear" || what == "off")
            {
                bool everywhere = args.Length >= 3 && args[2].ToLowerInvariant() == "all";
                if (everywhere)
                {
                    manager.ClearEveryZone();
                    Log("[weather] Cleared in every zone.");
                    return;
                }
                if (!RequireZone(manager, "clear")) return;
                string zone = manager.ActiveZone;
                manager.ClearAll();
                Log($"[weather] Cleared in {zone}. ('weather clear all' for every zone.)");
                return;
            }

            if (what == "storm")
            {
                if (!RequireZone(manager, "set a storm")) return;
                manager.Set(WeatherType.Rain, WeatherIntensity.Heavy);
                manager.Set(WeatherType.Wind, WeatherIntensity.Heavy);
                manager.Set(WeatherType.Snow, WeatherIntensity.Off);
                Log($"[weather] Storm in {manager.ActiveZone} — heavy rain, heavy wind, lightning armed.");
                return;
            }

            if (!TryParseWeatherType(what, out var type))
            {
                Log($"[weather] Unknown weather '{what}'. Expected wind, rain, snow, storm or clear.");
                return;
            }

            if (!RequireZone(manager, $"set {type}")) return;

            // No level given: cycle, so repeating the command walks the levels the same way
            // clicking the panel row does.
            if (args.Length < 3)
            {
                var cycled = manager.Cycle(type);
                Log($"[weather] {type} -> {cycled.ToLabel()} in {manager.ActiveZone}.");
                return;
            }

            if (!TryParseIntensity(args[2].ToLowerInvariant(), out var level))
            {
                Log($"[weather] Unknown level '{args[2]}'. Expected off, light, medium or heavy.");
                return;
            }

            manager.Set(type, level);
            Log($"[weather] {type} -> {level.ToLabel()} in {manager.ActiveZone}.");
        }

        /// <summary>
        /// Refuse an authoring command that has nowhere to land, and say which of the two
        /// reasons it is. Both are recoverable by moving, and neither is an error the player
        /// can diagnose from silence.
        /// </summary>
        private bool RequireZone(WeatherManager manager, string action)
        {
            if (manager.IsIndoors)
            {
                Log($"[weather] Cannot {action} indoors — you are sheltered, and an interior is " +
                    "not in the zone database. Step outside.");
                return false;
            }
            if (!manager.HasActiveZone)
            {
                Log($"[weather] Cannot {action} — no zone detected yet.");
                return false;
            }
            return true;
        }

        private void ReportWeather(WeatherManager manager)
        {
            string where = manager.IsIndoors      ? "INDOORS (sheltered)"
                         : manager.HasActiveZone  ? manager.ActiveZone
                         : "no zone detected";

            Log($"[weather] Here: {where} — " +
                $"Wind {manager.LevelOf(WeatherType.Wind).ToLabel()}  " +
                $"Rain {manager.LevelOf(WeatherType.Rain).ToLabel()}  " +
                $"Snow {manager.LevelOf(WeatherType.Snow).ToLabel()}");
            Log($"[weather] Rendering: wind {manager.DensityOf(WeatherType.Wind):F2}  " +
                $"rain {manager.DensityOf(WeatherType.Rain):F2}  " +
                $"snow {manager.DensityOf(WeatherType.Snow):F2}");
            Log($"[weather] Wind field {WeatherWind.VelocityX:F2} u/s (gust {WeatherWind.Gust01:F2}), " +
                $"audio {(WeatherManager.AudioEnabled ? "on" : "off")}, " +
                $"lightning {(WeatherManager.LightningEnabled ? "on" : "off")}, " +
                $"snow cover {SnowAccumulation.Amount:F2}.");

            var zones = manager.ZonesWithWeather();
            Log(zones.Count == 0
                ? "[weather] No zone in the world has weather authored."
                : $"[weather] {zones.Count} zone(s) with weather — 'weatherzones' to list them.");
        }

        private void CmdWeatherZones()
        {
            var manager = WeatherManager.Instance;
            if (manager == null) { Log("[weather] No WeatherManager in the scene."); return; }

            var zones = manager.ZonesWithWeather();
            if (zones.Count == 0) { Log("[weather] No zone has weather authored."); return; }

            for (int i = 0; i < zones.Count; i++)
            {
                string zone = zones[i];
                string here = string.Equals(zone, manager.ActiveZone,
                                            System.StringComparison.OrdinalIgnoreCase) ? "  <- you are here" : "";
                Log($"[weather] {zone}: " +
                    $"wind {manager.LevelOfZone(zone, WeatherType.Wind).ToLabel()}  " +
                    $"rain {manager.LevelOfZone(zone, WeatherType.Rain).ToLabel()}  " +
                    $"snow {manager.LevelOfZone(zone, WeatherType.Snow).ToLabel()}{here}");
            }
        }

        private void CmdWeatherIn(string[] args)
        {
            var manager = WeatherManager.Instance;
            if (manager == null) { Log("[weather] No WeatherManager in the scene."); return; }

            if (args == null || args.Length < 3)
            {
                Log("[weather] Usage: weatherin <zone> <wind|rain|snow|clear> [off|light|medium|heavy]");
                return;
            }

            string zone = args[1];
            string what = args[2].ToLowerInvariant();

            if (what == "clear" || what == "off")
            {
                manager.ClearZone(zone);
                Log($"[weather] Cleared in {zone}.");
                return;
            }

            if (!TryParseWeatherType(what, out var type))
            {
                Log($"[weather] Unknown weather '{what}'. Expected wind, rain, snow or clear.");
                return;
            }

            var level = WeatherIntensity.Medium;
            if (args.Length >= 4 && !TryParseIntensity(args[3].ToLowerInvariant(), out level))
            {
                Log($"[weather] Unknown level '{args[3]}'. Expected off, light, medium or heavy.");
                return;
            }

            manager.SetInZone(zone, type, level);
            Log($"[weather] {type} -> {level.ToLabel()} in {zone}." +
                (string.Equals(zone, manager.ActiveZone, System.StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : " You are not in that zone, so nothing changes on screen yet."));
        }

        // ── lightning / snow cover / wind field ──────────────────────────────────────

        private void CmdLightning(string[] args)
        {
            if (args != null && args.Length >= 2)
            {
                string mode = args[1].ToLowerInvariant();
                if (mode == "on" || mode == "off")
                {
                    WeatherManager.LightningEnabled = mode == "on";
                    Log($"[weather] Storm lightning {mode}.");
                    return;
                }
            }

            WeatherGrade.Strike();
            Log("[weather] Strike.");
        }

        private void CmdWind(string[] args)
        {
            if (args != null && args.Length >= 2 && args[1].ToLowerInvariant() == "flip")
            {
                float sign = WeatherWind.FlipDirection();
                Log($"[weather] Wind now blows {(sign < 0f ? "left" : "right")}.");
                return;
            }

            Log($"[weather] Wind {WeatherWind.VelocityX:F2} u/s " +
                $"(base {WeatherWind.BaseSpeed:F2}, gust {WeatherWind.Gust01:F2}, " +
                $"weather contribution {WeatherWind.WeatherSpeed:F2}).");
        }

        /// <summary>
        /// Set the lying-snow cover directly.
        ///
        /// Global rather than per zone, unlike everything else here: the cover is one shader
        /// scalar multiplied by the world-space accumulation buffer, and both are properties of
        /// the ground the camera is over rather than of an authored region. The accumulation
        /// takes a minute and a half of Heavy snow to reach full cover, which is right for play
        /// and useless for authoring: checking how a roof line reads at half cover should not
        /// mean standing in a blizzard first.
        /// </summary>
        private void CmdSnowCover(string[] args)
        {
            if (args == null || args.Length < 2)
            {
                Log($"[weather] Snow cover {SnowAccumulation.Amount:F2} " +
                    $"(accumulation {(WeatherManager.AccumulationEnabled ? "on" : "off")}).");
                return;
            }

            string arg = args[1].ToLowerInvariant();
            if (arg == "on" || arg == "off")
            {
                WeatherManager.AccumulationEnabled = arg == "on";
                Log($"[weather] Snow accumulation {arg}." +
                    (arg == "off" ? " The world melts back to bare." : string.Empty));
                return;
            }

            if (!float.TryParse(arg, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float amount))
            {
                Log($"[weather] '{args[1]}' is not a number. Usage: snow [0..1|on|off]");
                return;
            }

            SnowAccumulation.SetAmount(amount);
            Log($"[weather] Snow cover set to {SnowAccumulation.Amount:F2}. " +
                "It will melt from here unless snow is falling.");
        }

        // ── parsing ──────────────────────────────────────────────────────────────────

        private static bool TryParseWeatherType(string s, out WeatherType type)
        {
            switch (s)
            {
                case "wind": type = WeatherType.Wind; return true;
                case "rain": type = WeatherType.Rain; return true;
                case "snow": type = WeatherType.Snow; return true;
                default:     type = WeatherType.Wind; return false;
            }
        }

        private static bool TryParseIntensity(string s, out WeatherIntensity level)
        {
            switch (s)
            {
                case "off":    level = WeatherIntensity.Off;    return true;
                case "light":  level = WeatherIntensity.Light;  return true;
                case "medium": level = WeatherIntensity.Medium; return true;
                case "heavy":  level = WeatherIntensity.Heavy;  return true;
                default:       level = WeatherIntensity.Off;    return false;
            }
        }
    }
}
