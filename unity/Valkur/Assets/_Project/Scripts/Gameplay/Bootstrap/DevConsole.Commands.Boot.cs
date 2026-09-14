using System.Text;
using Valkur.Core.Boot;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>boot</c> command: what the arranque did, in what order, and what it cost.
    ///
    /// <para><b>Why this exists.</b> Every other large subsystem here has a probe —
    /// <c>faces</c>, <c>journal</c>, <c>spawners</c>, <c>ai</c>, <c>market</c>, <c>death</c> —
    /// and the boot was the one that did not, which is exactly why it was the subsystem
    /// nobody could answer questions about. "Which stage costs?" had no answer without
    /// hand-instrumenting; "did anything fail?" had no answer at all, because a step that
    /// threw killed the coroutine in silence and the loading screen faded out on a
    /// fifteen-second timer as if nothing had happened. An audit scored it 2/10 on
    /// instrumentation for precisely that.</para>
    ///
    /// <para><c>boot weights</c> is the half worth knowing about. The bar is calibrated from
    /// the PREVIOUS boot's measured milliseconds, so a machine that has launched once shows a
    /// bar that moves at the speed that machine actually loads at. Clearing the profile is how
    /// you check what a fresh install sees.</para>
    ///
    /// Registered from <c>DevConsole.cs::RegisterDefaults()</c>.
    /// </summary>
    public partial class DevConsole
    {
        private void RegisterBootCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name     = "boot",
                Usage    = "boot [all|fallos|etapas|prevision|tendencia [n]|logs [abrir]|weights|recalibrar]",
                Help     = "desglose del arranque, prevision por etapas y tendencia historica",
                Category = "boot",
                // Log(...) and args[1]. The handler is an Action that receives the command NAME in
                // args[0] and discards the lambda's value, so this command used to print nothing
                // at all and read "boot" as its own subcommand — "boot all" never worked.
                Handler  = args => Log(CmdBoot(args))
            });
        }

        private string CmdBoot(string[] args)
        {
            string sub = args != null && args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;

            switch (sub)
            {
                case "all":
                case "todo":
                    return BootTimeline.Report(int.MaxValue);

                case "fallos":
                case "errors":
                    return ReportBootFailures();

                case "weights":
                case "pesos":
                    return BootTimeline.IsCalibrated
                        ? "La barra de carga esta calibrada con las medidas del arranque anterior. " +
                          "'boot recalibrar' las borra para ver lo que ve una instalacion nueva."
                        : "Sin calibrar: este arranque uso las estimaciones declaradas. " +
                          "El siguiente ya usara lo que se ha medido hoy.";

                case "etapas":
                case "prevision":
                case "plan":
                    return ReportBootPlan();

                case "tendencia":
                case "trend":
                {
                    int window = 10;
                    if (args.Length > 2 && int.TryParse(args[2], out int n) && n > 0) window = n;
                    return BootTrend.Report(BootRunLog.ReadPhaseRows(), BootRunLog.ReadStepRows(),
                                            UnityEngine.Application.isEditor, window);
                }

                case "logs":
                case "registro":
                {
                    string dir = BootRunLog.Directory;
                    if (args.Length > 2 && (args[2] == "abrir" || args[2] == "open"))
                    {
                        System.IO.Directory.CreateDirectory(dir);
                        UnityEngine.Application.OpenURL("file:///" + dir.Replace(System.IO.Path.DirectorySeparatorChar, '/'));
                    }
                    var nl = System.Environment.NewLine;
                    return "Registro de arranques: " + dir +
                           (BootTimeline.LastLogPath != null ? nl + "Ultimo informe: " + BootTimeline.LastLogPath : string.Empty) +
                           nl + "'boot logs abrir' abre la carpeta; 'boot tendencia' la resume.";
                }

                case "recalibrar":
                case "reset":
                    BootTimeline.ClearWeights();
                    return "Perfil de pesos borrado. El proximo arranque medira desde cero.";

                default:
                    return BootTimeline.Report();
            }
        }

        /// <summary>
        /// The etapas the bar is divided into, with what the history predicts for each. Before
        /// the boot this is the plan the next loading screen will draw; after it, the live one.
        /// </summary>
        private static string ReportBootPlan()
        {
            var screen = BootTimeline.HasRun
                ? BootTimeline.PredictScreenPlan().WithBoot(BootTimeline.Plan)
                : BootTimeline.PredictScreenPlan();
            var sb = new StringBuilder();
            sb.Append("Etapas de la barra de carga").Append(screen.IsTimed ? " (con prevision)" : " (sin prevision todavia)").AppendLine();
            var starts = new System.Collections.Generic.List<float>();
            screen.SegmentStarts(starts);
            for (int i = 0; i < screen.SegmentCount; i++)
            {
                float ms = i == 0 ? screen.PredictedSceneMs : screen.Boot.Segments[i - 1].PredictedMs;
                float share = screen.SegmentEnd(i) - starts[i];
                sb.Append("  ").Append((i + 1).ToString().PadLeft(2)).Append(". ")
                  .Append((share * 100f).ToString("F1").PadLeft(5)).Append("% de la barra  ")
                  .Append(ms > 0f ? (ms.ToString("F0") + " ms").PadLeft(9) : "        -")
                  .Append("  ").Append(screen.SegmentName(i)).AppendLine();
            }
            if (screen.TotalPredictedMs > 0f)
                sb.Append("Prevision total: ").Append((screen.TotalPredictedMs / 1000f).ToString("F2")).Append(" s");
            return sb.ToString().TrimEnd();
        }

        private string ReportBootFailures()
        {
            var failures = BootTimeline.Failures;
            if (!BootTimeline.HasRun)
                return "Esta sesion no ha arrancado la escena de juego todavia.";
            if (failures.Count == 0)
                return $"Arranque limpio: {BootTimeline.StepCount} etapas, ningun fallo.";

            var sb = new StringBuilder();
            sb.Append(failures.Count).Append(" fallo(s) durante el arranque:").AppendLine();
            foreach (var f in failures) sb.Append("  · ").Append(f).AppendLine();
            sb.Append("El mundo puede estar incompleto. Los detalles con pila estan en la consola de Unity.");
            return sb.ToString();
        }
    }
}
