using System.Text;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>progression</c> command family: read the player's resolved stats with their
    /// per-layer breakdown, grant either currency, learn a node, and respec.
    ///
    /// <para>These are the seam that makes the whole stat layer testable without a UI. A
    /// PlayMode test or an agent driving <c>execute_code</c> can grant ten skill points,
    /// buy a talent and assert that melee damage moved — which is the composition test
    /// CLAUDE.md keeps saying is the only kind that proves anything, after the spawner
    /// coordinate drift and the boomerang's borrowed <c>Projectile</c> both passed every
    /// test of their individual halves.</para>
    /// </summary>
    public partial class DevConsole
    {
        private void RegisterProgressionCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name = "stats",
                Usage = "stats [statName]",
                Help = "print every resolved player stat with its per-layer breakdown",
                Category = "progression",
                Handler = CmdStats,
            });

            RegisterCommand(new ConsoleCommand
            {
                Name = "skillpoints",
                Aliases = new[] { "sp" },
                Usage = "sp <amount>",
                Help = "grant skill points (talent tree currency)",
                Category = "progression",
                Handler = args => GrantPoints(args, arcane: false),
            });

            RegisterCommand(new ConsoleCommand
            {
                Name = "arcanepoints",
                Aliases = new[] { "ap" },
                Usage = "ap <amount>",
                Help = "grant arcane points (grimoire currency)",
                Category = "progression",
                Handler = args => GrantPoints(args, arcane: true),
            });

            RegisterCommand(new ConsoleCommand
            {
                Name = "learn",
                Usage = "learn <skillId | grimoireNodeId>",
                Help = "buy one rank of a talent, or learn a grimoire node",
                Category = "progression",
                Handler = CmdLearn,
            });

            RegisterCommand(new ConsoleCommand
            {
                Name = "respec",
                Usage = "respec [skills|spells|all]",
                Help = "refund spent points and forget what they bought",
                Category = "progression",
                Handler = CmdRespec,
            });

            RegisterCommand(new ConsoleCommand
            {
                Name = "grimoire",
                Usage = "grimoire [schoolKey]",
                Help = "list the schools, or one school's nodes and their status",
                Category = "progression",
                Handler = CmdGrimoire,
            });
        }

        private static PlayerProgression ResolveProgression()
        {
            var player = EntityRegistry.PlayerTransform;
            return player != null ? player.GetComponent<PlayerProgression>() : null;
        }

        private static int ResolvePlayerLevel()
        {
            var player = EntityRegistry.PlayerTransform;
            var xp = player != null ? player.GetComponent<Experience>() : null;
            return xp != null ? xp.Level : 1;
        }

        private void CmdStats(string[] args)
        {
            var player = EntityRegistry.PlayerTransform;
            var stats = player != null ? player.GetComponent<PlayerStats>() : null;
            if (stats == null) { Log("No PlayerStats on the player."); return; }

            var sb = new StringBuilder();
            foreach (var stat in StatCatalog.All)
            {
                if (args.Length > 1 &&
                    !StatCatalog.DisplayName(stat).Replace(" ", "")
                        .Equals(args[1].Replace(" ", ""), System.StringComparison.OrdinalIgnoreCase) &&
                    !stat.ToString().Equals(args[1], System.StringComparison.OrdinalIgnoreCase))
                    continue;

                sb.Append($"  {StatCatalog.DisplayName(stat),-20} {stats.Get(stat),8:0.###}");
                sb.Append($"   base {stats.GetBase(stat):0.##}");

                foreach (StatLayer layer in System.Enum.GetValues(typeof(StatLayer)))
                {
                    if (layer == StatLayer.Base) continue;
                    float c = stats.GetLayerContribution(stat, layer);
                    if (Mathf.Abs(c) < 0.005f) continue;
                    sb.Append($" {(c >= 0 ? "+" : "-")}{Mathf.Abs(c):0.##} {layer.ToString().ToLowerInvariant()}");
                }
                sb.Append('\n');
            }

            Log(sb.Length == 0 ? "No matching stat." : sb.ToString().TrimEnd());
        }

        private void GrantPoints(string[] args, bool arcane)
        {
            var progression = ResolveProgression();
            if (progression == null) { Log("No PlayerProgression on the player."); return; }

            int amount = 1;
            if (args.Length > 1 && !int.TryParse(args[1], out amount))
            {
                Log($"Usage: {(arcane ? "ap" : "sp")} <amount>");
                return;
            }

            if (arcane)
            {
                if (progression.Grimoire == null) { Log("No grimoire component."); return; }
                progression.Grimoire.AddPoints(amount);
                Log($"Granted {amount} arcane point(s). Now {progression.Grimoire.AvailablePoints}.");
            }
            else
            {
                if (progression.Skills == null) { Log("No skills component."); return; }
                progression.Skills.AddPoints(amount);
                Log($"Granted {amount} skill point(s). Now {progression.Skills.AvailablePoints}.");
            }
        }

        private void CmdLearn(string[] args)
        {
            if (args.Length < 2) { Log("Usage: learn <skillId | grimoireNodeId>"); return; }

            var progression = ResolveProgression();
            if (progression == null) { Log("No PlayerProgression on the player."); return; }

            string id = args[1];
            int level = ResolvePlayerLevel();

            var skills = progression.Skills;
            if (skills != null && skills.Tree != null && skills.Tree.TryGet(id, out var talent))
            {
                if (skills.TryLearn(talent, level, out string reason))
                    Log($"Learned '{talent.displayName}' rank {skills.RankOf(id)}.");
                else
                    Log($"Refused: {reason}");
                return;
            }

            var grimoire = progression.Grimoire;
            if (grimoire != null)
            {
                foreach (var tree in grimoire.Trees)
                {
                    if (tree == null || !tree.TryGet(id, out var node)) continue;

                    if (grimoire.TryLearn(tree, node, level, out string reason))
                        Log($"Learned '{node.ResolveDisplayName()}' from {tree.displayName}.");
                    else
                        Log($"Refused: {reason}");
                    return;
                }
            }

            Log($"No talent or grimoire node with id '{id}'.");
        }

        private void CmdRespec(string[] args)
        {
            var progression = ResolveProgression();
            if (progression == null) { Log("No PlayerProgression on the player."); return; }

            string what = args.Length > 1 ? args[1].ToLowerInvariant() : "all";

            if (what == "skills" || what == "all")
            {
                progression.Skills?.Respec();
                Log($"Talents refunded. {progression.Skills?.AvailablePoints ?? 0} skill point(s).");
            }
            if (what == "spells" || what == "grimoire" || what == "all")
            {
                progression.Grimoire?.Respec();
                Log($"Grimoire refunded. {progression.Grimoire?.AvailablePoints ?? 0} arcane point(s).");
            }
        }

        private void CmdGrimoire(string[] args)
        {
            var progression = ResolveProgression();
            var grimoire = progression != null ? progression.Grimoire : null;
            if (grimoire == null) { Log("No grimoire component."); return; }

            var sb = new StringBuilder();

            if (args.Length < 2)
            {
                sb.Append($"{grimoire.AvailablePoints} arcane point(s). Schools:\n");
                foreach (var tree in grimoire.Trees)
                {
                    if (tree == null) continue;
                    int known = 0;
                    foreach (var n in tree.Nodes)
                        if (n != null && grimoire.IsNodeLearned(n)) known++;

                    string affinity = tree.HasAffinity(grimoire.ClassKey) ? "affinity" : "off-affinity";
                    sb.Append($"  {tree.schoolKey,-12} {known}/{tree.Count,-3} {affinity}  {tree.displayName}\n");
                }
                Log(sb.ToString().TrimEnd());
                return;
            }

            var target = progression.Catalog != null ? progression.Catalog.GetSpellTree(args[1]) : null;
            if (target == null) { Log($"No school '{args[1]}'."); return; }

            int level = ResolvePlayerLevel();
            foreach (var node in target.Nodes)
            {
                if (node == null) continue;
                string status = grimoire.IsNodeLearned(node)
                    ? "known"
                    : grimoire.CanLearn(target, node, level, out string reason) ? "available" : reason;
                sb.Append($"  {node.nodeId,-28} {grimoire.ResolveCost(target, node)} AP  {status}\n");
            }
            Log(sb.ToString().TrimEnd());
        }
    }
}
