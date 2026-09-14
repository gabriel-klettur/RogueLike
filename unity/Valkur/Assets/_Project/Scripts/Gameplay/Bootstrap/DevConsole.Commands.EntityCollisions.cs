using System.Text;
using Valkur.Gameplay.Spells.Debugging;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>colisiones</c> command: the entity-collider overlay and a text dump of what the
    /// last spell collision reached. The companion of <c>areas</c> - the two together say where
    /// a spell swept, what it was compared against, and which point decided it.
    ///
    /// Registered from <c>DevConsole.cs::RegisterDefaults()</c>.
    /// </summary>
    public partial class DevConsole
    {
        private void RegisterEntityCollisionCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name     = "colisiones",
                Aliases  = new[] { "colliders" },
                Usage    = "colisiones [on|off|lista]",
                Help     = "dibuja los colliders de las entidades y congela los que toco el ultimo hechizo",
                Category = "spells",
                // Log(...) and args[1]: the handler receives the command NAME in args[0] and
                // discards whatever the lambda returns.
                Handler  = args => Log(CmdEntityCollisions(args))
            });
        }

        private string CmdEntityCollisions(string[] args)
        {
            string sub = args != null && args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;

            switch (sub)
            {
                case "on":
                case "si":
                    EntityCollisionDebug.Enabled = true;
                    return "Colisiones ON. Lo que toque un hechizo se queda hasta la siguiente colision.";

                case "off":
                case "no":
                    EntityCollisionDebug.Enabled = false;
                    return "Colisiones OFF.";

                case "lista":
                case "list":
                    return DumpEntityCollisions();

                case "":
                    return EntityCollisionDebug.Enabled
                        ? "Colisiones ON. " + DescribeLastCollision()
                        : "Colisiones OFF. 'colisiones on' para encenderlas.";

                default:
                    return "Uso: colisiones [on|off|lista]";
            }
        }

        private static string DescribeLastCollision()
        {
            var contacts = EntityCollisionDebug.Current;
            if (contacts.Count == 0) return "Sin colision todavia.";
            int hits = 0;
            for (int i = 0; i < contacts.Count; i++)
                if (contacts[i].State == EntityContactState.Hit) hits++;
            return "'" + EntityCollisionDebug.SpellKey + "' por " + EntityCollisionDebug.CasterName +
                   ": " + contacts.Count + " entidades, " + hits + " golpeadas.";
        }

        private static string DumpEntityCollisions()
        {
            if (!EntityCollisionDebug.Enabled) return "Colisiones OFF. 'colisiones on' primero.";

            var contacts = EntityCollisionDebug.Current;
            if (contacts.Count == 0) return "Sin colision todavia: lanza un hechizo sobre algo.";

            var sb = new StringBuilder();
            sb.Append(DescribeLastCollision()).Append('\n');
            for (int i = 0; i < contacts.Count; i++)
            {
                var c = contacts[i];
                sb.Append("  ").Append(c.EntityName).Append("  ")
                  .Append(c.State == EntityContactState.Hit
                      ? "GOLPE -" + c.Damage + " x" + c.HitCount
                      : "tocado, sin dano");
                sb.Append("  colliders=").Append(c.Loops.Count);
                if (c.HasTestPoint)
                    sb.Append("  punto=").Append(Fmt(c.TestPoint))
                      .Append(c.TestPointAccepted ? " dentro" : " FUERA");
                if (i < contacts.Count - 1) sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
