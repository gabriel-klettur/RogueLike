using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>buildings</c> command family: what the placed buildings look like, as numbers.
    ///
    /// The 2026-09-05 audit of the Buildings editor answered "are the buildings drawn wrong"
    /// by walking every <see cref="BuildingObject"/> through <c>execute_code</c> and comparing
    /// its rendered scale with its sprite. That walk is the useful half of the audit and it
    /// belonged in the game, not in a probe: <c>buildings audit</c> lists every instance whose
    /// on-screen aspect no longer matches its art, every one rendered too small to see, and
    /// every one whose sprite failed to load — the three shapes a "looks wrong" report has had.
    /// </summary>
    public partial class DevConsole
    {
        private const float AUDIT_ASPECT_TOLERANCE = 0.10f;   // 10 % off the sprite's own aspect
        private const float AUDIT_TINY_SCALE       = 0.15f;   // under this the art is a few pixels
        private const int   AUDIT_MAX_ROWS         = 25;

        private void RegisterBuildingsCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name     = "buildings",
                Usage    = "buildings audit",
                Help     = "list placed buildings whose rendering disagrees with their art (aspect, size, missing sprite)",
                Category = "editors",
                Handler  = args => Log(CmdBuildingsAudit(args))
            });
        }

        private static string CmdBuildingsAudit(string[] args)
        {
            if (args.Length < 2 || args[1] != "audit")
                return "Usage: buildings audit";

            var all = Object.FindObjectsOfType<BuildingObject>();
            var sb  = new StringBuilder();
            var rows = new List<string>();
            int distorted = 0, tiny = 0, missing = 0, overridden = 0;

            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null) continue;

                var fr = b.FootprintRenderer;
                if (fr == null || fr.sprite == null)
                {
                    missing++;
                    rows.Add($"  MISSING  id={b.InstanceId} template={b.Template?.name ?? "null"} pos={b.transform.position:F1}");
                    continue;
                }

                var so = b.ScaleOverride;
                bool hasOverride = so.x > 0 && so.y > 0;
                if (hasOverride) overridden++;

                var ls = b.transform.localScale;
                if (ls.x < AUDIT_TINY_SCALE && ls.y < AUDIT_TINY_SCALE)
                {
                    tiny++;
                    rows.Add($"  TINY     id={b.InstanceId} template={b.Template?.name} scale={ls.x:F2}x{ls.y:F2} pos={b.transform.position:F1}");
                    continue;
                }

                // The renderers draw the sprite at localScale; anything but a uniform scale is a
                // stretch away from the art's own aspect. A deliberate per-instance override is
                // reported too — it is the designer's call, but it is still the thing that
                // makes a house look wide.
                float ratio = ls.y > 0.0001f ? ls.x / ls.y : 0f;
                if (Mathf.Abs(ratio - 1f) > AUDIT_ASPECT_TOLERANCE)
                {
                    distorted++;
                    rows.Add(string.Format(CultureInfo.InvariantCulture,
                        "  ASPECT   id={0} template={1} x/y={2:F2} {3} pos={4}",
                        b.InstanceId, b.Template?.name, ratio,
                        hasOverride ? $"override={so.x}x{so.y}" : "no override (template/sprite drift)",
                        b.transform.position.ToString("F1")));
                }
            }

            sb.AppendLine($"[buildings audit] {all.Length} placed, {overridden} with a size override.");
            sb.AppendLine($"  aspect off by >{AUDIT_ASPECT_TOLERANCE:P0}: {distorted}   too small to see: {tiny}   sprite missing: {missing}");
            if (rows.Count == 0)
            {
                sb.AppendLine("  Nothing to report: every building renders at its art's own aspect.");
                return sb.ToString();
            }
            for (int i = 0; i < rows.Count && i < AUDIT_MAX_ROWS; i++) sb.AppendLine(rows[i]);
            if (rows.Count > AUDIT_MAX_ROWS) sb.AppendLine($"  … {rows.Count - AUDIT_MAX_ROWS} more.");
            return sb.ToString();
        }
    }
}
