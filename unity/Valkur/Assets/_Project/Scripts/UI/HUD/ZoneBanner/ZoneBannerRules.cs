using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// When a zone's name is worth writing across the screen, and how it is spelled there.
    ///
    /// Pure, so the two decisions that make or break the banner are pinned without a canvas:
    /// a generated zone (<c>zone_100_50</c>) is a coordinate, not a place, and announcing it
    /// teaches the player to ignore the banner; and a place already announced is announced
    /// again only after a real absence, or crossing a border twice while fighting on it
    /// writes the same name three times in ten seconds.
    /// </summary>
    public static class ZoneBannerRules
    {
        /// <summary>Seconds away from a zone before its name is shown again.</summary>
        public const float RevisitAfterSeconds = 300f;

        private static readonly Regex Generated = new Regex(@"^zone_-?\d+_-?\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>True for a zone the database named by its grid offset rather than by a person.</summary>
        public static bool IsGeneratedName(string zone)
            => !string.IsNullOrWhiteSpace(zone) && Generated.IsMatch(zone.Trim());

        /// <summary>
        /// The name as the banner spells it: the overlay suffix dropped, separators to spaces,
        /// upper case — the pixel face has capitals only.
        /// </summary>
        public static string Humanize(string zone)
        {
            if (string.IsNullOrWhiteSpace(zone)) return "";
            string s = zone.Trim();
            const string overlay = ".overlay";
            if (s.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 5);
            if (s.EndsWith(overlay, System.StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - overlay.Length);

            var sb = new StringBuilder(s.Length);
            bool space = false;
            foreach (char c in s)
            {
                if (c == '_' || c == '-' || c == ' ')
                {
                    if (sb.Length > 0) space = true;
                    continue;
                }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Whether entering <paramref name="zone"/> at <paramref name="now"/> shows the banner,
        /// given when each zone was last shown. Records the showing when it answers yes.
        /// </summary>
        public static bool ShouldAnnounce(string zone, float now, IDictionary<string, float> lastShown)
        {
            if (string.IsNullOrWhiteSpace(zone) || IsGeneratedName(zone)) return false;
            if (lastShown != null && lastShown.TryGetValue(zone, out float at) && now - at < RevisitAfterSeconds)
                return false;
            if (lastShown != null) lastShown[zone] = now;
            return true;
        }
    }
}
