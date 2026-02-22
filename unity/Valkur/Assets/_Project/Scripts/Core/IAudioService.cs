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
        void PlaySFX(AudioClip clip, Vector3 position = default);
    }
}
