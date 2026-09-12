using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// Every player-visible string the CHARACTER and RECORDS tabs draw, resolved through
    /// <see cref="GameLanguage"/>.
    ///
    /// <para>The third of its family, after <c>SkillText</c> and <c>GrimoireText</c>, and the
    /// four tabs of one window go through the same switch on purpose: a sheet where two tabs
    /// answer the language setting and two do not is worse than either behaviour alone.</para>
    ///
    /// <para><b>Layer names are translated here and not in the enum.</b> <c>StatLayer</c> is the
    /// save format and the key of <c>PlayerStats.SetLayer</c>; its members are identifiers, and
    /// <c>layer.ToString().ToLowerInvariant()</c> — which is what the old panel printed — puts a
    /// C# identifier in front of a player.</para>
    /// </summary>
    public static class SheetText
    {
        // ── CHARACTER ─────────────────────────────────────────────────────────

        public static string Character => GameLanguage.Pick("Character", "Personaje");
        public static string Stats => GameLanguage.Pick("Attributes", "Atributos");
        public static string Level => GameLanguage.Pick("Level", "Nivel");
        public static string NoCharacter => GameLanguage.Pick("No character", "Sin personaje");
        public static string SkillPoints => GameLanguage.Pick("Talent points", "Puntos de talento");
        public static string ArcanePoints => GameLanguage.Pick("Arcane points", "Puntos arcanos");
        public static string Spent => GameLanguage.Pick("spent", "gastados");
        public static string Total => GameLanguage.Pick("Total", "Total");

        /// <summary>Where a number came from. The labels of the stacked breakdown bar.</summary>
        public static string LayerName(StatLayer layer)
        {
            switch (layer)
            {
                case StatLayer.Base:      return GameLanguage.Pick("base", "base");
                case StatLayer.Level:     return GameLanguage.Pick("level", "nivel");
                case StatLayer.Skill:     return GameLanguage.Pick("talents", "talentos");
                case StatLayer.Grimoire:  return GameLanguage.Pick("grimoire", "grimorio");
                case StatLayer.Equipment: return GameLanguage.Pick("gear", "equipo");
                case StatLayer.Buff:      return GameLanguage.Pick("buffs", "efectos");
                case StatLayer.Aura:      return GameLanguage.Pick("auras", "auras");
                default:                  return layer.ToString().ToLowerInvariant();
            }
        }

        // ── RECORDS ───────────────────────────────────────────────────────────

        public static string Records => GameLanguage.Pick("Records", "Registros");
        public static string Lifetime => GameLanguage.Pick("Lifetime", "Histórico");
        public static string Runs => GameLanguage.Pick("Runs", "Partidas");
        public static string Playtime => GameLanguage.Pick("Playtime", "Tiempo jugado");
        public static string AverageRun => GameLanguage.Pick("Average run", "Partida media");
        public static string Deaths => GameLanguage.Pick("Deaths", "Muertes");
        public static string Achievements => GameLanguage.Pick("Achievements", "Logros");
        public static string TopKills => GameLanguage.Pick("Most slain", "Más abatidos");
        public static string RecentRuns => GameLanguage.Pick("Recent runs", "Partidas recientes");
        public static string NoKills => GameLanguage.Pick("Nothing slain yet", "Todavía no has abatido nada");
        public static string NoRuns => GameLanguage.Pick("No runs yet", "Todavía no hay partidas");
        public static string Survived => GameLanguage.Pick("survived", "sobrevivió");
        public static string InProgress => GameLanguage.Pick("in progress", "en curso");
        public static string Kills => GameLanguage.Pick("kills", "bajas");
        public static string Depth => GameLanguage.Pick("level reached", "nivel alcanzado");

        /// <summary>"y 241 más" — the count a list of ten out of 251 must not swallow.</summary>
        public static string AndMore(int more) =>
            GameLanguage.IsEnglish ? "and " + more + " more" : "y " + more + " más";

        /// <summary>A duration a player reads, never a raw second count.</summary>
        public static string Duration(float seconds)
        {
            if (seconds <= 0f) return "—";
            int total = UnityEngine.Mathf.RoundToInt(seconds);
            int h = total / 3600, m = (total % 3600) / 60, s = total % 60;
            if (h > 0) return h + "h " + m.ToString("00") + "m";
            if (m > 0) return m + "m " + s.ToString("00") + "s";
            return s + "s";
        }
    }
}
