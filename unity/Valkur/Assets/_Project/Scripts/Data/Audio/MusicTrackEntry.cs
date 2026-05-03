using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// A single music track entry in the audio catalog.
    /// Maps to one entry in Python audio.json tracks.
    /// </summary>
    [Serializable]
    public class MusicTrackEntry
    {
        [Tooltip("Unique identifier matching Python tracks key (e.g. 'main_theme')")]
        public string id;

        [Tooltip("Display title for Now-Playing toast")]
        public string title;

        [Tooltip("AudioClip asset for this track")]
        public AudioClip clip;

        // ── Beat metadata (drives MusicBeatClock + boss choreography) ───────
        [Header("Beat Metadata")]
        [Tooltip("Tempo in beats per minute. 0 disables the beat clock for this track.")]
        [Min(0f)] public float bpm = 0f;

        [Tooltip("Beats per bar (time signature numerator). Default 4/4.")]
        [Min(1)] public int beatsPerBar = 4;

        [Tooltip("Offset in seconds from clip start to the first downbeat (silent intros).")]
        [Min(0f)] public float firstBeatOffsetSec = 0f;

        [Tooltip("Estimated musical key (e.g. 'C major', 'A minor'). Empty if unknown.")]
        public string key = string.Empty;

        [Tooltip("Confidence 0..1 of the key estimate (gap to second-best correlation).")]
        [Range(0f, 1f)] public float keyConfidence = 0f;

        [Tooltip("Per-beat onsets in seconds from clip start, produced by analyze_music.py. " +
                 "When non-empty the MusicBeatClock fires beats from these timestamps directly " +
                 "(precise mode), so boss choreography lands on the actual musical beat even if " +
                 "the song's tempo drifts. Empty = fall back to constant-BPM model.")]
        public float[] beatTimes;
    }
}
