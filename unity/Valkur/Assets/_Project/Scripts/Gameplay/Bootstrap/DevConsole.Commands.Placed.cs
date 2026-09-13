using System.Globalization;
using Valkur.Gameplay.Entities;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>placed</c> probe: every hand-placed entity on this map, what the author wrote for it,
    /// and what this run has done to it.
    ///
    /// <para>It exists because the three things it reports live in three places and each hides the
    /// others. The FILE says a knight stands in the lobby; the SCENE says nothing is there; the SAVE
    /// says the run killed it an hour ago. Without a probe "is this placement deleted, dead, waiting
    /// to respawn, or failing to spawn" is a question with no answer short of reading JSON.</para>
    ///
    /// <para><c>placed revive</c> is the one mutation, and it touches only run state — it never
    /// writes the map file. Authoring stays in the Entities editor, where it is undoable.</para>
    /// </summary>
    public partial class DevConsole
    {
        private void RegisterPlacedEntityCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name = "placed",
                Usage = "placed [revive <id-fragment>|revive all]",
                Help = "list hand-placed entities with their state this run; revive stands defeated ones back up",
                Category = "world",
                Handler = args => Log(CmdPlaced(args))
            });
        }

        private static string CmdPlaced(string[] args)
        {
            var service = PlacedEntityService.Instance;
            if (service == null)
                return "[placed] no PlacedEntityService in the scene (the boot sequence creates it).";
            if (!service.IsLoaded)
                return "[placed] the service exists but no map is loaded (world swap or interior in progress).";

            // args[0] is the command name; the first real argument is args[1].
            string verb = args != null && args.Length > 1 ? args[1].ToLowerInvariant() : null;
            if (verb == "revive")
            {
                string target = args.Length > 2 ? args[2] : null;
                if (string.IsNullOrEmpty(target))
                    return "[placed] usage: placed revive <id-fragment> | placed revive all";
                if (target == "all")
                    return $"[placed] revived {service.ReviveAll()} placement(s).";

                foreach (var record in service.Records)
                {
                    if (record.Id.IndexOf(target, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    return service.Revive(record.Id)
                        ? $"[placed] revived {record.MonsterKey} ({record.Id})."
                        : $"[placed] {record.MonsterKey} ({record.Id}) is already standing or cannot spawn.";
                }
                return $"[placed] no placement id contains '{target}'.";
            }

            var sb = new System.Text.StringBuilder();
            sb.Append("[placed] ").Append(service.Records.Count).Append(" placement(s), ")
              .Append(service.RunState.Count).Append(" killed this run, ")
              .Append(service.UnresolvedRecords.Count).Append(" unresolved")
              .Append(service.IsDirty ? ", unsaved edit pending" : "").Append('\n');

            double now = PlacedEntityRunState.UnixNow();
            foreach (var record in service.Records)
            {
                var status = service.StatusOf(record.Id, out double at);
                string state = status == PlacedEntityStatus.Respawning
                    ? $"respawns in {System.Math.Max(0d, at - now).ToString("0", CultureInfo.InvariantCulture)} s"
                    : status.ToString();

                sb.Append("  ").Append(Short(record.Id)).Append("  ")
                  .Append(record.MonsterKey.PadRight(20)).Append(' ')
                  .Append(record.Zone).Append(" [").Append(record.TileCol).Append(',').Append(record.TileRow).Append("]  ")
                  .Append(record.RespawnSeconds > 0f
                      ? "respawn " + record.RespawnSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s"
                      : "no respawn")
                  .Append("  ").Append(state).Append('\n');
            }

            foreach (var record in service.UnresolvedRecords)
                sb.Append("  ").Append(Short(record.Id)).Append("  ").Append(record.MonsterKey)
                  .Append(" in '").Append(record.Zone).Append("'  UNRESOLVED (unknown key or zone; kept in the file)\n");

            return sb.ToString().TrimEnd('\n');
        }

        private static string Short(string id)
            => string.IsNullOrEmpty(id) ? "?" : (id.Length > 8 ? id.Substring(0, 8) : id);
    }
}
