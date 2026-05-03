using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    public static partial class AudioCatalogImporter
    {
        // â”€â”€ Track Import â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static void ImportTracks(AudioCatalogSO catalog, PythonAudioJson data)
        {
            var entries = new List<MusicTrackEntry>();
            if (data.tracks == null) return;

            foreach (var kv in data.tracks)
            {
                var clip = FindMusicClip(kv.Key);
                entries.Add(new MusicTrackEntry
                {
                    id    = kv.Key,
                    title = kv.Value.title ?? kv.Key,
                    clip  = clip
                });

                if (clip == null)
                    Debug.LogWarning($"[AudioImporter] Music clip not found for track '{kv.Key}'. Expected in {MUSIC_FOLDER}/");
            }

            catalog.EditorSetTracks(entries.ToArray());
        }

        private static AudioClip FindMusicClip(string trackId)
        {
            // Map track IDs to file names
            // Python paths: "assets/audio/music/X.mp3"
            // Unity paths: "Assets/_Project/Audio/Music/X.mp3"
            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { MUSIC_FOLDER });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                string normalizedId = trackId.ToLowerInvariant().Replace("_", " ");

                // Direct name match
                if (fileName.Replace("_", " ").Replace("-", " ") == normalizedId)
                    return AssetDatabase.LoadAssetAtPath<AudioClip>(path);

                // Partial match: trackId maps to filename
                if (TrackIdMatchesFile(trackId, fileName))
                    return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
            return null;
        }

        private static bool TrackIdMatchesFile(string trackId, string fileName)
        {
            // "menu_intro" â†’ "intro_theme"
            // "main_theme" â†’ "pepitoria_main_theme"
            // "pepitoria_theme_2" â†’ "pepitoria_theme_2"
            // "forest_main_theme" â†’ "forest_main_theme"
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "menu_intro", "intro_theme" },
                { "main_theme", "pepitoria_main_theme" }
            };

            if (map.TryGetValue(trackId, out string expected))
                return fileName.Equals(expected, StringComparison.OrdinalIgnoreCase);

            return fileName.Equals(trackId, StringComparison.OrdinalIgnoreCase);
        }

        // â”€â”€ SFX Import â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static void ImportSfxMap(AudioCatalogSO catalog, PythonAudioJson data)
        {
            var entries = new List<SfxEntry>();
            if (data.sfx_map == null) return;

            // Build a lookup of all audio clips under SFX/ and Ambient/
            var clipLookup = BuildSfxClipLookup();

            foreach (var kv in data.sfx_map)
            {
                string sfxId = kv.Key;
                string pythonPath = kv.Value.path;
                string group = DetectGroup(sfxId);

                AudioClip clip = ResolveSfxClip(sfxId, pythonPath, clipLookup);

                entries.Add(new SfxEntry
                {
                    id    = sfxId,
                    clip  = clip,
                    group = group
                });

                if (clip == null)
                    Debug.LogWarning($"[AudioImporter] SFX clip not found for '{sfxId}' (python: {pythonPath})");
            }

            catalog.EditorSetSfx(entries.ToArray());
        }

        private static Dictionary<string, AudioClip> BuildSfxClipLookup()
        {
            var lookup = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
            string[] searchPaths = { SFX_ROOT, AMBIENT_ROOT };

            foreach (var searchPath in searchPaths)
            {
                if (!AssetDatabase.IsValidFolder(searchPath)) continue;
                var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { searchPath });
                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string fileName = Path.GetFileNameWithoutExtension(path);
                    if (!lookup.ContainsKey(fileName))
                        lookup[fileName] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);

                    // Also add with path-based key for disambiguation
                    string relPath = path.Replace("\\", "/");
                    lookup[relPath] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                }
            }
            return lookup;
        }

        private static AudioClip ResolveSfxClip(string sfxId, string pythonPath,
            Dictionary<string, AudioClip> lookup)
        {
            // Try direct file name from python path
            if (!string.IsNullOrEmpty(pythonPath))
            {
                string pyFileName = Path.GetFileNameWithoutExtension(pythonPath);
                if (lookup.TryGetValue(pyFileName, out var clip1)) return clip1;
            }

            // Try sfx_id-based heuristics
            // "sword_clash_1" â†’ file "sword_clash.1" or "sword_clash_1"
            if (lookup.TryGetValue(sfxId, out var clip2)) return clip2;

            // Handle numbered pattern: sword_clash_N â†’ sword_clash.N
            if (sfxId.Contains("_"))
            {
                int lastUnderscore = sfxId.LastIndexOf('_');
                string prefix = sfxId.Substring(0, lastUnderscore);
                string suffix = sfxId.Substring(lastUnderscore + 1);
                string dotName = $"{prefix}.{suffix}";
                if (lookup.TryGetValue(dotName, out var clip3)) return clip3;
            }

            // Handle "player_damage_N" â†’ "NN._damage_grunt_male (1)" or "NN._damage_grunt_male"
            if (sfxId.StartsWith("player_damage_", StringComparison.OrdinalIgnoreCase))
            {
                string numStr = sfxId.Replace("player_damage_", "");
                if (int.TryParse(numStr, out int num))
                {
                    string padded = num.ToString("D2");
                    // Try various filename patterns found in Unity
                    string[] patterns =
                    {
                        $"{padded}._damage_grunt_male (1)",
                        $"{padded}._damage_grunt_male",
                        $"{num}._damage_grunt_male (1)",
                        $"{num}._damage_grunt_male"
                    };
                    foreach (var p in patterns)
                        if (lookup.TryGetValue(p, out var clip4)) return clip4;
                }
            }

            // Handle "barbol_attack_N" â†’ specific file names
            if (sfxId.StartsWith("barbol_attack_", StringComparison.OrdinalIgnoreCase))
            {
                var barbolAttackMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "barbol_attack_1", "animal melee sound" },
                    { "barbol_attack_2", "melee sound" },
                    { "barbol_attack_3", "sword sound" }
                };
                if (barbolAttackMap.TryGetValue(sfxId, out string mappedName))
                    if (lookup.TryGetValue(mappedName, out var clip5)) return clip5;
            }

            // Handle "barbol_damage_1" â†’ "recive_damage"
            if (sfxId.StartsWith("barbol_damage_", StringComparison.OrdinalIgnoreCase))
            {
                if (lookup.TryGetValue("recive_damage", out var clip6)) return clip6;
            }

            // Handle "inv_open" â†’ "open_inventory"
            if (sfxId == "inv_open")
            {
                if (lookup.TryGetValue("open_inventory", out var clip7)) return clip7;
            }

            // Handle ambient_bird_1 â†’ amb_bird_1
            if (sfxId.StartsWith("ambient_", StringComparison.OrdinalIgnoreCase))
            {
                string ambName = sfxId.Replace("ambient_", "amb_");
                if (lookup.TryGetValue(ambName, out var clip8)) return clip8;
            }

            return null;
        }

    }
}
