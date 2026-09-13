using System.Text;
using Valkur.Gameplay.Spells.Debugging;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>areas</c> command: the spell impact-area overlay, and a text dump of the last
    /// cast's geometry.
    ///
    /// <para>The dump is not a convenience. The overlay answers "is the red circle where the
    /// particles are", which needs eyes on a screen; this answers "what exactly did that cast
    /// sweep, in world units", which is the half that can be read from a PlayMode test or from
    /// <c>execute_code</c> without anybody looking at the Game view. Every probe in this console
    /// exists for that same reason - see <c>faces</c>, <c>journal</c> and <c>spawners</c>.</para>
    ///
    /// Registered from <c>DevConsole.cs::RegisterDefaults()</c>.
    /// </summary>
    public partial class DevConsole
    {
        private void RegisterSpellAreaCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name     = "areas",
                Aliases  = new[] { "spellareas" },
                Usage    = "areas [on|off|lista]",
                Help     = "dibuja las areas de impacto, origen y alcance del ultimo hechizo lanzado",
                Category = "spells",
                // Log(...) and args[1]: the handler is an Action that receives the command NAME
                // in args[0] and discards whatever the lambda returns.
                Handler  = args => Log(CmdSpellAreas(args))
            });
        }

        private string CmdSpellAreas(string[] args)
        {
            string sub = args != null && args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;

            switch (sub)
            {
                case "on":
                case "si":
                    SpellDebugAreas.Enabled = true;
                    return "Areas de hechizo ON. Lanza uno: el dibujo se queda hasta el siguiente lanzamiento.";

                case "off":
                case "no":
                    SpellDebugAreas.Enabled = false;
                    return "Areas de hechizo OFF.";

                case "lista":
                case "list":
                    return DumpSpellAreas();

                case "":
                    return SpellDebugAreas.Enabled
                        ? "Areas ON. Ultimo lanzamiento: " + DescribeLastCast()
                        : "Areas OFF. 'areas on' para encenderlas.";

                default:
                    return "Uso: areas [on|off|lista]";
            }
        }

        private static string DescribeLastCast()
        {
            if (string.IsNullOrEmpty(SpellDebugAreas.SpellKey)) return "ninguno todavia.";
            // "y efectos vivos" is not padding. A mine placed two casts ago goes on querying its
            // trigger every frame and keeps pushing shapes into the CURRENT record, which is the
            // right behaviour -- a live area is a real area -- but calling the whole list "the
            // last cast" would be a lie about half of it.
            return "'" + SpellDebugAreas.SpellKey + "' por " + SpellDebugAreas.CasterName +
                   " (y efectos vivos), " + SpellDebugAreas.Current.Count + " formas" +
                   (SpellDebugAreas.Dropped > 0 ? " (+" + SpellDebugAreas.Dropped + " descartadas)" : "") + ".";
        }

        private static string DumpSpellAreas()
        {
            if (!SpellDebugAreas.Enabled) return "Areas OFF. 'areas on' primero.";

            var shapes = SpellDebugAreas.Current;
            if (shapes.Count == 0) return "Sin formas todavia: lanza un hechizo.";

            var sb = new StringBuilder();
            sb.Append("Ultimo lanzamiento: ").Append(DescribeLastCast()).Append('\n');
            for (int i = 0; i < shapes.Count; i++)
            {
                var s = shapes[i];
                sb.Append("  ").Append(s.Role).Append(' ').Append(s.Kind).Append("  ");
                switch (s.Kind)
                {
                    case SpellDebugKind.Point:
                        sb.Append(Fmt(s.A));
                        break;
                    case SpellDebugKind.Circle:
                        sb.Append(Fmt(s.A)).Append(" r=").Append(s.Radius.ToString("0.###"));
                        break;
                    case SpellDebugKind.Sector:
                        sb.Append(Fmt(s.A)).Append(" r=").Append(s.Radius.ToString("0.###"))
                          .Append(" arco=").Append(s.Angle.ToString("0.#"));
                        break;
                    case SpellDebugKind.Segment:
                        sb.Append(Fmt(s.A)).Append(" -> ").Append(Fmt(s.B))
                          .Append(" semiancho=").Append(s.Radius.ToString("0.###"));
                        break;
                    case SpellDebugKind.Rect:
                        sb.Append(Fmt(s.A)).Append(" ").Append(s.Size.x.ToString("0.###"))
                          .Append(" x ").Append(s.Size.y.ToString("0.###"))
                          .Append(" giro=").Append(s.Angle.ToString("0.#"));
                        break;
                }
                if (!string.IsNullOrEmpty(s.Label)) sb.Append("   \"").Append(s.Label).Append('"');
                if (i < shapes.Count - 1) sb.Append('\n');
            }
            return sb.ToString();
        }

        private static string Fmt(UnityEngine.Vector2 v)
        {
            return "(" + v.x.ToString("0.##") + ", " + v.y.ToString("0.##") + ")";
        }
    }
}
