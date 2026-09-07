using System.Text;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Enemies;
using Valkur.Gameplay.FSM;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>ai</c> command family: read and draw what the hostile AI is actually doing.
    ///
    /// <para>It exists for the same reason <c>faces</c> and <c>journal</c> do. Perception,
    /// pursuit and the standoff are decided from numbers that live in three places — a
    /// <c>MonsterDefinition</c>, a set in <c>sets.json</c>, and defaults in
    /// <c>FSMTuning</c> — and until now the only thing the game would tell you about any of
    /// it was a state name in the debug HUD. "Why did that one not see me" was answered by
    /// opening an asset and doing arithmetic.</para>
    /// </summary>
    public partial class DevConsole
    {
        private void RegisterAICommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name = "ai",
                Usage = "ai [on|off]",
                Help = "report every live monster's state, target, distance and perception knobs",
                Category = "debug",
                Handler = CmdAi
            });
        }

        private void CmdAi(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                string mode = args[0].ToLowerInvariant();
                if (mode == "on" || mode == "off")
                {
                    bool on = mode == "on";
                    int drawn = AIDebugOverlay.SetEnabled(on);
                    Log(on
                        ? $"[ai] overlay ON — aggro ring, leash ring, view cone and current target for {drawn} monster(s)."
                        : "[ai] overlay OFF.");
                    return;
                }

                Log("[ai] usage: ai [on|off]");
                return;
            }

            var monsters = EntityRegistry.Monsters;
            if (monsters == null || monsters.Count == 0)
            {
                Log("[ai] no monsters registered.");
                return;
            }

            var player = EntityRegistry.Player;
            var sb = new StringBuilder();
            sb.Append("[ai] ").Append(monsters.Count).Append(" monster(s)");
            sb.Append(AIDebugOverlay.IsOn ? " — overlay ON" : " — overlay off (`ai on`)").Append('\n');

            for (int i = 0; i < monsters.Count; i++)
            {
                var m = monsters[i];
                if (m == null) continue;

                var brain = m.GetComponent<FSMMonsterBrain>();
                var fsm = brain != null ? brain.FSM : null;
                if (fsm == null)
                {
                    sb.Append("  ").Append(m.name).Append(" — no brain\n");
                    continue;
                }

                var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
                var target = c?.Target(fsm);

                sb.Append("  ").Append(m.name)
                  .Append(" [").Append(brain.CurrentStateName).Append(']')
                  .Append(" t=").Append(fsm.TimeInCurrentState.ToString("0.0")).Append('s');

                if (target != null)
                {
                    float d = Vector2.Distance(m.transform.position, target.transform.position);
                    sb.Append(" -> ").Append(target == player ? "player" : target.name)
                      .Append(" @").Append(d.ToString("0.0"));
                    sb.Append(FSMPerception.HasLineOfSight(m, target) ? " (sees)" : " (blind)");
                }
                else
                {
                    sb.Append(" -> no target");
                }

                sb.Append("  aggro=").Append(fsm.GetContextFloat("aggro_range", 0f).ToString("0.#"));
                float fov = FSMTuning.FovDegrees(fsm);
                if (fov < 360f) sb.Append(" fov=").Append(fov.ToString("0"));
                float desired = FSMTuning.DesiredRange(fsm);
                if (desired > 0f) sb.Append(" standoff=").Append(desired.ToString("0.#"));
                if (FSMPerception.AggroSuppressed(fsm)) sb.Append(" REGROUPING");
                if (FSMAlert.IsPending(fsm)) sb.Append(" ALERTED");
                sb.Append('\n');
            }

            Log(sb.ToString().TrimEnd());
        }
    }
}
