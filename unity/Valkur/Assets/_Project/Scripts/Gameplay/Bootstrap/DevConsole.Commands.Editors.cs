using System.Linq;
using System.Text;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay.Editors.General;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Console commands that answer "how do I open an editor" and "what is this key bound to".
    ///
    /// <para>WHY THEY EXIST. The fourteen editor F-keys were retired — every runtime editor is
    /// reached from the General Editor on Escape — and the failure mode of that change is
    /// SILENT: somebody who knows F8 presses it, nothing happens, and nothing on screen says
    /// why. A key that does nothing is indistinguishable from a broken build. These are the
    /// cheapest surface that answers it without inventing a HUD element.</para>
    /// </summary>
    public partial class DevConsole
    {
        private void RegisterEditorCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name = "editors",
                Usage = "editors",
                Help = "list every runtime editor and how to open it (Escape -> General Editor)",
                Category = "editors",
                Handler = _ => Log(CmdListEditors())
            });

            RegisterCommand(new ConsoleCommand
            {
                Name = "binding",
                Aliases = new[] { "bindings" },
                Usage = "binding [texto]",
                Help = "show what each action is bound to; filter by name or key",
                Category = "editors",
                Handler = args => Log(CmdShowBindings(args))
            });
        }

        private string CmdListEditors()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Los editores se abren desde ESCAPE -> Editor general.");
            sb.AppendLine("Las F1-F12 se retiraron: eran la fuente de todos los conflictos de teclas.");
            sb.AppendLine("Puedes reasignarlas desde ESC -> Controls si las quieres de vuelta.");
            sb.AppendLine();

            if (!GameEditorManager.HasInstance)
            {
                sb.AppendLine("(GameEditorManager no esta vivo — ¿estas en el menu principal?)");
                return sb.ToString();
            }

            var names = GameEditorManager.Instance.RegisteredEditors
                .Where(e => e != null && !string.IsNullOrEmpty(e.EditorName))
                .Select(e => e.EditorName)
                .OrderBy(n => n, System.StringComparer.Ordinal)
                .ToList();

            sb.AppendLine($"Editores registrados ({names.Count}):");
            foreach (var n in names)
            {
                var context = InputContexts.ForEditor(n);
                int tools = InputActionCatalog.All.Count(d =>
                    string.Equals(d.OwnerEditor, n, System.StringComparison.OrdinalIgnoreCase));
                sb.AppendLine($"  {n,-20} contexto {context}   herramientas propias: {tools}");
            }
            return sb.ToString();
        }

        private string CmdShowBindings(string[] args)
        {
            string filter = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "";
            var svc = InputService.Instance;
            if (svc?.Asset == null) return "InputService no esta listo.";

            var sb = new StringBuilder();
            int shown = 0;

            foreach (var d in InputActionCatalog.All)
            {
                var map = svc.Asset.FindActionMap(d.Map, throwIfNotFound: false);
                var action = map?.FindAction(d.Action, throwIfNotFound: false);
                string chip = action == null ? "?" : InputBindingResolver.PrimaryLabel(action);
                if (string.IsNullOrEmpty(chip)) chip = "sin asignar";

                if (filter.Length > 0 &&
                    !d.Id.ToLowerInvariant().Contains(filter) &&
                    !d.DisplayName.ToLowerInvariant().Contains(filter) &&
                    !chip.ToLowerInvariant().Contains(filter))
                    continue;

                sb.AppendLine($"  {d.Id,-34} {chip,-16} {d.DisplayName}");
                shown++;
            }

            if (shown == 0) return $"Ninguna accion coincide con '{filter}'.";
            sb.AppendLine();
            sb.AppendLine($"{shown} accion(es). Contexto vivo: {InputContexts.Current}. " +
                          "Reasignar: ESC -> Controls.");
            return sb.ToString();
        }
    }
}
