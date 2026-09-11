using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Valkur.Data;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Every string the debug HUD prints, and the one place its wording lives (H9: in Spanish,
    /// from a table). Pure and static so the formatting is provable in EditMode.
    ///
    /// <para><b>Everything is folded for the pixel face.</b> The 3x5 face has capitals, digits
    /// and some punctuation — no lowercase, no accents. <see cref="Fold"/> upper-cases, strips
    /// diacritics (Á to A, Ñ to N) and drops what the face still cannot draw, so a name like
    /// "Bola de Fuego" or "Gatita" prints instead of vanishing letter by letter.</para>
    /// </summary>
    public static class DebugHudText
    {
        // Section titles.
        public const string Performance = "RENDIMIENTO";
        public const string Player = "JUGADOR";
        public const string Combat = "COMBATE";
        public const string Nearby = "CERCA";

        // Readout words.
        public const string Ready = "LISTO";
        public const string NoMana = "SIN MANA";
        public const string Locked = "NO APRENDIDO";
        public const string Empty = "-";
        public const string Dashing = "EN CURSO";
        public const string Waiting = "ESPERANDO AL JUGADOR";
        public const string NobodyNear = "NADIE A 15 U";
        public const string NoHitches = "SIN TIRONES";
        public const string Copy = "COPIAR";
        public const string Copied = "COPIADO";
        public const string Invincible = "INVENCIBLE";
        public const string Unavailable = "-";

        /// <summary>Label of each graded frame, beside its colour: never colour alone (R6).</summary>
        public static string Grade(int grade) => grade == 0 ? "OK" : grade == 1 ? "AVISO" : "MAL";

        public static string Stance(bool peace) => peace ? "PAZ" : "GUERRA";

        /// <summary>The level hint in the footer: which key, and where the cycle is.</summary>
        public static string LevelHint(string key, int level, int max) =>
            Fold(key) + " NIVEL " + level + "/" + max;

        /// <summary>A spell caster's phase, in the words the rest of the HUD uses.</summary>
        public static string Phase(int phase)
        {
            switch (phase)
            {
                case 1: return "PREPARA";
                case 2: return "CANALIZA";
                case 3: return "RECARGA";
                default: return "LISTO";
            }
        }

        /// <summary>Short Spanish name of a status effect, for a one-row readout.</summary>
        public static string Status(StatusEffectKind kind)
        {
            switch (kind)
            {
                case StatusEffectKind.Burn: return "ARDE";
                case StatusEffectKind.Poison: return "VENENO";
                case StatusEffectKind.Stun: return "ATURDIDO";
                case StatusEffectKind.Freeze: return "CONGELADO";
                case StatusEffectKind.Slow: return "LENTO";
                case StatusEffectKind.Root: return "ENRAIZADO";
                case StatusEffectKind.Vulnerable: return "VULNERABLE";
                case StatusEffectKind.Marked: return "MARCADO";
                default: return Fold(kind.ToString());
            }
        }

        /// <summary>An FSM state's class name without the "State" suffix: "ChaseState" is CHASE.</summary>
        public static string FsmState(string className)
        {
            if (string.IsNullOrEmpty(className)) return "?";
            const string suffix = "State";
            string s = className.EndsWith(suffix) && className.Length > suffix.Length
                ? className.Substring(0, className.Length - suffix.Length)
                : className;
            return Fold(s);
        }

        // -- Numbers ---------------------------------------------------------------

        /// <summary>Milliseconds with one decimal under 100 and none above: "9.8", "17.8", "143".</summary>
        public static string Ms(float ms)
        {
            if (ms < 0f) ms = 0f;
            return ms < 99.95f
                ? ms.ToString("0.0", CultureInfo.InvariantCulture)
                : ms.ToString("0", CultureInfo.InvariantCulture);
        }

        /// <summary>Two decimals under a millisecond: the overlay's own cost is ~0.05 ms and "0.0" says nothing.</summary>
        public static string SmallMs(float ms)
        {
            if (ms < 0f) ms = 0f;
            return ms < 1f ? ms.ToString("0.00", CultureInfo.InvariantCulture) : Ms(ms);
        }

        /// <summary>A duration as a clock: "0:42", "12:05", "1:02:03". Raw seconds were unreadable past a minute.</summary>
        public static string Clock(float seconds)
        {
            if (seconds < 0f || float.IsNaN(seconds)) seconds = 0f;
            int total = (int)seconds;
            int h = total / 3600, m = (total / 60) % 60, s = total % 60;
            return h > 0
                ? h + ":" + m.ToString("00") + ":" + s.ToString("00")
                : m + ":" + s.ToString("00");
        }

        /// <summary>A byte count with one significant unit: "512B", "1.2K", "812M", "1.4G".</summary>
        public static string Bytes(long bytes)
        {
            if (bytes < 0) bytes = 0;
            if (bytes < 1024) return bytes + "B";
            double v = bytes / 1024.0;
            if (v < 1024) return Short(v) + "K";
            v /= 1024.0;
            if (v < 1024) return Short(v) + "M";
            return Short(v / 1024.0) + "G";
        }

        /// <summary>A world coordinate with one decimal, invariant: "173.2".</summary>
        public static string Coord(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);

        private static string Short(double v) =>
            v < 10 ? v.ToString("0.0", CultureInfo.InvariantCulture) : v.ToString("0", CultureInfo.InvariantCulture);

        // -- Folding ---------------------------------------------------------------

        /// <summary>
        /// Upper-cases, strips diacritics and drops anything the small pixel face has no glyph
        /// for. Spaces survive. The result is always spellable in that face.
        /// </summary>
        public static string Fold(string s, int maxLength = int.MaxValue)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var glyphs = SmallGlyphs();
            var sb = new StringBuilder(s.Length);
            string decomposed = s.Normalize(NormalizationForm.FormD);
            for (int i = 0; i < decomposed.Length && sb.Length < maxLength; i++)
            {
                char c = decomposed[i];
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                c = char.ToUpperInvariant(c);
                if (c == ' ' || glyphs.ContainsKey(c)) sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        private static Dictionary<char, string[]> _smallGlyphs;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDebugHudTextStatics() => _smallGlyphs = null;

        private static Dictionary<char, string[]> SmallGlyphs() =>
            _smallGlyphs ?? (_smallGlyphs = HudPixelFont.Glyphs(HudFontFace.Small));
    }
}
