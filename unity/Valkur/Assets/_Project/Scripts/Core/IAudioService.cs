using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// Audio service interface defined in Core so any layer can request audio
    /// without cross-asmdef reflection. Implemented by AudioManager in Infrastructure.
    /// </summary>
    public interface IAudioService
    {
        void PlayMusic(AudioClip clip);
        void StopMusic();
        void PlaySFX(AudioClip clip, float volumeScale = 1f);
        void PlaySFXAtPosition(AudioClip clip, Vector3 position, float volumeScale = 1f);
        void SetMusicVolume(float vol);
        void SetSFXVolume(float vol);
    }
}
