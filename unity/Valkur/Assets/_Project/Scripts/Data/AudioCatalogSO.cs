using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Central audio catalog ScriptableObject.
    /// Mirrors Python audio.json: tracks, sfx_map, defaults, scoped overrides.
    /// Created via editor tool "Valkur > Audio > Import Catalog from Python JSON".
    /// </summary>
    [CreateAssetMenu(fileName = "AudioCatalog", menuName = "Valkur/Audio/Audio Catalog")]
    public class AudioCatalogSO : ScriptableObject
    {
        // ── Music Tracks ─────────────────────────────────────────────────────
        [Header("Music Tracks")]
        [Tooltip("All music tracks; maps to Python audio.json 'tracks'")]
        [SerializeField] private MusicTrackEntry[] tracks = Array.Empty<MusicTrackEntry>();

        // ── SFX Map ──────────────────────────────────────────────────────────
        [Header("SFX")]
        [Tooltip("All SFX entries; maps to Python audio.json 'sfx_map'")]
        [SerializeField] private SfxEntry[] sfxEntries = Array.Empty<SfxEntry>();

        // ── Defaults ─────────────────────────────────────────────────────────
        [Header("Music Defaults")]
        [Tooltip("Track ID played on menu/boot (Python: menu_intro)")]
        [SerializeField] private string startupTrackId = "menu_intro";

        [Tooltip("Default in-game track ID (Python: main_theme)")]
        [SerializeField] private string ingameTrackId = "main_theme";

        [Tooltip("Playlist track IDs for in-game shuffle (Python: 13 pepitoria tracks)")]
        [SerializeField] private string[] ingamePlaylist = Array.Empty<string>();

        [Tooltip("Seconds between playlist track changes (Python: 120)")]
        [SerializeField] private float playlistIntervalSec = 120f;

        [Tooltip("Playlist mode: shuffle or sequential")]
        [SerializeField] private bool playlistShuffle = true;

        [Tooltip("Crossfade duration in seconds (Python: 0.6)")]
        [SerializeField] private float crossfadeSec = 0.6f;

        [Tooltip("Menu fade-out duration in seconds (Python: 0.5)")]
        [SerializeField] private float menuFadeOutSec = 0.5f;

        // ── Ambient Defaults ─────────────────────────────────────────────────
        [Header("Ambient Defaults")]
        [Tooltip("Default ambient SFX choice IDs")]
        [SerializeField] private string[] defaultAmbientChoices = Array.Empty<string>();

        [Tooltip("Default min interval between ambient sounds (Python: 6.0)")]
        [SerializeField] private float defaultAmbientMinInterval = 6f;

        [Tooltip("Default max interval between ambient sounds (Python: 18.0)")]
        [SerializeField] private float defaultAmbientMaxInterval = 18f;

        // ── Ducking ──────────────────────────────────────────────────────────
        [Header("Ducking")]
        [Tooltip("Music attenuation in dB when ducking (Python: -4.0)")]
        [SerializeField] private float duckingAmountDb = -4f;

        [Tooltip("Ducking hold time in ms (Python: 250)")]
        [SerializeField] private float duckingHoldMs = 250f;

        [Tooltip("Ducking release time in ms (Python: 200)")]
        [SerializeField] private float duckingReleaseMs = 200f;

        [Tooltip("SFX ID prefixes that auto-trigger ducking")]
        [SerializeField] private string[] duckingPrefixes = { "sword_clash_", "fireball" };

        // ── Scoped Overrides ─────────────────────────────────────────────────
        [Header("Music Scope Overrides")]
        [Tooltip("Per-zone/level/biome music overrides")]
        [SerializeField] private MusicScopeOverride[] musicOverrides = Array.Empty<MusicScopeOverride>();

        [Header("Ambient Scope Overrides")]
        [Tooltip("Per-zone/biome ambient sound overrides")]
        [SerializeField] private AmbientScopeEntry[] ambientOverrides = Array.Empty<AmbientScopeEntry>();

        // ── Runtime Lookup ───────────────────────────────────────────────────
        private Dictionary<string, MusicTrackEntry> _trackLookup;
        private Dictionary<string, SfxEntry> _sfxLookup;

        private void BuildLookups()
        {
            if (_trackLookup == null)
            {
                _trackLookup = new Dictionary<string, MusicTrackEntry>(tracks.Length);
                foreach (var t in tracks)
                    if (!string.IsNullOrEmpty(t.id))
                        _trackLookup[t.id] = t;
            }
            if (_sfxLookup == null)
            {
                _sfxLookup = new Dictionary<string, SfxEntry>(sfxEntries.Length);
                foreach (var s in sfxEntries)
                    if (!string.IsNullOrEmpty(s.id))
                        _sfxLookup[s.id] = s;
            }
        }

        /// <summary>Clear cached lookups (call after editor changes).</summary>
        public void InvalidateCache()
        {
            _trackLookup = null;
            _sfxLookup = null;
        }

        // ── Public API ───────────────────────────────────────────────────────

        public MusicTrackEntry GetTrack(string trackId)
        {
            BuildLookups();
            _trackLookup.TryGetValue(trackId, out var entry);
            return entry;
        }

        public AudioClip GetTrackClip(string trackId)
        {
            return GetTrack(trackId)?.clip;
        }

        public SfxEntry GetSfx(string sfxId)
        {
            BuildLookups();
            _sfxLookup.TryGetValue(sfxId, out var entry);
            return entry;
        }

        public AudioClip GetSfxClip(string sfxId)
        {
            return GetSfx(sfxId)?.clip;
        }

        // ── Property Accessors ───────────────────────────────────────────────
        public string StartupTrackId       => startupTrackId;
        public string IngameTrackId        => ingameTrackId;
        public string[] IngamePlaylist     => ingamePlaylist;
        public float PlaylistIntervalSec   => playlistIntervalSec;
        public bool PlaylistShuffle        => playlistShuffle;
        public float CrossfadeSec          => crossfadeSec;
        public float MenuFadeOutSec        => menuFadeOutSec;

        public string[] DefaultAmbientChoices    => defaultAmbientChoices;
        public float DefaultAmbientMinInterval   => defaultAmbientMinInterval;
        public float DefaultAmbientMaxInterval   => defaultAmbientMaxInterval;

        public float DuckingAmountDb  => duckingAmountDb;
        public float DuckingHoldMs    => duckingHoldMs;
        public float DuckingReleaseMs => duckingReleaseMs;
        public string[] DuckingPrefixes => duckingPrefixes;

        public MusicScopeOverride[] MusicOverrides  => musicOverrides;
        public AmbientScopeEntry[] AmbientOverrides => ambientOverrides;
        public MusicTrackEntry[] Tracks             => tracks;
        public SfxEntry[] SfxEntries                => sfxEntries;

        // ── Scope Resolution (zone > level > biome > defaults) ───────────────

        /// <summary>
        /// Resolve the music track for a given context.
        /// Priority: zoneName > levelName > biomeName > ingameTrackId default.
        /// Mirrors Python AudioSystem._resolve_music_scope().
        /// </summary>
        public string ResolveTrackId(string zoneName = null, string levelName = null, string biomeName = null)
        {
            if (musicOverrides != null)
            {
                // Zone first
                if (!string.IsNullOrEmpty(zoneName))
                    foreach (var o in musicOverrides)
                        if (o.scope == MusicScopeOverride.ScopeType.Zone &&
                            string.Equals(o.scopeName, zoneName, StringComparison.OrdinalIgnoreCase))
                            return o.trackId;
                // Level
                if (!string.IsNullOrEmpty(levelName))
                    foreach (var o in musicOverrides)
                        if (o.scope == MusicScopeOverride.ScopeType.Level &&
                            string.Equals(o.scopeName, levelName, StringComparison.OrdinalIgnoreCase))
                            return o.trackId;
                // Biome
                if (!string.IsNullOrEmpty(biomeName))
                    foreach (var o in musicOverrides)
                        if (o.scope == MusicScopeOverride.ScopeType.Biome &&
                            string.Equals(o.scopeName, biomeName, StringComparison.OrdinalIgnoreCase))
                            return o.trackId;
            }
            return ingameTrackId;
        }

        /// <summary>
        /// Resolve ambient config for a given scope.
        /// Falls back to default ambient settings.
        /// </summary>
        public void ResolveAmbient(string scopeName, out string[] choices, out float minInterval, out float maxInterval)
        {
            if (ambientOverrides != null && !string.IsNullOrEmpty(scopeName))
            {
                foreach (var a in ambientOverrides)
                {
                    if (string.Equals(a.scopeName, scopeName, StringComparison.OrdinalIgnoreCase))
                    {
                        choices = a.choices;
                        minInterval = a.minInterval;
                        maxInterval = a.maxInterval;
                        return;
                    }
                }
            }
            choices = defaultAmbientChoices;
            minInterval = defaultAmbientMinInterval;
            maxInterval = defaultAmbientMaxInterval;
        }

        /// <summary>
        /// Build playlist AudioClip array from playlist track IDs.
        /// </summary>
        public AudioClip[] BuildPlaylistClips()
        {
            BuildLookups();
            var clips = new List<AudioClip>();
            foreach (var id in ingamePlaylist)
            {
                if (_trackLookup.TryGetValue(id, out var entry) && entry.clip != null)
                    clips.Add(entry.clip);
            }
            return clips.ToArray();
        }

#if UNITY_EDITOR
        // ── Editor Setters (used by importer) ────────────────────────────────
        public void EditorSetTracks(MusicTrackEntry[] t)  { tracks = t; }
        public void EditorSetSfx(SfxEntry[] s)            { sfxEntries = s; }
        public void EditorSetStartupTrackId(string id)    { startupTrackId = id; }
        public void EditorSetIngameTrackId(string id)     { ingameTrackId = id; }
        public void EditorSetIngamePlaylist(string[] pl)   { ingamePlaylist = pl; }
        public void EditorSetPlaylistInterval(float s)    { playlistIntervalSec = s; }
        public void EditorSetPlaylistShuffle(bool b)      { playlistShuffle = b; }
        public void EditorSetCrossfadeSec(float s)        { crossfadeSec = s; }
        public void EditorSetMenuFadeOutSec(float s)      { menuFadeOutSec = s; }
        public void EditorSetDefaultAmbientChoices(string[] c) { defaultAmbientChoices = c; }
        public void EditorSetDefaultAmbientMin(float v)   { defaultAmbientMinInterval = v; }
        public void EditorSetDefaultAmbientMax(float v)   { defaultAmbientMaxInterval = v; }
        public void EditorSetDuckingAmountDb(float v)     { duckingAmountDb = v; }
        public void EditorSetDuckingHoldMs(float v)       { duckingHoldMs = v; }
        public void EditorSetDuckingReleaseMs(float v)    { duckingReleaseMs = v; }
        public void EditorSetDuckingPrefixes(string[] p)  { duckingPrefixes = p; }
        public void EditorSetMusicOverrides(MusicScopeOverride[] o) { musicOverrides = o; }
        public void EditorSetAmbientOverrides(AmbientScopeEntry[] o) { ambientOverrides = o; }
#endif
    }
}
