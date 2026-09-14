using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.World;
using Valkur.Gameplay.Skills;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>skill</c>, <c>talar</c>, <c>regrow</c> and <c>fell</c> commands: the gathering
    /// skill and the woodcutting nodes, measurable without chopping for an hour.
    ///
    /// <para>The skill is a curve over thousands of blows and a tree regrows on a wall clock
    /// measured in minutes, so neither "is 60 % reachable in a sensible time" nor "does the stump
    /// come back as a tree" is answerable by playing. <c>skill sim</c> prints the expected hours
    /// the definition's own pure maths predicts, and <c>talar</c> prints everything the next blow
    /// on the nearest tree will resolve to — the same numbers the prompt and the blow use.</para>
    ///
    /// Registered from <c>DevConsole.cs::RegisterDefaults()</c>.
    /// </summary>
    public partial class DevConsole
    {
        private void RegisterHarvestCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name     = "skill",
                Aliases  = new[] { "habilidad" },
                Usage    = "skill [clave [porcentaje]] | skill sim [clave]",
                Help     = "habilidades de recoleccion: listar, fijar un valor o simular el tiempo hasta 100%",
                Category = "gathering",
                Handler  = args => Log(CmdSkill(args))
            });

            RegisterCommand(new ConsoleCommand
            {
                Name     = "talar",
                Aliases  = new[] { "harvest" },
                Usage    = "talar",
                Help     = "informe del arbol mas cercano: dificultad, eficiencia, ganancia y maderas posibles",
                Category = "gathering",
                Handler  = args => Log(CmdHarvestReport())
            });

            RegisterCommand(new ConsoleCommand
            {
                Name     = "regrow",
                Aliases  = new[] { "rebrotar" },
                Usage    = "regrow [radio|all]",
                Help     = "hace rebrotar ya los arboles talados cercanos (o todos)",
                Category = "gathering",
                Handler  = args => Log(CmdRegrow(args))
            });

            RegisterCommand(new ConsoleCommand
            {
                Name     = "fell",
                Usage    = "fell",
                Help     = "tala el arbol mas cercano de un golpe, como el jugador (prueba de caida y botin)",
                Category = "gathering",
                Handler  = args => Log(CmdFell())
            });

            RegisterCommand(new ConsoleCommand
            {
                Name     = "tronco",
                Aliases  = new[] { "trunk" },
                Usage    = "tronco | tronco ver [on|off] | tronco <x0> <y0> <x1> <y1>",
                Help     = "caja del tronco del arbol mas cercano: informe, dibujarla en el mundo o fijarla (fracciones del sprite)",
                Category = "gathering",
                Handler  = args => Log(CmdTrunk(args))
            });
        }

        private string CmdTrunk(string[] args)
        {
            string sub = args != null && args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;

            if (sub == "ver" || sub == "show")
            {
                bool on = args.Length > 2
                    ? args[2].ToLowerInvariant() != "off"
                    : Object.FindObjectOfType<TrunkBoxOverlay>() == null;
                return TrunkBoxOverlay.SetVisible(on)
                    ? "Cajas de impacto visibles (verde = tronco dibujado, naranja = huella sin tronco)."
                    : "Cajas de impacto ocultas.";
            }

            var player = HarvestPlayer();
            var node = NearestNode(player, includeSpent: true);
            if (node == null || node.Building == null) return "No hay árbol cercano.";
            var t = node.Building.Template;
            if (t == null) return "El árbol más cercano no tiene plantilla.";

            if (args != null && args.Length >= 5)
            {
                var v = new float[4];
                for (int i = 0; i < 4; i++)
                    if (!float.TryParse(args[1 + i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]))
                        return $"'{args[1 + i]}' no es un número. Fracciones del sprite, p. ej. tronco 0.4 0.05 0.6 0.45";
                if (v[2] <= v[0] || v[3] <= v[1]) return "La esquina superior derecha tiene que quedar por encima y a la derecha.";

                t.trunkNormalized = Rect.MinMaxRect(Mathf.Clamp01(v[0]), Mathf.Clamp01(v[1]), Mathf.Clamp01(v[2]), Mathf.Clamp01(v[3]));
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(t);
#endif
                return $"Tronco de '{t.assetPath}' (plantilla {t.templateId}) fijado. Copialo a tools/atlas/generated/tree_trunks.json " +
                       "o el próximo 'Apply Tree Trunk Boxes' lo devolverá al valor dibujado.";
            }

            var r = t.trunkNormalized;
            var b = node.InteractionBounds;
            bool drawn = node.Building.TryGetTrunkBounds(out _);
            return $"{t.assetPath} (plantilla {t.templateId})\n" +
                   (t.HasTrunk
                       ? $"  tronco {r.xMin:0.00} {r.yMin:0.00} {r.xMax:0.00} {r.yMax:0.00}"
                       : "  sin tronco dibujado") +
                   $"\n  area de impacto ({(drawn ? "tronco" : "huella")}): " +
                   $"x {b.min.x:0.00}..{b.max.x:0.00}  y {b.min.y:0.00}..{b.max.y:0.00}";
        }

        private static GameObject HarvestPlayer() => GameObject.FindWithTag("Player");

        private string CmdSkill(string[] args)
        {
            var catalog = SkillCatalog.Shared;
            var player = HarvestPlayer();
            if (catalog == null) return "No hay SkillCatalog en Resources/Skills.";

            string sub = args != null && args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;

            if (sub == "sim")
            {
                var def = args.Length > 2 ? catalog.Find(args[2]) : (catalog.skills.Count > 0 ? catalog.skills[0] : null);
                if (def == null) return "Habilidad desconocida.";
                return SimulateSkill(def);
            }

            if (player == null) return "No hay jugador.";

            if (string.IsNullOrEmpty(sub))
            {
                var sb = new StringBuilder("Habilidades de recolección:\n");
                var skills = PlayerSkills.Peek(player);
                foreach (var def in catalog.skills)
                {
                    if (def == null) continue;
                    int t = skills != null ? skills.GetTenths(def.skillKey) : 0;
                    sb.Append("  ").Append(def.skillKey).Append("  ").Append(def.displayName).Append("  ")
                      .Append(SkillDefinition.FormatPercent(t)).Append('\n');
                }
                return sb.ToString();
            }

            var target = catalog.Find(sub);
            if (target == null) return $"Habilidad '{sub}' desconocida.";
            if (args.Length < 3) return $"{target.displayName}: {SkillDefinition.FormatPercent(PlayerSkills.Peek(player)?.GetTenths(target.skillKey) ?? 0)}";

            if (!float.TryParse(args[2].TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
                return "Porcentaje no válido.";

            var comp = PlayerSkills.For(player);
            if (comp == null) return "El jugador no admite habilidades.";
            comp.SetTenths(target.skillKey, Mathf.RoundToInt(Mathf.Clamp(pct, 0f, 100f) * 10f));
            return $"{target.displayName} = {SkillDefinition.FormatPercent(comp.GetTenths(target.skillKey))}";
        }

        /// <summary>Expected hours to 100 % from the definition's pure gain maths.</summary>
        private static string SimulateSkill(SkillDefinition def)
        {
            const float secondsPerBlow = 0.6f;
            var sb = new StringBuilder();
            sb.Append(def.displayName).Append(": horas de golpes efectivos hasta 100% (")
              .Append(secondsPerBlow.ToString(CultureInfo.InvariantCulture)).Append(" s/golpe)\n");

            Append(sb, "árbol adecuado siempre", def.ExpectedBlowsToMax(0, s => Mathf.Clamp(Mathf.RoundToInt(s), 0, 80)), secondsPerBlow);
            Append(sb, "solo árboles comunes (15)", def.ExpectedBlowsToMax(0, s => 15), secondsPerBlow);
            Append(sb, "solo ancestrales (70)", def.ExpectedBlowsToMax(0, s => 70), secondsPerBlow);
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string label, double blows, float secondsPerBlow)
        {
            sb.Append("  ").Append(label).Append(": ")
              .Append(double.IsInfinity(blows) ? "inalcanzable" : (blows * secondsPerBlow / 3600d).ToString("0.0", CultureInfo.InvariantCulture) + " h")
              .Append('\n');
        }

        private static HarvestNode NearestNode(GameObject player, bool includeSpent)
        {
            if (player == null) return null;
            HarvestNode best = null;
            float bestSq = float.MaxValue;
            foreach (var node in Object.FindObjectsOfType<HarvestNode>())
            {
                if (!includeSpent && node.IsSpent) continue;
                float sq = ((Vector2)(node.InteractionPosition - (Vector2)player.transform.position)).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = node; }
            }
            return best;
        }

        private string CmdHarvestReport()
        {
            var player = HarvestPlayer();
            var node = NearestNode(player, includeSpent: true);
            if (node == null) return "No hay nodos de recolección en la escena.";

            var p = node.Profile;
            var sb = new StringBuilder();
            float dist = Vector2.Distance(node.InteractionPosition, player.transform.position);
            string path = node.Building != null && node.Building.Template != null ? node.Building.Template.assetPath : "?";

            sb.Append(p.name).Append("  ").Append(path).Append("  a ").Append(dist.ToString("0.0", CultureInfo.InvariantCulture)).Append(" u\n");
            sb.Append("  modo ").Append(p.harvestMode).Append("  restante ").Append((node.RemainingFraction * 100f).ToString("0")).Append("%")
              .Append(node.IsSpent ? "  (talado)" : "").Append('\n');

            var blow = HarvestBlowResolver.Resolve(p, player, element: null);
            sb.Append("  golpe: clase ").Append(blow.DamageClass).Append("  tier ").Append(blow.ToolTier)
              .Append(blow.WrongTool ? "  HERRAMIENTA INADECUADA" : "")
              .Append("  multiplicador ").Append(blow.Multiplier.ToString("0.###", CultureInfo.InvariantCulture))
              .Append("  daño ").Append(HarvestBlowResolver.Scale(p.blowDamage, blow.Multiplier)).Append('\n');

            if (p.gatheringSkill != null)
            {
                var skills = PlayerSkills.Peek(player);
                int t = skills != null ? skills.GetTenths(p.gatheringSkill.skillKey) : 0;
                var def = p.gatheringSkill;
                sb.Append("  ").Append(def.displayName).Append(' ').Append(SkillDefinition.FormatPercent(t))
                  .Append(" vs dificultad ").Append(p.skillDifficulty)
                  .Append(" (").Append(SkillDefinition.EaseLabel(def.Ease(t, p.skillDifficulty))).Append(")\n");
                sb.Append("  eficiencia x").Append(def.EfficiencyMultiplier(t, p.skillDifficulty).ToString("0.00", CultureInfo.InvariantCulture))
                  .Append("  prob. ganancia ").Append((def.GainChance(t, p.skillDifficulty, blow.WrongTool) * 100f).ToString("0.00", CultureInfo.InvariantCulture)).Append("%")
                  .Append("  bonus al talar +").Append(p.fellBonusYields + def.BonusYields(t)).Append('\n');

                if (def.yieldTable != null)
                {
                    var shares = new List<float>();
                    def.yieldTable.Shares(SkillDefinition.ToPercent(t), p.yieldTags, shares);
                    sb.Append("  maderas posibles aquí:\n");
                    for (int i = 0; i < shares.Count; i++)
                    {
                        if (shares[i] <= 0f) continue;
                        var tier = def.yieldTable.tiers[i];
                        sb.Append("    ").Append((shares[i] * 100f).ToString("0.0", CultureInfo.InvariantCulture).PadLeft(5))
                          .Append("%  ").Append(tier.displayName).Append('\n');
                    }
                }
            }
            return sb.ToString();
        }

        private string CmdRegrow(string[] args)
        {
            var player = HarvestPlayer();
            bool all = args != null && args.Length > 1 && args[1].ToLowerInvariant() == "all";
            float radius = 12f;
            if (!all && args != null && args.Length > 1)
                float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out radius);

            int count = 0;
            foreach (var d in Object.FindObjectsOfType<BuildingDurability>())
            {
                if (!d.IsDestroyed) continue;
                if (!all && player != null &&
                    ((Vector2)(d.transform.position - player.transform.position)).sqrMagnitude > radius * radius) continue;
                d.Regrow();
                count++;
            }
            return $"{count} árbol(es) rebrotado(s).";
        }

        private string CmdFell()
        {
            var player = HarvestPlayer();
            var node = NearestNode(player, includeSpent: false);
            if (node == null) return "No hay árbol cercano en pie.";

            var durability = node.GetComponent<BuildingDurability>();
            if (durability == null) return "El nodo más cercano no se tala (es una veta).";

            Vector2 contact = node.InteractionBounds.ClosestPoint(player.transform.position);
            // A large raw amount through the real entry point: the matrix, the tool gate and the
            // skill still apply, so this tests the path a real finishing blow takes.
            for (int i = 0; i < 400 && durability.AcceptsDamage; i++)
                durability.ApplyObstacleDamage(1000, player, contact, element: null);

            return durability.IsDestroyed ? "Talado." : "No se pudo talar (¿inmune?).";
        }
    }
}
