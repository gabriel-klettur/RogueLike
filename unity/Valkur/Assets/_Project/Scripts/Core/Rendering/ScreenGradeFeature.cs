using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Valkur.Core.Rendering
{
    /// <summary>
    /// Per-phase colour grading, vignette and dither, as one full-screen pass on the 2D renderer.
    ///
    /// This is the only way to give the day/night cycle a look a Multiply Light2D cannot produce.
    /// A multiply can darken a pixel and tint it; it cannot drain saturation out of the night, it
    /// cannot recontrast what it just crushed, and it cannot dither. Those three are the difference
    /// between "the screen got darker and bluer" and "it is night".
    ///
    /// Deliberately NOT a URP Volume override. The project keeps camera
    /// <c>renderPostProcessing</c> off because UberPost costs ~18 ms/frame on a mid GPU even at
    /// Volume weight 0 (see <c>.github/skills/unity-performance/SKILL.md</c>); renderer features are
    /// dispatched from <c>RenderSingleCamera</c> regardless of that flag, so this runs without
    /// re-enabling the stack. The death-sequence Volume in
    /// <see cref="GrayscaleVolumeController"/> keeps working exactly as before.
    ///
    /// Injected at <see cref="RenderPassEvent.AfterRenderingPostProcessing"/> with NO offset:
    /// Renderer2D tests that value for literal equality when deciding whether post-processing may
    /// resolve straight to the camera target, so <c>+ 1</c> would change unrelated behaviour.
    /// </summary>
    public sealed class ScreenGradeFeature : ScriptableRendererFeature
    {
        [SerializeField, Tooltip("Hidden/Valkur/ScreenGrade. A serialized reference, not " +
                                  "Shader.Find, so the build stripper keeps the variant.")]
        private Shader shader;

        private Material        _material;
        private ScreenGradePass _pass;
        private bool            _warnedMissingShader;

        public override void Create()
        {
            // Called from OnEnable AND OnValidate, so it has to be idempotent — leaking a Material
            // per inspector keystroke is the classic way this component eats memory in the Editor.
            CoreUtils.Destroy(_material);
            _material = shader != null ? CoreUtils.CreateEngineMaterial(shader) : null;

            _pass ??= new ScreenGradePass();
            _pass.SetMaterial(_material);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var cameraType = renderingData.cameraData.cameraType;
            // Only the game view. A graded Scene view would mislead anyone authoring art, and
            // preview/reflection cameras have no business paying for it.
            if (cameraType != CameraType.Game) return;

            // Announced before any early-out below: the uGUI vignette needs to know the feature is
            // installed even on frames where the grade happens to be neutral, or it would flicker
            // back in every time the grade passed through 1.0.
            ScreenGradeSettings.FeaturePresent = true;

            if (_material == null)
            {
                if (!_warnedMissingShader)
                {
                    _warnedMissingShader = true;
                    Debug.LogWarning("[ScreenGradeFeature] No shader assigned — the day/night grade " +
                                      "and vignette will not render. Assign Hidden/Valkur/ScreenGrade " +
                                      "on the feature in Renderer2D.asset.");
                }
                return;
            }

            // A neutral grade is two full-screen passes that produce an identical image.
            if (!ScreenGradeSettings.WouldChangeTheFrame) return;

            _pass.UploadSettings();
            _pass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            _pass.ConfigureInput(ScriptableRenderPassInput.Color);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
            _pass?.Dispose();
        }
    }
}
