using Valkur.Core;
using Valkur.Core.Services;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>debughud</c> command: the debug HUD's level, its bug report, and a fresh start
    /// for its measurements.
    ///
    /// <para>The overlay lives in <c>Valkur.UI</c>, which this assembly may not reference, so
    /// everything goes through <see cref="IDebugOverlayService"/>. The command exists for the
    /// same reason every other probe here does: <c>DevConsole.Execute</c> is reachable from
    /// PlayMode tests and from <c>execute_code</c>, so the overlay can be driven and read without
    /// anybody pressing F1 in the Game view.</para>
    ///
    /// Registered from <c>DevConsole.cs::RegisterDefaults()</c>.
    /// </summary>
    public partial class DevConsole
    {
        private void RegisterDebugHudCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name     = "debughud",
                Usage    = "debughud [0|1|2|3|informe|copiar|reiniciar]",
                Help     = "HUD de depuracion (F1): nivel, informe para un bug, medidas desde cero",
                Category = "hud",
                // Log(...) and args[1]: the handler is an Action that receives the command NAME
                // in args[0] and discards whatever the lambda returns.
                Handler  = args => Log(CmdDebugHud(args))
            });
        }

        private string CmdDebugHud(string[] args)
        {
            var hud = ServiceLocator.Get<IDebugOverlayService>();
            if (hud == null) return "No hay HUD de depuracion en esta escena.";

            string sub = args != null && args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
            switch (sub)
            {
                case "":
                    return "Nivel " + hud.Level + " de " + hud.MaxLevel +
                           " (0 oculto, 1 chip, 2 panel, 3 panel y anotaciones en el mundo).";

                case "informe":
                case "report":
                    return hud.BuildReport();

                case "copiar":
                case "copy":
                    hud.CopyReport();
                    return "Informe copiado al portapapeles.";

                case "reiniciar":
                case "reset":
                    var perf = PerformanceMonitor.Instance;
                    if (perf == null) return "No hay monitor de rendimiento en esta escena.";
                    perf.ResetSession();
                    return "Medidas, tirones y cuenta de la consola a cero.";

                default:
                    if (!int.TryParse(sub, out int level)) return "Uso: debughud [0|1|2|3|informe|copiar|reiniciar]";
                    hud.SetLevel(level);
                    return "Nivel " + hud.Level + (hud.Level < level ? " (este build no permite mas)." : ".");
            }
        }
    }
}
