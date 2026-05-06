using System;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Editors.Boss
{
    /// <summary>
    /// Catalog discovery + cached lookups for the Boss Editor.
    ///
    /// The editor needs the live <see cref="SpellCatalog"/> (for the CastSpell
    /// cue's spell-key dropdown) and the <see cref="AudioCatalogSO"/> (for
    /// importing the per-beat onset map of a music track into a chart). Both
    /// are scanned via <c>AssetDatabase.FindAssets</c> on first access and
    /// cached for the lifetime of the editor — production code that reads
    /// these catalogs at runtime goes through the ServiceLocator-bound
    /// references on the live boss, but this in-game editor authors .asset
    /// files at design-time and wants to find catalogs without dragging
    /// inspector references onto the editor singleton.
    /// </summary>
    public partial class BossEditorManager
        : SingletonMonoBehaviour<BossEditorManager>, GameEditorManager.IGameEditor
    {
        private SpellCatalog   _spellCatalog;
        private AudioCatalogSO _audioCatalog;
        private bool _spellCatalogScanned;
        private bool _audioCatalogScanned;

        // ── Spell catalog ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns the cached catalog or scans the project for the first
        /// <see cref="SpellCatalog"/> asset on first call. Returns null if
        /// none exists (the dropdown silently falls back to the text field).
        /// </summary>
        public SpellCatalog GetSpellCatalog()
        {
            if (_spellCatalog != null) return _spellCatalog;
            if (_spellCatalogScanned)  return null;
            _spellCatalogScanned = true;
#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:SpellCatalog");
            if (guids.Length == 0) return null;
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
            _spellCatalog = UnityEditor.AssetDatabase.LoadAssetAtPath<SpellCatalog>(path);
#endif
            return _spellCatalog;
        }

        /// <summary>
        /// Sorted list of spell keys for the dropdown. Empty if no catalog or
        /// no spells. Pre-pends an empty entry so the dropdown can express
        /// "no spell selected yet".
        /// </summary>
        public string[] GetSpellKeysForDropdown()
        {
            var cat = GetSpellCatalog();
            if (cat == null) return Array.Empty<string>();
            string[] keys = cat.GetAllKeys();
            if (keys == null || keys.Length == 0) return Array.Empty<string>();
            Array.Sort(keys, StringComparer.OrdinalIgnoreCase);
            return keys;
        }

        // ── Audio catalog ──────────────────────────────────────────────────────

        public AudioCatalogSO GetAudioCatalog()
        {
            if (_audioCatalog != null) return _audioCatalog;
            if (_audioCatalogScanned)  return null;
            _audioCatalogScanned = true;
#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:AudioCatalogSO");
            if (guids.Length == 0) return null;
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
            _audioCatalog = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioCatalogSO>(path);
#endif
            return _audioCatalog;
        }

        // ── Import beats from MusicTrackEntry.beatTimes ────────────────────────

        /// <summary>
        /// Populates the selected chart with cues anchored at every beat of
        /// the chart's target track, folded into the chart's loop window. New
        /// cues default to <see cref="BossCueType.CastSpell"/> with empty
        /// targetKey so the author can fill them in via the dropdown.
        ///
        /// Skips cues whose (bar, beat) already exists so the operation is
        /// safe to run multiple times — it tops up the chart rather than
        /// duplicating existing cues.
        /// </summary>
        public void ImportBeatsFromActiveTrack()
        {
            if (_selectedChart == null)
            {
                SetStatus("No chart selected.");
                return;
            }
            string trackId = _selectedChart.musicTrackId;
            if (string.IsNullOrWhiteSpace(trackId))
            {
                SetStatus("Set the chart's Track ID first.");
                return;
            }

            var catalog = GetAudioCatalog();
            if (catalog == null)
            {
                SetStatus("No AudioCatalogSO found in the project.");
                return;
            }
            MusicTrackEntry track = catalog.GetTrack(trackId);
            if (track == null)
            {
                SetStatus($"Track '{trackId}' not found in audio catalog.");
                return;
            }
            if (track.beatTimes == null || track.beatTimes.Length == 0)
            {
                SetStatus($"Track '{trackId}' has no analysed beatTimes — run analyze_music.py first.");
                return;
            }

            int beatsPerBar = Mathf.Max(1, track.beatsPerBar);
            int barsPerLoop = Mathf.Max(1, _selectedChart.barsPerLoop);
            int slotsInLoop = beatsPerBar * barsPerLoop;
            int toImport    = Mathf.Min(track.beatTimes.Length, slotsInLoop);

            var existing = new System.Collections.Generic.HashSet<int>();
            foreach (var c in _selectedChart.cues)
                existing.Add(c.bar * beatsPerBar + c.beat);

            var toAdd = new System.Collections.Generic.List<BossCue>();
            for (int i = 0; i < toImport; i++)
            {
                int bar  = i / beatsPerBar;
                int beat = i % beatsPerBar;
                int slot = bar * beatsPerBar + beat;
                if (existing.Contains(slot)) continue;
                toAdd.Add(new BossCue
                {
                    bar          = bar,
                    beat         = beat,
                    beatFraction = 0f,
                    type         = BossCueType.CastSpell,
                    targetKey    = string.Empty,
                    targeting    = BossCueTargeting.ToPlayer,
                    payload      = 0f,
                    note         = "imported from beatTimes",
                });
            }

            if (toAdd.Count == 0)
            {
                SetStatus("All loop slots already have cues — nothing to import.");
                return;
            }

            var chart = _selectedChart;
            int startCount = chart.cues.Count;
            _undo.Do(
                $"Import {toAdd.Count} beats",
                () =>
                {
                    foreach (var c in toAdd) chart.cues.Add(c);
                    MarkDirty(chart);
                    RefreshCuesPanel();
                    RefreshUndoRedoButtons();
                },
                () =>
                {
                    int target = startCount;
                    while (chart.cues.Count > target) chart.cues.RemoveAt(chart.cues.Count - 1);
                    MarkDirty(chart);
                    RefreshCuesPanel();
                    RefreshUndoRedoButtons();
                });

            SetStatus($"Imported {toAdd.Count} cues from track '{trackId}' " +
                      $"({toImport} beats, {beatsPerBar}/bar, {barsPerLoop} bars).");
        }
    }
}
