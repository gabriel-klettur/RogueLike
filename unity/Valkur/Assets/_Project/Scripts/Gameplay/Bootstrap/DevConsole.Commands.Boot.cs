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
                Usage    = "boot [all|fallos|weights|recalibrar]",
                Help     = "desglose del arranque: etapas, coste por etapa y fallos",
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

                case "recalibrar":
                case "reset":
                    BootTimeline.ClearWeights();
                    return "Perfil de pesos borrado. El proximo arranque medira desde cero.";

                default:
                    return BootTimeline.Report();
            }
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
