using System;
using System.Text;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat.Death;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>death</c> command family.
    ///
    /// <para><b>Why a probe was needed at all.</b> The death flow has six moving parts that can
    /// each fail while the others look fine — the binder, the altar registry, the phase machine,
    /// the two trails, the rescue clock and the litter — and every one of them fails SILENTLY.
    /// The 2026-09-07 audit could only diagnose it by reflecting into five classes through
    /// <c>execute_code</c>; the question "is there an altar in this world" had no answer anybody
    /// could ask from inside the game. Same argument the <c>faces</c> and <c>journal</c> commands
    /// make about the chat subsystem.</para>
    ///
    /// <para>Registered from <c>DevConsole.cs::RegisterDefaults()</c> under the "death" category
    /// and reachable through <see cref="DevConsole.Execute"/>, so a PlayMode test or an agent
    /// driving <c>execute_code</c> can drive the whole flow with no Game view.</para>
    /// </summary>
    public partial class DevConsole
    {
        private void RegisterDeathCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name = "death",
                Usage = "death",
                Help = "report the death flow: phase, altares, rastros, rescate, botin en el suelo",
                Category = "death",
                Handler = _ => CmdDeathReport()
            });

            RegisterCommand(new ConsoleCommand
            {
                Name = "altars",
                Usage = "altars",
                Help = "list every registered resurrection altar and its distance from the player",
                Category = "death",
                Handler = _ => CmdAltars()
            });

            RegisterCommand(new ConsoleCommand
            {
                Name = "rescue",
                Usage = "rescue",
                Help = "fire the death safety net now instead of waiting out its clock",
                Category = "death",
                Handler = _ => CmdRescue()
            });

            RegisterCommand(new ConsoleCommand
            {
                Name = "altar",
                Usage = "altar [on|off] [cerca|base|centro|arriba] [radio]",
                Help = "make the NEAREST building an altar (or not), and say where the spirit must stand",
                Category = "death",
                Handler = CmdAltar
            });

            RegisterCommand(new ConsoleCommand
            {
                Name = "corpseloot",
                Usage = "corpseloot [clear]",
                Help = "how many drops the last death left on the ground; 'clear' sweeps them",
                Category = "death",
                Handler = CmdCorpseLoot
            });
        }

        private void CmdDeathReport()
        {
            var controller = ServiceLocator.Get<DeathSequenceController>();
            var tuning = DeathTuning.Active;
            var sb = new StringBuilder();

            sb.AppendLine(controller == null
                ? "phase          : (no hay DeathSequenceController registrado)"
                : $"phase          : {controller.CurrentPhase}  (ultimo revivir: {controller.LastReviveKind})");

            if (controller != null && controller.CurrentPhase == DeathSequenceController.Phase.Spirit)
            {
                float eta = controller.RescueEta;
                sb.AppendLine($"espiritu       : {controller.SpiritElapsed:0.0} s, " +
                              $"rescate en {(float.IsPositiveInfinity(eta) ? "nunca" : eta.ToString("0.0") + " s")}");
                sb.AppendLine($"cadaver en     : {controller.LastDeathPosition}");
            }

            sb.AppendLine($"altares        : {ResurrectionAltarRegistry.Count} registrados, " +
                          $"{(ResurrectionAltarRegistry.AnyUsable ? "al menos uno usable" : "NINGUNO usable")}");

            var binder = UnityEngine.Object.FindObjectOfType<ResurrectionZoneAutoBinder>();
            sb.AppendLine(binder == null
                ? "vinculador     : ausente"
                : $"vinculador     : {(binder.IsSearching ? "buscando" : "terminado")}, " +
                  $"{binder.BoundCount} vinculados, plantillas [{string.Join(",", tuning.altarTemplateIds ?? new int[0])}]" +
                  (binder.SearchExhaustedWithNoAltar ? "  <-- se rindio sin encontrar ninguno" : ""));

            var path = ServiceLocator.Get<SpiritAltarPathHighlighter>();
            sb.AppendLine(path == null
                ? "rastros        : (no hay SpiritAltarPathHighlighter)"
                : $"rastros        : altar {path.AltarTrailLength} baldosas, cadaver {path.CorpseTrailLength}" +
                  (string.IsNullOrEmpty(path.AltarTrailBlockedReason) ? "" : $"  ({path.AltarTrailBlockedReason})"));

            sb.AppendLine($"botin caido    : {DeathLitter.Count} objetos de la muerte anterior");
            sb.AppendLine($"rescate        : modo {tuning.rescueMode}, {Mathf.RoundToInt(tuning.rescueHpFraction * 100f)}% de vida, " +
                          $"sin altar a los {tuning.rescueDelayWithoutAltar:0} s, limite {tuning.spiritTimeLimitSeconds:0} s");
            sb.AppendLine($"espiritu       : x{tuning.spiritSpeedMultiplier:0.00} velocidad, " +
                          $"{(tuning.spiritPassesThroughWalls ? "atraviesa muros" : "solido")}, " +
                          $"{(tuning.spiritIsTargetable ? "atacable" : "intangible")}");
            sb.AppendLine($"coste          : {Mathf.RoundToInt(tuning.xpLossFraction * 100f)}% XP del nivel, " +
                          $"inventario {(tuning.dropInventory ? "cae" : "se conserva")}, " +
                          $"monedas {(tuning.dropCoins ? $"cae {Mathf.RoundToInt(tuning.coinLossFraction * 100f)}%" : "se conservan")}, " +
                          $"persistencia {(tuning.persistDeathState ? "ON" : "OFF")}");

            Log(sb.ToString().TrimEnd());
        }

        private void CmdAltars()
        {
            var altars = ResurrectionAltarRegistry.Snapshot();
            if (altars.Count == 0)
            {
                Log("No hay ningun altar registrado. Abre ESC > Muerte > Altares para elegir la " +
                    "plantilla, o coloca un edificio de una plantilla ya listada.");
                return;
            }

            var player = EntityRegistry.PlayerTransform;
            Vector3 from = player != null ? player.position : Vector3.zero;

            var sb = new StringBuilder();
            sb.AppendLine($"{altars.Count} altar(es):");
            for (int i = 0; i < altars.Count; i++)
            {
                var a = altars[i];
                if (a == null) continue;
                var tpl = a.Building != null ? a.Building.Template : null;
                int templateId = tpl != null ? tpl.templateId : -1;
                sb.AppendLine($"  [{i}] plantilla {templateId} en {a.AnchorPoint} " +
                              $"({Vector2.Distance(from, a.AnchorPoint):0.0} u) — " +
                              $"{ResurrectionZoneGeometry.Describe(a.Anchor)}" +
                              (a.Anchor == ResurrectionAnchor.Proximity ? $", radio {a.Radius:0.##} u" : "") +
                              $"{(a.isActiveAndEnabled ? "" : "  (inactivo)")}");
            }
            Log(sb.ToString().TrimEnd());
        }

        private void CmdRescue()
        {
            var controller = ServiceLocator.Get<DeathSequenceController>();
            if (controller == null) { Log("No hay DeathSequenceController."); return; }
            if (controller.CurrentPhase != DeathSequenceController.Phase.Spirit)
            {
                Log($"El rescate solo actua en forma espiritu (fase actual: {controller.CurrentPhase}).");
                return;
            }
            controller.Rescue("pedido desde la consola");
            Log("Rescate lanzado.");
        }

        /// <summary>
        /// Turn the nearest building into an altar, or off, and pick WHERE on it the spirit has to
        /// stand.
        ///
        /// <para>The fastest authoring path there is: stand next to the thing and say what it is.
        /// The alternative — finding a template id, opening a tuning asset and typing the number —
        /// is what the 2026-09-07 audit found had gone wrong, because nobody can check a number
        /// against a building they are looking at.</para>
        ///
        /// <para>It writes the TEMPLATE, so it changes every placement of that art, and it says so
        /// with the count. That is the same scope <c>hasDoor</c> has and the same warning the
        /// Buildings editor's door panel prints.</para>
        /// </summary>
        private void CmdAltar(string[] parts)
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) { Log("No hay jugador: no se desde donde medir."); return; }

            var loader = UnityEngine.Object.FindObjectOfType<Valkur.Gameplay.World.BuildingLoader>();
            if (loader == null) { Log("No hay BuildingLoader en la escena."); return; }

            Valkur.Gameplay.World.BuildingObject nearest = null;
            float best = float.PositiveInfinity;
            var spawned = loader.SpawnedBuildings;
            for (int i = 0; i < spawned.Count; i++)
            {
                var b = spawned[i];
                if (b == null || b.Template == null) continue;
                Vector2 at = b.TryGetWorldRect(out Rect r) ? r.center : (Vector2)b.transform.position;
                float d = Vector2.Distance(player.position, at);
                if (d < best) { best = d; nearest = b; }
            }
            if (nearest == null) { Log("No hay ningun edificio colocado cerca."); return; }

            var t = nearest.Template;

            // No arguments: REPORT rather than toggle. A command that flips shipped catalogue data
            // on a bare invocation is one an author runs by accident while looking for its syntax.
            if (parts.Length < 2)
            {
                var report = new StringBuilder();
                report.AppendLine($"El edificio mas cercano ({best:0.0} u) es la plantilla " +
                                  $"{t.templateId} ({t.assetPath}).");

                string via = t.isResurrectionAltar
                    ? ""
                    : DeathTuning.Active.IsAltarTemplate(t.templateId)
                        ? "  (por la lista legacy de DeathTuning)"
                        : "";
                report.AppendLine($"  altar   : {(ResurrectionAltarRegistry.IsAltar(t) ? "SI" : "no")}{via}");
                report.AppendLine($"  donde   : {ResurrectionZoneGeometry.Describe(t.resurrectionAnchor)}");
                report.AppendLine($"  radio   : {t.resurrectionRadius:0.##} u (solo lo usa CERCA)");
                report.AppendLine($"  solido  : {(t.solid ? "SI - base/centro/arriba pueden ser inalcanzables" : "no")}");
                report.Append("Usa: altar on [cerca|base|centro|arriba] [radio]");

                Log(report.ToString());
                return;
            }

            bool turnOn = !string.Equals(parts[1], "off", StringComparison.OrdinalIgnoreCase);
            t.isResurrectionAltar = turnOn;

            if (turnOn && parts.Length >= 3 && TryParseAnchor(parts[2], out var anchorMode))
                t.resurrectionAnchor = anchorMode;

            if (turnOn && parts.Length >= 4 &&
                float.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float radius))
                t.resurrectionRadius = Mathf.Clamp(radius, 0.25f, 8f);

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(t);
#endif
            // Rebind BOTH ways: turning it on has to bind every placement of that art now rather
            // than on the next world load, and turning it off has to remove the ones already bound
            // or the altar goes on reviving people after the author switched it off.
            ResurrectionAltarRegistry.Rebind(loader);
            UnityEngine.Object.FindObjectOfType<ResurrectionZoneAutoBinder>()?.Rearm();

            int placed = 0;
            for (int i = 0; i < spawned.Count; i++)
                if (spawned[i] != null && spawned[i].Template == t) placed++;

            Log(turnOn
                ? $"Plantilla {t.templateId} ({t.assetPath}) es ahora altar, {ResurrectionZoneGeometry.Describe(t.resurrectionAnchor)}. " +
                  $"Afecta a las {placed} colocacion(es) de esa arte. Altares en el mundo: {ResurrectionAltarRegistry.Count}."
                : $"Plantilla {t.templateId} ({t.assetPath}) ya no es altar. " +
                  $"Altares en el mundo: {ResurrectionAltarRegistry.Count}.");
        }

        private static bool TryParseAnchor(string raw, out ResurrectionAnchor anchor)
        {
            switch ((raw ?? "").ToLowerInvariant())
            {
                case "cerca": case "proximity": case "near": anchor = ResurrectionAnchor.Proximity; return true;
                case "base": case "pie":                     anchor = ResurrectionAnchor.Base;      return true;
                case "centro": case "center":                anchor = ResurrectionAnchor.Center;    return true;
                case "arriba": case "top":                   anchor = ResurrectionAnchor.Top;       return true;
                default: anchor = ResurrectionAnchor.Proximity; return false;
            }
        }

        private void CmdCorpseLoot(string[] parts)
        {
            if (parts.Length >= 2 && string.Equals(parts[1], "clear", StringComparison.OrdinalIgnoreCase))
            {
                int removed = DeathLitter.ClearAll();
                Log($"Retirados {removed} objeto(s) de la muerte anterior.");
                return;
            }
            Log($"{DeathLitter.Count} objeto(s) de la muerte anterior siguen en el suelo. " +
                "'corpseloot clear' los retira.");
        }
    }
}
