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

        [Tooltip("Loudness of the whole song in 128 equal slices, one byte each as hex (256 " +
                 "characters), baked by analyze_music.py. The music panel draws it as the song's " +
                 "overview: every shipped track is Streaming, which refuses AudioClip.GetData, and " +
                 "the live output is read after the source volume, so without it the overview only " +
                 "existed for what had already been heard — and not at all while muted.")]
        public string envelope = string.Empty;

        /// <summary>
        /// <see cref="envelope"/> decoded to 0..1, or an empty array when it was never baked or
        /// is malformed. Allocates; callers cache the result per track.
        /// </summary>
        public float[] DecodeEnvelope()
        {
            if (string.IsNullOrEmpty(envelope) || (envelope.Length & 1) != 0) return Array.Empty<float>();
            var values = new float[envelope.Length / 2];
            for (int i = 0; i < values.Length; i++)
            {
                int hi = HexValue(envelope[i * 2]), lo = HexValue(envelope[i * 2 + 1]);
                if (hi < 0 || lo < 0) return Array.Empty<float>();
                values[i] = (hi * 16 + lo) / 255f;
            }
            return values;
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }
}
