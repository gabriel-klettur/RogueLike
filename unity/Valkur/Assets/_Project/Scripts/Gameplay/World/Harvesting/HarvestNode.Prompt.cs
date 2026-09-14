using System.Text;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Interaction;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// What the badge over a node says.
    ///
    /// <para>Answering four questions rather than one is the whole difference between a prompt
    /// and a label: what the key does, why it is refused, when to come back, and — for a node that
    /// trains a skill — what kind of node this is and how it measures up to the player. That last
    /// half is what makes a player walk to the old grove instead of chopping the sapling at the
    /// spawn point for the fortieth time.</para>
    /// </summary>
    public partial class HarvestNode
    {
        public InteractionPromptInfo DescribePrompt(GameObject player)
        {
            if (_profile == null || !_profile.harvestable) return InteractionPromptInfo.None;

            string verb = string.IsNullOrEmpty(_profile.harvestVerb)
                ? "Recolectar"
                : _profile.harvestVerb;

            if (_sessionActive)
                return BusyPrompt();

            if (_profile.harvestMode == HarvestMode.Deplete)
            {
                if (!_spent)
                    return Available(player, verb);

                // A node that will never refill has nothing to promise, so it says so once
                // rather than showing a countdown that never moves.
                if (_regrowAtUnix <= 0d)
                    return new InteractionPromptInfo(
                        InteractionAvailability.Blocked, "Agotada", "No volverá a llenarse");

                double secondsLeft = _regrowAtUnix - WorldDamageService.UnixNow();
                return new InteractionPromptInfo(
                    InteractionAvailability.Blocked, "Agotada",
                    secondsLeft > 0d
                        ? "Vuelve en " + FormatCountdown(secondsLeft)
                        : "Reponiéndose…");
            }

            // Destroy mode: once the building is gone there is nothing left to work.
            if (_spent || _durability == null || !_durability.AcceptsDamage)
                return InteractionPromptInfo.None;

            return Available(player, verb);
        }

        /// <summary>
        /// A node the player can work. The blow is resolved through the SAME entry point a real
        /// swing uses, so the badge can never promise something the next blow disagrees with.
        /// </summary>
        private InteractionPromptInfo Available(GameObject player, string verb)
        {
            var blow = HarvestBlowResolver.Resolve(_profile, player, element: null);

            if (blow.Immune)
                return new InteractionPromptInfo(
                    InteractionAvailability.Blocked, verb, ToolHint(_profile.material));

            var detail = new StringBuilder(64);

            string kind = NodeKindName();
            if (!string.IsNullOrEmpty(kind)) detail.Append(kind);

            if (_profile.gatheringSkill != null)
            {
                int tenths = SkillTenthsOf(player);
                var ease = _profile.gatheringSkill.Ease(tenths, _profile.skillDifficulty);
                Separator(detail);
                detail.Append(_profile.gatheringSkill.displayName).Append(' ')
                      .Append(GatheringSkillDefinition.FormatPercent(tenths))
                      .Append(" (").Append(GatheringSkillDefinition.EaseLabel(ease)).Append(')');
            }

            // Bare-handed work is not refused — the floor in HarvestBlowResolver.Scale keeps it at
            // one point a blow — so a tree takes ten times the blows. With the reason on screen it
            // reads as a game telling the player to find an axe rather than as a broken node.
            if (blow.WrongTool)
            {
                Separator(detail);
                detail.Append(ToolHint(_profile.material)).Append(": así es muy lento");
            }

            return new InteractionPromptInfo(InteractionAvailability.Ready, verb, detail.ToString());
        }

        /// <summary>
        /// While working: the key now STRIKES on the beat (when this node has a rhythm), so the badge
        /// says that, how much faster it is at this skill, and how to stop — the verb used to be
        /// "Detener", which is no longer what a tap does.
        /// </summary>
        private InteractionPromptInfo BusyPrompt()
        {
            string remaining = RemainingDetail();
            if (!AcceptsRhythmTaps)
                return new InteractionPromptInfo(InteractionAvailability.Busy, "Detener", remaining);

            float tempo = _profile.gatheringSkill.RhythmTempo(SkillTenthsOf(_worker));
            string pace = "x" + tempo.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

            if (_rhythmMode)
            {
                string streak = _hitStreak > 1 ? " · racha " + _hitStreak : string.Empty;
                return new InteractionPromptInfo(InteractionAvailability.Busy, "Al ritmo " + pace,
                    remaining + streak + " · mantén para parar");
            }

            return new InteractionPromptInfo(InteractionAvailability.Busy, "Pulsa al ritmo",
                remaining + " · automático · hasta " + pace + " · mantén para parar");
        }

        private static void Separator(StringBuilder sb)
        {
            if (sb.Length > 0) sb.Append(" · ");
        }

        /// <summary>"Árbol ancestral" for a tree, nothing for a node whose kind is not a tree.</summary>
        private string NodeKindName()
        {
            if (_profile != null && !string.IsNullOrEmpty(_profile.nodeDisplayName)) return _profile.nodeDisplayName;

            string path = _building != null && _building.Template != null ? _building.Template.assetPath : null;
            var family = TreeFamilyClassifier.Classify(path);
            return family == TreeFamily.None ? null : TreeFamilyClassifier.DisplayName(family);
        }

        private int SkillTenthsOf(GameObject player)
        {
            if (_profile == null || _profile.gatheringSkill == null) return 0;
            var skills = PlayerGatheringSkills.Peek(player);
            return skills != null ? skills.GetTenths(_profile.gatheringSkill.skillKey) : 0;
        }

        /// <summary>
        /// What to go and fetch, named after the MATERIAL rather than after any particular item:
        /// the resistance matrix is keyed off the material, and a named item goes stale the first
        /// time the catalogue grows a second pick.
        /// </summary>
        private static string ToolHint(MaterialClass material)
        {
            switch (material)
            {
                case MaterialClass.Stone:   return "Necesitas un pico";
                case MaterialClass.Wood:    return "Necesitas un hacha";
                case MaterialClass.Metal:   return "Necesitas algo contundente";
                case MaterialClass.Foliage: return "Necesitas algo afilado";
                default:                    return "Necesitas otra herramienta";
            }
        }

        /// <summary>How much is left, phrased for whichever mode this node is in.</summary>
        private string RemainingDetail()
        {
            if (_profile == null) return string.Empty;

            if (_profile.harvestMode == HarvestMode.Deplete)
                return _chargesRemaining == 1
                    ? "Queda 1 carga"
                    : $"Quedan {_chargesRemaining} cargas";

            return $"{Mathf.CeilToInt(RemainingFraction * 100f)}%";
        }

        /// <summary><c>m:ss</c> above a minute, plain seconds below it.</summary>
        private static string FormatCountdown(double seconds)
        {
            int total = Mathf.Max(1, Mathf.CeilToInt((float)seconds));
            if (total < 60) return total + " s";
            return (total / 60) + ":" + (total % 60).ToString("00");
        }
    }
}
