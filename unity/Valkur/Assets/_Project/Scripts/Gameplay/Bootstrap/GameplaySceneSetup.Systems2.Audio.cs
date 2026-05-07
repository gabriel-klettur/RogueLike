using UnityEngine;
using Valkur.Core;
using Valkur.Infrastructure;

namespace Valkur.Gameplay
{
    public partial class GameplaySceneSetup
    {
        private void EnsureAudioManager()
        {
            // AudioManager is persistent (Persist => true): reuse existing instance
            if (AudioManager.HasInstance)
            {
                var audio = ServiceLocator.Get<IAudioService>();
                if (audio != null)
                {
                    Debug.Log("[GameplaySceneSetup] AudioManager already running (singleton persists).");
                    return;
                }
                // Instance exists but not registered; register it
                ServiceLocator.Register<IAudioService>(AudioManager.Instance);
                Debug.Log("[GameplaySceneSetup] AudioManager found, registered with ServiceLocator.");
                return;
            }

            if (_audioCatalog == null)
            {
                Debug.LogWarning("[GameplaySceneSetup] No AudioCatalog assigned — audio system skipped.");
                return;
            }

            var go = new GameObject("AudioManager");
            var mgr = go.AddComponent<AudioManager>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            mgr.SetCatalog(_audioCatalog);
            Debug.Log("[GameplaySceneSetup] AudioManager created (first instantiation).");
        }

        private void EnsureCombatAudioSystem()
        {
            if (FindObjectOfType<Combat.CombatAudioSystem>() != null) return;

            if (_combatSfxConfig == null)
            {
                Debug.LogWarning("[GameplaySceneSetup] No CombatSfxConfig assigned — combat audio skipped.");
                return;
            }

            var go = new GameObject("CombatAudioSystem");
            var sys = go.AddComponent<Combat.CombatAudioSystem>();
            go.transform.SetParent(GetSceneContainer("[Systems]"), false);
            sys.Initialize(_combatSfxConfig);
            Debug.Log("[GameplaySceneSetup] CombatAudioSystem created.");
        }

        private void EnterGameAudio()
        {
            var audio = ServiceLocator.Get<IAudioService>();
            if (audio == null) return;
            audio.EnterGameAudio();
        }

        // Ambient audio bed driven by DayNightCycle.
        // Phase-driven *particles* were intentionally removed — particles are
        // a Weather concern (Rain / Snow / Dust storm). Phases only modulate
        // colour palette + ambient audio, never the particle layer.
        // Audio component is idempotent so repeated bootstraps are safe.
        private void EnsureDayNightAtmosphere()
        {
            if (FindObjectOfType<Valkur.Gameplay.World.DayNightAmbientAudio>() == null)
            {
                var go = new GameObject("DayNightAmbientAudio");
                go.AddComponent<Valkur.Gameplay.World.DayNightAmbientAudio>();
                go.transform.SetParent(GetSceneContainer("[Systems]"), false);
                Debug.Log("[GameplaySceneSetup] DayNightAmbientAudio created (clips wired via inspector).");
            }
        }

        // Weather orchestrator (Wind / Rain / Snow). The manager creates each
        // effect lazily on first request — at boot we just ensure a single
        // root GameObject exists so the WeatherHUD has somewhere to publish to.
        private void EnsureWeatherManager()
        {
            if (Valkur.Gameplay.World.Weather.WeatherManager.Instance != null) return;
            var go = new GameObject("WeatherManager");
            go.AddComponent<Valkur.Gameplay.World.Weather.WeatherManager>();
            go.transform.SetParent(GetSceneContainer("[VFX]"), false);
            Debug.Log("[GameplaySceneSetup] WeatherManager created (effects spawn lazily on first toggle).");
        }
    }
}
