using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// A single SFX entry in the audio catalog.
    /// Maps to one entry in Python audio.json sfx_map.
    /// </summary>
    [Serializable]
    public class SfxEntry
    {
        [Tooltip("Unique identifier matching Python sfx_map key (e.g. 'sword_clash_1')")]
        public string id;

        [Tooltip("AudioClip asset for this SFX")]
        public AudioClip clip;

        [Tooltip("Volume group: sfx, ui, combat, ambient")]
        public string group = "sfx";
    }
}
