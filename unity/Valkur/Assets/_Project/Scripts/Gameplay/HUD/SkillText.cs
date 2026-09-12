using System.Collections.Generic;
using System.Text;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// Every player-visible string the talents board draws, in one place, resolved through
    /// <see cref="GameLanguage"/>.
    ///
    /// <para><b>Why a file rather than literals at the call site.</b> The panel shipped entirely
    /// in English inside a game whose HUD, chat, quests and menus are in Spanish — "Learn",
    /// "Available", "Locked", and the placeholder plural <c>"point(s) available"</c> — while the
    /// stat names in the SAME sentence came out of <c>StatCatalog</c> already translated.
    /// HUD_VISUAL_LANGUAGE.md R15 forbids that for a game window. This is the twin of
    /// <c>GrimoireText</c>, which the sibling tab uses, and the two go through the same switch on
    /// purpose: two tabs of one window behaving differently under EN is worse than either
    /// behaviour on its own.</para>
    ///
    /// <para><b>Plurals are resolved, never parenthesised.</b> "1 punto" and "2 puntos" are two
    /// strings, and a panel that says "punto(s)" is a panel nobody finished.</para>
    ///
    /// <para><b>It does not translate the node NAMES.</b> Those live on the assets and are
    /// authored content, the boundary <c>GameLanguage</c> draws for itself: English moves the
    /// game's own chrome and leaves authored text alone.</para>
    /// </summary>
    public static class SkillText
    {
        public static string Title => GameLanguage.Pick("Talents", "Talentos");
        public static string Learn => GameLanguage.Pick("Learn", "Aprender");
        public static string Respec => GameLanguage.Pick("Reset talents", "Reiniciar talentos");
        public static string RespecConfirm => GameLanguage.Pick("Sure?", "¿Seguro?");
        public static string NoTree => GameLanguage.Pick("Talents  —  this class has no path",
                                                         "Talentos  —  esta clase no tiene senda");
        public static string Maxed => GameLanguage.Pick("Maxed", "Al máximo");
        public static string Available => GameLanguage.Pick("Available", "Disponible");
        public static string Locked => GameLanguage.Pick("Locked", "Bloqueado");
        public static string Now => GameLanguage.Pick("Now", "Ahora");
        public static string NextRank => GameLanguage.Pick("Next", "Siguiente");
        public static string Cost => GameLanguage.Pick("Cost", "Coste");
        public static string PointsLabel => GameLanguage.Pick("POINTS", "PUNTOS");
        public static string PassiveEffect => GameLanguage.Pick("Passive effect", "Efecto pasivo");

        /// <summary>Separator between two lock reasons on one line.</summary>
        public const string ReasonSeparator = "  ·  ";

        /// <summary>"1 punto" / "3 puntos".</summary>
        public static string Points(int n) =>
            GameLanguage.IsEnglish
                ? (n == 1 ? "1 point" : n + " points")
                : (n == 1 ? "1 punto" : n + " puntos");

        /// <summary>
        /// The short form drawn on a node: "1 PH" / "1 SP". Two letters, because it sits under a
        /// 32-texel socket beside a row of pips.
        /// </summary>
        public static string PointsShort(int n) => n + GameLanguage.Pick(" SP", " PH");

        /// <summary>"Rango 2 / 5", or "Rango 0 / 5" for an untouched node.</summary>
        public static string Rank(int rank, int maxRank) =>
            GameLanguage.Pick("Rank ", "Rango ") + rank + " / " + maxRank;

        /// <summary>Header: the tree, and the purse that governs every node in it.</summary>
        public static string Header(string treeName, int availablePoints)
        {
            string available = GameLanguage.IsEnglish
                ? (availablePoints == 1 ? " available" : " available")
                : (availablePoints == 1 ? " disponible" : " disponibles");
            return treeName + "   —   " + Points(availablePoints) + available;
        }

        /// <summary>Footer: how far into the tree the character is, in the only unit they spend.</summary>
        public static string Spent(int spent, int total) =>
            GameLanguage.IsEnglish
                ? spent + " of " + total + " points spent"
                : spent + " de " + total + " puntos gastados";

        /// <summary>
        /// One lock reason as a sentence. The numbers come from the struct rather than from a
        /// pre-formatted string, which is the whole reason <see cref="SkillLock"/> is data.
        /// </summary>
        public static string Reason(SkillLock lockReason)
        {
            switch (lockReason.Kind)
            {
                case SkillLockKind.Maxed:
                    return Maxed;
                case SkillLockKind.Level:
                    return GameLanguage.IsEnglish
                        ? "Needs level " + lockReason.Required + " (you are " + lockReason.Have + ")"
                        : "Necesitas nivel " + lockReason.Required + " (tienes " + lockReason.Have + ")";
                case SkillLockKind.Prerequisite:
                    return PrerequisiteReason(lockReason);
                case SkillLockKind.Points:
                    return MissingPoints(lockReason.Required - lockReason.Have);
                default:
                    return GameLanguage.Pick("Malformed node", "Nodo mal formado");
            }
        }

        private static string MissingPoints(int missing)
        {
            if (GameLanguage.IsEnglish) return "You need " + Points(missing) + " more";
            return "Te " + (missing == 1 ? "falta" : "faltan") + " " + Points(missing);
        }

        /// <summary>
        /// "Falta Piel de Piedra (3 rangos)". The ranks owed are said out loud because "finish
        /// that one" and "finish that one, three ranks to go" are different amounts of commitment
        /// and the player is deciding whether to make it.
        /// </summary>
        private static string PrerequisiteReason(SkillLock lockReason)
        {
            string name = lockReason.Missing != null ? lockReason.Missing.displayName : "?";
            string head = GameLanguage.Pick("Needs ", "Falta ");
            if (lockReason.MissingRanksLeft <= 1) return head + name;
            string ranks = GameLanguage.IsEnglish
                ? lockReason.MissingRanksLeft + " ranks"
                : lockReason.MissingRanksLeft + " rangos";
            return head + name + " (" + ranks + ")";
        }

        /// <summary>Every reason on one line, most-blocking first (the list is already ordered).</summary>
        public static string Reasons(IReadOnlyList<SkillLock> locks, StringBuilder sb)
        {
            if (locks == null || locks.Count == 0) return string.Empty;
            sb.Length = 0;
            for (int i = 0; i < locks.Count; i++)
            {
                if (i > 0) sb.Append(ReasonSeparator);
                sb.Append(Reason(locks[i]));
            }
            return sb.ToString();
        }

        /// <summary>
        /// The mechanical effect of a node AT a rank, generated from its modifiers. Wraps
        /// <see cref="SkillNode.DescribeRank"/> only to give the aura-only case a translated
        /// sentence; the numeric half is already translated by <c>StatCatalog</c>.
        /// </summary>
        public static string Effect(SkillNode node, int rank)
        {
            if (node == null) return string.Empty;
            string described = node.DescribeRank(rank);
            return described == "Passive effect" ? PassiveEffect : described;
        }
    }
}
