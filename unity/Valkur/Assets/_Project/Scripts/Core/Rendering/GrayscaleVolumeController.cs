using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;          // Volume, VolumeProfile (Core)
using UnityEngine.Rendering.Universal; // ColorAdjustments, Vignette (URP)

namespace Valkur.Core.Rendering
{
    /// <summary>
    /// Owns a global URP Volume + VolumeProfile that drains the world to
    /// grayscale + slight darkening + vignette while the player is dead.
    /// Animates <see cref="Volume.weight"/> 0↔1 in real time so the death
    /// scene can fade in/out without touching individual SpriteRenderers.
    ///
    /// Created and registered in <c>ServiceLocator</c> by GameplaySceneSetup.
    /// </summary>
    public class GrayscaleVolumeController : MonoBehaviour
    {
        [SerializeField, Tooltip("Default fade-in duration when the player dies.")]
        private float defaultFadeIn = 1.5f;

        [SerializeField, Tooltip("Default fade-out duration when the player revives.")]
        private float defaultFadeOut = 1.0f;

        private Volume _volume;
        private VolumeProfile _profile;
        private Coroutine _activeFade;

        public bool IsActive => _volume != null && _volume.weight > 0.001f;

        private void Awake()
        {
            EnsureVolume();
            EnsureCameraHasPostProcessing();
        }

        /// <summary>
        /// URP cameras only run the Volume framework when their
        /// <c>UniversalAdditionalCameraData.renderPostProcessing</c> flag is on.
        /// Cinemachine-driven cameras in this project ship with that flag off,
        /// which silently swallows the grayscale fade. Force it on at boot so
        /// the death sequence actually paints the screen.
        /// </summary>
        private static void EnsureCameraHasPostProcessing()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null) return;
            if (!data.renderPostProcessing)
            {
                data.renderPostProcessing = true;
                Debug.Log("[GrayscaleVolumeController] Enabled renderPostProcessing on Camera.main so the death-sequence Volume can affect the frame.");
            }
        }

        private void OnDestroy()
        {
            if (_profile != null)
            {
                Destroy(_profile);
                _profile = null;
            }
        }

        public void FadeIn(float duration = -1f)
        {
            if (duration < 0f) duration = defaultFadeIn;
            // Camera.main may not have existed yet when this controller booted —
            // retry the post-processing toggle right when the fade starts.
            EnsureCameraHasPostProcessing();
            StartFade(targetWeight: 1f, duration);
        }

        public void FadeOut(float duration = -1f)
        {
            if (duration < 0f) duration = defaultFadeOut;
            StartFade(targetWeight: 0f, duration);
        }

        /// <summary>Snap to a weight without animating. Used by ForceRevive / tests.</summary>
        public void SetWeight(float weight)
        {
            EnsureVolume();
            if (_activeFade != null)
            {
                StopCoroutine(_activeFade);
                _activeFade = null;
            }
            _volume.weight = Mathf.Clamp01(weight);
        }

        private void StartFade(float targetWeight, float duration)
        {
            EnsureVolume();
            if (_activeFade != null) StopCoroutine(_activeFade);
            _activeFade = StartCoroutine(FadeRoutine(targetWeight, duration));
        }

        private IEnumerator FadeRoutine(float targetWeight, float duration)
        {
            float start = _volume.weight;
            float t = 0f;
            float clampedDuration = Mathf.Max(0.0001f, duration);
            while (t < clampedDuration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / clampedDuration);
                _volume.weight = Mathf.Lerp(start, targetWeight, k);
                yield return null;
            }
            _volume.weight = targetWeight;
            _activeFade = null;
        }

        private void EnsureVolume()
        {
            if (_volume != null) return;

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "DeathGrayscaleProfile";
            _profile.hideFlags = HideFlags.DontSave;

            // ColorAdjustments: slight darkening + contrast for the death mood.
            // Saturation drop lives in SpiritWorldGrayscale (per-sprite) so altar
            // buildings + path markers can keep their full colors despite the
            // post-process pass; a global saturation -100 here would crush them
            // along with everything else.
            var color = _profile.Add<ColorAdjustments>(overrides: true);
            color.postExposure.overrideState = true;
            color.postExposure.value = -0.6f;
            color.contrast.overrideState = true;
            color.contrast.value = 10f;

            // Vignette: dark border to amplify the dread.
            var vignette = _profile.Add<Vignette>(overrides: true);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.45f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.5f;
            vignette.color.overrideState = true;
            vignette.color.value = Color.black;

            _volume = gameObject.GetComponent<Volume>();
            if (_volume == null) _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 100f;
            _volume.weight = 0f;
            _volume.profile = _profile;

            // Default-layer (0) so any Volume Mask configuration on URP cameras
            // (which by default includes the Default layer) picks this up.
            gameObject.layer = 0;
        }
    }
}
