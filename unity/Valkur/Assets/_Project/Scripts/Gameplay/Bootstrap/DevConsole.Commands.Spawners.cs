using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spawners;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>spawners</c> probe.
    ///
    /// <para>It exists for the same reason <c>ai</c>, <c>journal</c>, <c>faces</c>,
    /// <c>market</c> and <c>stats</c> do: the spawner layer is written by an editor, driven by
    /// a state machine and persisted by a serializer, and each of those can fail in a way the
    /// other two hide. Without a probe, "did the migration run", "is that camp armed", "why is
    /// nothing coming out of it" and "did my roster edit actually stick" are not answerable
    /// without opening the editor and clicking each spawner in turn — and several of them are
    /// not answerable from the editor at all, because the panel shows the placement you picked
    /// and never the set.</para>
    ///
    /// <para>Read-only by design. Every mutation the spawner layer has belongs to the editor,
    /// where it is undoable and persisted; a console that could retune a camp would be a
    /// second authoring path writing the same file.</para>
    /// </summary>
    public partial class DevConsole
    {
        private void RegisterSpawnerCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name = "spawners",
                Usage = "spawners [id-or-preset]",
                Help = "list every placed spawner with its state, roster and live count; " +
                       "pass a fragment to see one in full",
                Category = "world",
                Handler = CmdSpawners
            });
        }

        private void CmdSpawners(string[] args)
        {
            var all = Object.FindObjectsOfType<SpawnerInstance>();
            if (all == null || all.Length == 0)
            {
                Log("[spawners] none in the scene. If the world is loaded, the instances file " +
                    "is empty or every row was refused — check the load warnings.");
                return;
            }

            // Stable order, so two runs of the command can be compared by eye. FindObjectsOfType
            // has no defined order and reshuffles as objects are created and destroyed, which
            // makes a diff between two invocations meaningless.
            var ordered = all.Where(s => s != null)
                             .OrderBy(s => s.Zone, System.StringComparer.OrdinalIgnoreCase)
                             .ThenBy(s => s.InstanceId, System.StringComparer.OrdinalIgnoreCase)
                             .ToList();

            // args[0] is the command name the user typed (possibly an alias); the first real
            // argument is args[1]. Reading args[0] would filter every listing by the word
            // "spawners" and quietly return nothing.
            string filter = args != null && args.Length > 1 ? args[1] : null;
            if (!string.IsNullOrEmpty(filter))
            {
                var matches = ordered.Where(s => Matches(s, filter)).ToList();
                if (matches.Count == 0)
                {
                    Log($"[spawners] nothing matches '{filter}'. {ordered.Count} placed in total.");
                    return;
                }
                foreach (var si in matches) LogDetail(si);
                return;
            }

            Log($"[spawners] {ordered.Count} placed");
            foreach (var si in ordered) Log("  " + Summarise(si));

            // The two aggregates worth having without asking for them: how many are actually
            // holding entities right now, and how many carry the despawn exemption. A world of
            // exempt monsters is a leak with no error, and `persistent` is now per-placement,
            // so it is easy to set and impossible to see.
            int live = ordered.Sum(s => s.ActiveEntityCount);
            int persistent = ordered.Count(s => s.Config != null && s.Config.persistent);
            Log($"[spawners] {live} entity(ies) currently held; {persistent} placement(s) " +
                "exempt from the despawn sweep.");
        }

        private static bool Matches(SpawnerInstance si, string fragment)
        {
            if (si == null) return false;
            if (Contains(si.InstanceId, fragment)) return true;
            if (si.Preset != null && Contains(si.Preset.templateId, fragment)) return true;
            return Contains(si.Zone, fragment);
        }

        private static bool Contains(string haystack, string needle)
            => !string.IsNullOrEmpty(haystack) &&
               haystack.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;

        private static string Summarise(SpawnerInstance si)
        {
            var c = si.Config;
            var pos = si.transform.position;
            string roster = c != null ? DescribeRoster(c) : "no config";
            return $"{si.InstanceId,-44} {si.Zone,-14} ({pos.x,6:F0},{pos.y,6:F0}) " +
                   $"{si.State,-12} w{si.CurrentWaveIndex} live={si.ActiveEntityCount}  {roster}";
        }

        private void LogDetail(SpawnerInstance si)
        {
            var c = si.Config;
            var pos = si.transform.position;

            Log($"[spawners] {si.InstanceId}");
            Log($"  preset      {(si.Preset != null ? si.Preset.templateId : "(deleted — the placement owns its config)")}");
            Log($"  zone/pos    {si.Zone} ({pos.x:F1}, {pos.y:F1})");

            if (c == null)
            {
                Log("  config      NONE — this placement is still schema v1 and is following " +
                    "its preset. Press Play in the Editor once to migrate it.");
                return;
            }

            Log($"  state       {si.State}  wave {si.CurrentWaveIndex}  live {si.ActiveEntityCount}");
            Log($"  trigger     {c.triggerType} r={c.triggerRadius:0.##} autoStart={c.autoStart} rearm={c.proximityRearms}");
            Log($"  policy      {c.spawnMode} cd={c.cooldownSeconds:0.##} betweenWaves={c.betweenWavesCooldownSeconds:0.##} " +
                $"advanceOn={c.advanceOn} maxActive={c.maxActive}");
            Log($"  restart     onDone={c.restartOnDone} cd={c.restartCooldownSeconds:0.##}  persistent={c.persistent}");
            Log($"  area        {c.spawnerShape} r={c.spawnRadius}");
            Log($"  difficulty  levelBonus={c.levelBonus} scaleWithPlayer={c.scaleWithPlayerLevel:0.##}");

            string brain = string.IsNullOrEmpty(c.fsmSetOverride) ? "(monster's own)" : c.fsmSetOverride;
            Log($"  brain       fsmSet={brain} leash={(c.defendLeashRadius > 0f ? c.defendLeashRadius.ToString("0.##") : "(monster's own)")}");

            var waves = c.waves;
            if (waves == null || waves.Count == 0)
            {
                Log("  roster      EMPTY — this spawner produces nothing. That is a live defect, " +
                    "not a state: it will sit in Active forever.");
                return;
            }

            for (int w = 0; w < waves.Count; w++)
            {
                var wave = waves[w];
                if (wave?.spawns == null || wave.spawns.Count == 0)
                {
                    Log($"  wave {w}      (empty)");
                    continue;
                }
                foreach (var e in wave.spawns)
                {
                    if (e == null) continue;
                    Log($"  wave {w}      {e.kind} {e.entityId} x{e.count} spread={e.spreadRadius:0.##}");
                }
            }
        }

        private static string DescribeRoster(SpawnerInstanceConfig c)
        {
            string primary = c.PrimaryEntityId();
            int total = c.TotalEntityCount();
            int waves = c.waves?.Count ?? 0;
            if (primary == null) return "EMPTY ROSTER";
            return total > 1 ? $"{primary} x{total} ({waves}w)" : $"{primary} ({waves}w)";
        }
    }
}
