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
        // â”€â”€ JSON Data Classes â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // JsonUtility doesn't support Dictionary, so we parse manually with
        // Unity's built-in JSON + a simple key-value extraction approach.

        private static PythonAudioJson ParseManual(string json)
        {
            var result = new PythonAudioJson();

            // Use a more robust approach: parse with MiniJSON-style
            var root = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (root == null) return result;

            // Tracks
            result.tracks = new Dictionary<string, TrackData>();
            if (root.TryGetValue("tracks", out var tracksObj) && tracksObj is Dictionary<string, object> tracksDict)
            {
                foreach (var kv in tracksDict)
                {
                    var td = new TrackData();
                    if (kv.Value is Dictionary<string, object> tDict)
                    {
                        td.path  = GetString(tDict, "path");
                        td.title = GetString(tDict, "title");
                    }
                    result.tracks[kv.Key] = td;
                }
            }

            // SFX map
            result.sfx_map = new Dictionary<string, SfxData>();
            if (root.TryGetValue("sfx_map", out var sfxObj) && sfxObj is Dictionary<string, object> sfxDict)
            {
                foreach (var kv in sfxDict)
                {
                    var sd = new SfxData();
                    if (kv.Value is Dictionary<string, object> sDict)
                        sd.path = GetString(sDict, "path");
                    result.sfx_map[kv.Key] = sd;
                }
            }

            // Defaults
            result.defaults = new DefaultsData();
            if (root.TryGetValue("defaults", out var defObj) && defObj is Dictionary<string, object> defDict)
            {
                // Music defaults
                if (defDict.TryGetValue("music", out var mObj) && mObj is Dictionary<string, object> mDict)
                {
                    result.defaults.music = new MusicDefaults
                    {
                        startup_track_id   = GetString(mDict, "startup_track_id"),
                        ingame_track_id    = GetString(mDict, "ingame_track_id"),
                        ingame_playlist    = GetStringArray(mDict, "ingame_playlist"),
                        playlist_interval_s = GetFloat(mDict, "playlist_interval_s"),
                        playlist_mode      = GetString(mDict, "playlist_mode"),
                        crossfade_ms       = GetFloat(mDict, "crossfade_ms"),
                        menu_fade_out_ms   = GetFloat(mDict, "menu_fade_out_ms")
                    };
                }

                // Ambient defaults
                if (defDict.TryGetValue("ambient", out var aObj) && aObj is Dictionary<string, object> aDict)
                {
                    result.defaults.ambient = new AmbientDefaults
                    {
                        choices      = GetStringArray(aDict, "choices"),
                        min_interval = GetFloat(aDict, "min_interval"),
                        max_interval = GetFloat(aDict, "max_interval")
                    };
                }

                // Ducking defaults
                if (defDict.TryGetValue("ducking", out var dObj) && dObj is Dictionary<string, object> dDict)
                {
                    result.defaults.ducking = new DuckingDefaults
                    {
                        amount_db            = GetFloat(dDict, "amount_db"),
                        hold_ms              = GetFloat(dDict, "hold_ms"),
                        release_ms           = GetFloat(dDict, "release_ms"),
                        auto_on_sfx_prefixes = GetStringArray(dDict, "auto_on_sfx_prefixes")
                    };
                }
            }

            // Biomes
            result.biomes = new Dictionary<string, BiomeData>();
            if (root.TryGetValue("biomes", out var bioObj) && bioObj is Dictionary<string, object> bioDict)
            {
                foreach (var kv in bioDict)
                {
                    var bd = new BiomeData();
                    if (kv.Value is Dictionary<string, object> bDict)
                    {
                        bd.music_track_id = GetString(bDict, "music_track_id");
                        if (bDict.TryGetValue("ambient", out var abObj) && abObj is Dictionary<string, object> abDict)
                        {
                            bd.ambient = new AmbientDefaults
                            {
                                choices      = GetStringArray(abDict, "choices"),
                                min_interval = GetFloat(abDict, "min_interval"),
                                max_interval = GetFloat(abDict, "max_interval")
                            };
                        }
                    }
                    result.biomes[kv.Key] = bd;
                }
            }

            // Levels
            result.levels = new Dictionary<string, LevelData>();
            if (root.TryGetValue("levels", out var lvlObj) && lvlObj is Dictionary<string, object> lvlDict)
            {
                foreach (var kv in lvlDict)
                {
                    var ld = new LevelData();
                    if (kv.Value is Dictionary<string, object> lDict)
                        ld.music_track_id = GetString(lDict, "music_track_id");
                    result.levels[kv.Key] = ld;
                }
            }

            // Zones
            result.zones = new Dictionary<string, ZoneData>();
            if (root.TryGetValue("zones", out var zObj) && zObj is Dictionary<string, object> zDict)
            {
                foreach (var kv in zDict)
                {
                    var zd = new ZoneData();
                    if (kv.Value is Dictionary<string, object> zoneDict)
                        zd.music_track_id = GetString(zoneDict, "music_track_id");
                    result.zones[kv.Key] = zd;
                }
            }

            return result;
        }

        private static string GetString(Dictionary<string, object> d, string key)
        {
            return d.TryGetValue(key, out var v) && v is string s ? s : null;
        }

        private static float GetFloat(Dictionary<string, object> d, string key)
        {
            if (!d.TryGetValue(key, out var v)) return 0f;
            if (v is double dv) return (float)dv;
            if (v is long lv) return lv;
            if (v is float fv) return fv;
            return 0f;
        }

        private static string[] GetStringArray(Dictionary<string, object> d, string key)
        {
            if (!d.TryGetValue(key, out var v)) return Array.Empty<string>();
            if (v is List<object> list)
            {
                var result = new string[list.Count];
                for (int i = 0; i < list.Count; i++)
                    result[i] = list[i]?.ToString() ?? "";
                return result;
            }
            return Array.Empty<string>();
        }

        // â”€â”€ Data model for Python audio.json â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Serializable]
        private class PythonAudioJson
        {
            public Dictionary<string, TrackData> tracks;
            public Dictionary<string, SfxData> sfx_map;
            public DefaultsData defaults;
            public Dictionary<string, BiomeData> biomes;
            public Dictionary<string, LevelData> levels;
            public Dictionary<string, ZoneData> zones;
        }

        [Serializable] private class TrackData   { public string path; public string title; }
        [Serializable] private class SfxData     { public string path; }
        [Serializable] private class LevelData   { public string music_track_id; }
        [Serializable] private class ZoneData    { public string music_track_id; }

        [Serializable]
        private class BiomeData
        {
            public string music_track_id;
            public AmbientDefaults ambient;
        }

        [Serializable]
        private class DefaultsData
        {
            public MusicDefaults music;
            public AmbientDefaults ambient;
            public DuckingDefaults ducking;
        }

        [Serializable]
        private class MusicDefaults
        {
            public string startup_track_id;
            public string ingame_track_id;
            public string[] ingame_playlist;
            public float playlist_interval_s;
            public string playlist_mode;
            public float crossfade_ms;
            public float menu_fade_out_ms;
        }

        [Serializable]
        private class AmbientDefaults
        {
            public string[] choices;
            public float min_interval;
            public float max_interval;
        }

        [Serializable]
        private class DuckingDefaults
        {
            public float amount_db;
            public float hold_ms;
            public float release_ms;
            public string[] auto_on_sfx_prefixes;
        }
    }
}
