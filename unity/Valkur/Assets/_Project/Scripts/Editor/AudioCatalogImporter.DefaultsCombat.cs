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
        private static string DetectGroup(string sfxId)
        {
            if (sfxId.StartsWith("ambient_", StringComparison.OrdinalIgnoreCase)) return "ambient";
            if (sfxId.StartsWith("inv_", StringComparison.OrdinalIgnoreCase)) return "ui";
            if (sfxId.StartsWith("menu_", StringComparison.OrdinalIgnoreCase)) return "ui";
            return "sfx";
        }

        // â”€â”€ Defaults Import â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static void ImportDefaults(AudioCatalogSO catalog, PythonAudioJson data)
        {
            if (data.defaults == null) return;

            var music = data.defaults.music;
            if (music != null)
            {
                catalog.EditorSetStartupTrackId(music.startup_track_id ?? "menu_intro");
                catalog.EditorSetIngameTrackId(music.ingame_track_id ?? "main_theme");
                catalog.EditorSetIngamePlaylist(music.ingame_playlist ?? Array.Empty<string>());
                catalog.EditorSetPlaylistInterval(music.playlist_interval_s > 0 ? music.playlist_interval_s : 120f);
                catalog.EditorSetPlaylistShuffle(music.playlist_mode == "shuffle");
                catalog.EditorSetCrossfadeSec(music.crossfade_ms > 0 ? music.crossfade_ms / 1000f : 0.6f);
                catalog.EditorSetMenuFadeOutSec(music.menu_fade_out_ms > 0 ? music.menu_fade_out_ms / 1000f : 0.5f);
            }

            var ambient = data.defaults.ambient;
            if (ambient != null)
            {
                catalog.EditorSetDefaultAmbientChoices(ambient.choices ?? Array.Empty<string>());
                catalog.EditorSetDefaultAmbientMin(ambient.min_interval > 0 ? ambient.min_interval : 6f);
                catalog.EditorSetDefaultAmbientMax(ambient.max_interval > 0 ? ambient.max_interval : 18f);
            }

            var ducking = data.defaults.ducking;
            if (ducking != null)
            {
                catalog.EditorSetDuckingAmountDb(ducking.amount_db);
                catalog.EditorSetDuckingHoldMs(ducking.hold_ms > 0 ? ducking.hold_ms : 250f);
                catalog.EditorSetDuckingReleaseMs(ducking.release_ms > 0 ? ducking.release_ms : 200f);
                catalog.EditorSetDuckingPrefixes(ducking.auto_on_sfx_prefixes ?? new[] { "sword_clash_", "fireball" });
            }
        }

        // â”€â”€ Scope Overrides Import â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static void ImportScopeOverrides(AudioCatalogSO catalog, PythonAudioJson data)
        {
            var musicOverrides = new List<MusicScopeOverride>();
            var ambientOverrides = new List<AmbientScopeEntry>();

            // Biomes
            if (data.biomes != null)
            {
                foreach (var kv in data.biomes)
                {
                    if (!string.IsNullOrEmpty(kv.Value.music_track_id))
                    {
                        musicOverrides.Add(new MusicScopeOverride
                        {
                            scope     = MusicScopeOverride.ScopeType.Biome,
                            scopeName = kv.Key,
                            trackId   = kv.Value.music_track_id
                        });
                    }
                    if (kv.Value.ambient != null && kv.Value.ambient.choices != null)
                    {
                        ambientOverrides.Add(new AmbientScopeEntry
                        {
                            scopeName   = kv.Key,
                            choices     = kv.Value.ambient.choices,
                            minInterval = kv.Value.ambient.min_interval > 0 ? kv.Value.ambient.min_interval : 6f,
                            maxInterval = kv.Value.ambient.max_interval > 0 ? kv.Value.ambient.max_interval : 18f
                        });
                    }
                }
            }

            // Levels
            if (data.levels != null)
            {
                foreach (var kv in data.levels)
                {
                    if (!string.IsNullOrEmpty(kv.Value.music_track_id))
                    {
                        musicOverrides.Add(new MusicScopeOverride
                        {
                            scope     = MusicScopeOverride.ScopeType.Level,
                            scopeName = kv.Key,
                            trackId   = kv.Value.music_track_id
                        });
                    }
                }
            }

            // Zones
            if (data.zones != null)
            {
                foreach (var kv in data.zones)
                {
                    if (!string.IsNullOrEmpty(kv.Value.music_track_id))
                    {
                        musicOverrides.Add(new MusicScopeOverride
                        {
                            scope     = MusicScopeOverride.ScopeType.Zone,
                            scopeName = kv.Key,
                            trackId   = kv.Value.music_track_id
                        });
                    }
                }
            }

            catalog.EditorSetMusicOverrides(musicOverrides.ToArray());
            catalog.EditorSetAmbientOverrides(ambientOverrides.ToArray());
        }

        // â”€â”€ Combat SFX Import â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static void ImportCombatSfx(CombatSfxConfigSO config, PythonAudioJson data)
        {
            if (data.sfx_map == null) return;

            // Player damage: player_damage_1 through player_damage_22
            var playerDamage = new List<string>();
            for (int i = 1; i <= 22; i++)
            {
                string id = $"player_damage_{i}";
                if (data.sfx_map.ContainsKey(id)) playerDamage.Add(id);
            }
            config.EditorSetPlayerDamage(playerDamage.ToArray());

            // Slash: sword_clash_1 through sword_clash_10
            var slash = new List<string>();
            for (int i = 1; i <= 10; i++)
            {
                string id = $"sword_clash_{i}";
                if (data.sfx_map.ContainsKey(id)) slash.Add(id);
            }
            config.EditorSetSlashSfx(slash.ToArray());

            // Fireball
            config.EditorSetFireballSfx("fireball");

            // NPC archetypes
            var archetypes = new List<CombatSfxConfigSO.ArchetypeSfxMap>();

            // Barbol
            var barbolDamage = new List<string>();
            var barbolAttack = new List<string>();
            foreach (var key in data.sfx_map.Keys)
            {
                if (key.StartsWith("barbol_damage_", StringComparison.OrdinalIgnoreCase))
                    barbolDamage.Add(key);
                else if (key.StartsWith("barbol_attack_", StringComparison.OrdinalIgnoreCase))
                    barbolAttack.Add(key);
            }
            if (barbolDamage.Count > 0 || barbolAttack.Count > 0)
            {
                archetypes.Add(new CombatSfxConfigSO.ArchetypeSfxMap
                {
                    archetype    = "barbol",
                    damageSfxIds = barbolDamage.ToArray(),
                    attackSfxIds = barbolAttack.ToArray()
                });
            }

            config.EditorSetNpcArchetypes(archetypes.ToArray());
        }

        // â”€â”€ Asset Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static T LoadOrCreateAsset<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            string dir = Path.GetDirectoryName(path);
            if (!AssetDatabase.IsValidFolder(dir))
            {
                string parent = Path.GetDirectoryName(dir);
                string folder = Path.GetFileName(dir);
                AssetDatabase.CreateFolder(parent, folder);
            }

            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
