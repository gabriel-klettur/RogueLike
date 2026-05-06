using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Static integrity checks for an <see cref="AudioCatalogSO"/>:
    /// duplicate ids, missing AudioClips, dangling track-id references in
    /// playlists / scope overrides / ambient choices.
    /// </summary>
    public static class AudioCatalogValidator
    {
        [MenuItem("Valkur/Audio/Validate Catalog")]
        public static void ValidateMenu()
        {
            var catalog = AudioCatalogLocator.Find();
            if (catalog != null) Validate(catalog);
        }

        public static void Validate(AudioCatalogSO catalog)
        {
            var issues = new List<string>();
            var trackIds = new HashSet<string>();
            var sfxIds   = new HashSet<string>();

            CollectTrackIssues(catalog, trackIds, issues);
            CollectSfxIssues(catalog, sfxIds, issues);
            CollectDefaultIssues(catalog, trackIds, issues);
            CollectScopeIssues(catalog, trackIds, sfxIds, issues);

            ReportToConsole(catalog, issues);
        }

        private static void CollectTrackIssues(AudioCatalogSO catalog,
            HashSet<string> trackIds, List<string> issues)
        {
            foreach (var t in catalog.Tracks)
            {
                if (string.IsNullOrEmpty(t.id))
                {
                    issues.Add($"Track has empty id (title='{t.title}')");
                    continue;
                }
                if (!trackIds.Add(t.id))
                    issues.Add($"Duplicate track id: '{t.id}'");
                if (t.clip == null)
                    issues.Add($"Track '{t.id}' has no AudioClip assigned");
            }
        }

        private static void CollectSfxIssues(AudioCatalogSO catalog,
            HashSet<string> sfxIds, List<string> issues)
        {
            foreach (var s in catalog.SfxEntries)
            {
                if (string.IsNullOrEmpty(s.id))
                {
                    issues.Add("SFX has empty id");
                    continue;
                }
                if (!sfxIds.Add(s.id))
                    issues.Add($"Duplicate SFX id: '{s.id}'");
                if (s.clip == null)
                    issues.Add($"SFX '{s.id}' has no AudioClip assigned");
            }
        }

        private static void CollectDefaultIssues(AudioCatalogSO catalog,
            HashSet<string> trackIds, List<string> issues)
        {
            if (!string.IsNullOrEmpty(catalog.StartupTrackId) && !trackIds.Contains(catalog.StartupTrackId))
                issues.Add($"Default startup_track_id '{catalog.StartupTrackId}' not found in tracks");
            if (!string.IsNullOrEmpty(catalog.IngameTrackId) && !trackIds.Contains(catalog.IngameTrackId))
                issues.Add($"Default ingame_track_id '{catalog.IngameTrackId}' not found in tracks");

            foreach (var id in catalog.IngamePlaylist)
                if (!string.IsNullOrEmpty(id) && !trackIds.Contains(id))
                    issues.Add($"Playlist references missing track '{id}'");
        }

        private static void CollectScopeIssues(AudioCatalogSO catalog,
            HashSet<string> trackIds, HashSet<string> sfxIds, List<string> issues)
        {
            foreach (var o in catalog.MusicOverrides)
            {
                if (string.IsNullOrEmpty(o.scopeName))
                    issues.Add($"Music override of scope {o.scope} has empty scopeName");
                if (!string.IsNullOrEmpty(o.trackId) && !trackIds.Contains(o.trackId))
                    issues.Add($"Music override {o.scope}/'{o.scopeName}' references missing track '{o.trackId}'");
            }

            foreach (var a in catalog.AmbientOverrides)
            {
                if (a.choices == null) continue;
                foreach (var c in a.choices)
                    if (!string.IsNullOrEmpty(c) && !sfxIds.Contains(c))
                        issues.Add($"Ambient '{a.scopeName}' references missing SFX '{c}'");
            }
        }

        private static void ReportToConsole(AudioCatalogSO catalog, List<string> issues)
        {
            string summary =
                $"Tracks={catalog.Tracks.Length} | " +
                $"SFX={catalog.SfxEntries.Length} | " +
                $"MusicScopes={catalog.MusicOverrides.Length} | " +
                $"AmbientScopes={catalog.AmbientOverrides.Length}";

            if (issues.Count == 0)
            {
                Debug.Log($"[AudioCatalog] Validation OK — {summary}");
                return;
            }

            string list = "• " + string.Join("\n• ", issues);
            Debug.LogWarning(
                $"[AudioCatalog] Validation found {issues.Count} issue(s):\n{summary}\n{list}");
        }
    }
}
