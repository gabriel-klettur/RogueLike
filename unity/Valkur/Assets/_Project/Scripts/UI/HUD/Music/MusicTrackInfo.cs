using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Everything the music panel SAYS about a track, derived from the catalog entry: which
    /// zone's list it belongs to, where in that list it sits, and the words for its tempo and
    /// key. Pure, so every rule here is an EditMode test away.
    ///
    /// <para><b>The zone comes from the TITLE, not the id.</b> The ids are not regular
    /// (<c>main_theme</c> is Pepitoria's, <c>menu_intro</c> is the menu's) while every title is
    /// "&lt;Zone&gt; Main Theme" or "&lt;Zone&gt; Theme N" — so the title is the one field that
    /// already names the list a track belongs to.</para>
    /// </summary>
    public static class MusicTrackInfo
    {
        /// <summary>The zone part of a title: "Pepitoria Theme 7" -> "Pepitoria".</summary>
        public static string GroupOf(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;
            string t = title.Trim();
            int cut = t.IndexOf(" Main Theme", System.StringComparison.OrdinalIgnoreCase);
            if (cut < 0) cut = t.IndexOf(" Theme", System.StringComparison.OrdinalIgnoreCase);
            if (cut > 0) return t.Substring(0, cut).Trim();
            int space = t.IndexOf(' ');
            return space > 0 ? t.Substring(0, space) : t;
        }

        /// <summary>
        /// 1-based position of <paramref name="trackId"/> among the catalog tracks of the same
        /// group, in catalog order, and the size of that group. (0, 0) when unknown.
        /// </summary>
        public static Vector2Int PositionInGroup(IList<MusicTrackEntry> tracks, string trackId)
        {
            if (tracks == null || string.IsNullOrEmpty(trackId)) return Vector2Int.zero;
            string group = null;
            for (int i = 0; i < tracks.Count; i++)
                if (tracks[i] != null && tracks[i].id == trackId) { group = GroupOf(tracks[i].title); break; }
            if (string.IsNullOrEmpty(group)) return Vector2Int.zero;

            int index = 0, count = 0;
            for (int i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                if (t == null || !string.Equals(GroupOf(t.title), group, System.StringComparison.OrdinalIgnoreCase)) continue;
                count++;
                if (t.id == trackId) index = count;
            }
            return new Vector2Int(index, count);
        }

        /// <summary>The medallion's picture for a zone name.</summary>
        public static MusicSigil SigilOf(string group)
        {
            switch ((group ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "pepitoria": return MusicSigil.Town;
                case "forest":    return MusicSigil.Forest;
                case "desert":    return MusicSigil.Desert;
                case "covetus":   return MusicSigil.Crypt;
                default:          return MusicSigil.Note;
            }
        }

        /// <summary>"m:ss", or "h:mm:ss" past an hour. Negative and NaN read as zero.</summary>
        public static string FormatTime(float seconds)
        {
            if (float.IsNaN(seconds) || seconds < 0f) seconds = 0f;
            int total = Mathf.FloorToInt(seconds);
            int h = total / 3600, m = (total / 60) % 60, s = total % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
        }

        /// <summary>
        /// The key in Spanish solfège: "A minor" -> "La menor", "F# major" -> "Fa# mayor".
        /// Empty in, empty out; anything unrecognised is returned as authored.
        /// </summary>
        public static string KeyInSpanish(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;
            var parts = key.Trim().Split(' ');
            if (parts.Length != 2) return key;
            string note = parts[0], mode = parts[1].ToLowerInvariant();
            string sharp = note.EndsWith("#") ? "#" : string.Empty;
            string root = sharp.Length > 0 ? note.Substring(0, note.Length - 1) : note;
            string name;
            switch (root.ToUpperInvariant())
            {
                case "C": name = "Do"; break;
                case "D": name = "Re"; break;
                case "E": name = "Mi"; break;
                case "F": name = "Fa"; break;
                case "G": name = "Sol"; break;
                case "A": name = "La"; break;
                case "B": name = "Si"; break;
                default: return key;
            }
            string modeName = mode == "major" ? "mayor" : mode == "minor" ? "menor" : null;
            return modeName == null ? key : name + sharp + " " + modeName;
        }

        /// <summary>
        /// Below this, the analysed key is not shown. The estimate is the gap between the best
        /// and second-best key profile, and across the shipped 24 it runs from 0.007 to 0.30:
        /// eleven sit under 0.1, where the "key" is noise. A panel that states noise as a fact is
        /// less honest than one that says nothing.
        /// </summary>
        public const float KeyConfidenceFloor = 0.1f;

        /// <summary>The track's key when the analysis is sure enough to say it, else empty.</summary>
        public static string TrustedKey(MusicTrackEntry track)
            => track != null && track.keyConfidence >= KeyConfidenceFloor ? track.key : string.Empty;

        /// <summary>
        /// Cuts <paramref name="text"/> to fit <paramref name="maxWidth"/> texels in a pixel face,
        /// ending it with "..." when it had to be cut.
        /// </summary>
        public static string FitToWidth(string text, int maxWidth, System.Func<string, int> measure)
        {
            if (string.IsNullOrEmpty(text) || measure == null || measure(text) <= maxWidth) return text ?? string.Empty;
            for (int len = text.Length - 1; len > 0; len--)
            {
                string candidate = text.Substring(0, len).TrimEnd() + "...";
                if (measure(candidate) <= maxWidth) return candidate;
            }
            return string.Empty;
        }
    }
}
