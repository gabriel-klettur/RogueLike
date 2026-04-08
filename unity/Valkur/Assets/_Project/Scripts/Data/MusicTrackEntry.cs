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
    }
}
