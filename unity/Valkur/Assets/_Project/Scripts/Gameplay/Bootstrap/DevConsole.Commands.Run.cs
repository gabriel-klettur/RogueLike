using System.Globalization;
using System.Text;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Player;
using Valkur.Gameplay.Skills;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>run</c> probe: what the feet are doing, and why.
    ///
    /// <para>The gait is decided by a physics step, drawn by an animator, paid for by an energy
    /// pool and taught by a skill, and each of those can fail in a way the others hide — a run
    /// that never starts looks exactly like a player who never walked far enough. This prints the
    /// live answer to all four at once, and lets the skill and the pool be set so the extremes of
    /// the curve can be felt without five hours of jogging.</para>
    ///
    /// <para>Remember the console contract: the handler receives the command NAME in
    /// <c>args[0]</c>, so the subcommand is <c>args[1]</c>.</para>
    /// </summary>
    public partial class DevConsole
    {
        private void RegisterRunCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name = "run",
                Usage = "run [skill <0-100> | energy <n|full|empty> | reset]",
                Help = "report walking/running state, energy and the Carrera skill; set skill or energy",
                Category = "player",
                Handler = args => Log(CmdRun(args))
            });

            RegisterCommand(new ConsoleCommand
            {
                Name = "autowalk",
                Usage = "autowalk <x> <y> <seconds> | autowalk stop",
                Help = "walk the player without the keyboard (to watch and capture walking and running)",
                Category = "player",
                Handler = args => Log(CmdAutowalk(args))
            });
        }

        private string CmdAutowalk(string[] args)
        {
            var player = GameObject.FindWithTag("Player");
            var controller = player != null ? player.GetComponent<PlayerController>() : null;
            if (controller == null) return "[autowalk] No player in the scene.";
            if (args == null || args.Length < 2 || args[1] == "stop")
            {
                controller.SetDebugMove(Vector2.zero, 0f);
                return "[autowalk] stopped.";
            }
            if (args.Length < 4 ||
                !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                !float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds))
                return "[autowalk] usage: autowalk <x> <y> <seconds> | autowalk stop";
            controller.SetDebugMove(new Vector2(x, y), seconds);
            return $"[autowalk] moving ({x}, {y}) for {seconds} s of game time.";
        }

        private string CmdRun(string[] args)
        {
            var player = GameObject.FindWithTag("Player");
            var controller = player != null ? player.GetComponent<PlayerController>() : null;
            if (controller == null) return "[run] No player in the scene.";

            string sub = args != null && args.Length > 1 ? args[1].ToLowerInvariant() : "";
            string value = args != null && args.Length > 2 ? args[2].ToLowerInvariant() : "";

            if (sub == "skill")
            {
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
                    return "[run] usage: run skill <0-100>";
                var skills = PlayerSkills.For(player);
                if (skills == null) return "[run] this entity cannot hold skills.";
                skills.SetTenths(LocomotionTuning.SkillKey, Mathf.RoundToInt(Mathf.Clamp(pct, 0f, 100f) * 10f));
                return "[run] Carrera = " + skills.GetPercent(LocomotionTuning.SkillKey).ToString("0.0", CultureInfo.InvariantCulture) + "%\n" + DescribeRun(controller);
            }

            if (sub == "energy")
            {
                var energy = controller.Energy;
                if (energy == null) return "[run] the player has no Energy component.";
                if (value == "full") energy.Regenerate(energy.Max);
                else if (value == "empty") energy.Drain(energy.Max);
                else if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float n))
                {
                    energy.Drain(energy.Max);
                    energy.Regenerate(Mathf.Clamp(n, 0f, energy.Max));
                }
                else return "[run] usage: run energy <n|full|empty>";
                return DescribeRun(controller);
            }

            if (sub == "reset")
            {
                controller.Gait.Reset();
                return "[run] gait reset.\n" + DescribeRun(controller);
            }

            return DescribeRun(controller);
        }

        private static string DescribeRun(PlayerController c)
        {
            var tuning = LocomotionTuning.Active;
            var gait = c.Gait;
            float skill = c.AthleticsSkill01;
            var energy = c.Energy;
            float max = energy != null ? energy.Max : 100f;
            var ci = CultureInfo.InvariantCulture;

            var sb = new StringBuilder();
            sb.Append("[run] gait=").Append(gait.State)
              .Append(gait.IsWinded ? " (sin aliento)" : "")
              .Append(" impulso=").Append(gait.Momentum.ToString("0.00", ci))
              .Append(" blend=").Append(gait.RunBlend.ToString("0.00", ci))
              .Append(" x").Append(gait.SpeedMultiplier.ToString("0.00", ci))
              .Append(" ultimaRuptura=").Append(gait.LastBreak).Append('\n');
            sb.Append("      velocidad pedida=").Append((c.WalkSpeed * gait.SpeedMultiplier).ToString("0.00", ci))
              .Append(" medida=").Append(c.MeasuredSpeed.ToString("0.00", ci))
              .Append(" caminar=").Append(c.WalkSpeed.ToString("0.00", ci)).Append('\n');
            sb.Append("      energia=")
              .Append(energy != null ? energy.Exact.ToString("0.0", ci) + "/" + energy.Max : "(ninguna)").Append('\n');
            sb.Append("      Carrera ").Append((skill * 100f).ToString("0.0", ci)).Append("%: arranque ")
              .Append(tuning.StartSeconds(skill).ToString("0.00", ci)).Append(" s, carrera x")
              .Append(tuning.RunMultiplier(skill).ToString("0.00", ci)).Append(", aguante ")
              .Append(tuning.RunEndurance(skill, max).ToString("0.0", ci)).Append(" s, giro ")
              .Append(tuning.TurnTolerance(skill).ToString("0", ci)).Append(" grados");

            var anim = c.GetComponent<DirectionalAnimator>();
            if (anim != null)
            {
                sb.Append('\n').Append("      anim=").Append(anim.CurrentState)
                  .Append(" ritmo=").Append(anim.LocomotionRate.ToString("0.00", ci))
                  .Append(" refCaminar=").Append(anim.WalkReferenceSpeed.ToString("0.00", ci))
                  .Append(" refCorrer=").Append(anim.RunReferenceSpeed.ToString("0.00", ci))
                  .Append(" (0 = derivada)");
            }
            return sb.ToString();
        }
    }
}
