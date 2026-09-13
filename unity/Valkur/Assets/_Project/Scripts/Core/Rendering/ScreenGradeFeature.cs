using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Valkur.Core.Rendering
{
    /// <summary>
    /// Per-phase colour grading, vignette, dither — and the bloom under them — as full-screen
    /// passes on the 2D renderer.
    ///
    /// This is the only way to give the day/night cycle a look a Multiply Light2D cannot produce.
    /// A multiply can darken a pixel and tint it; it cannot drain saturation out of the night, it
    /// cannot recontrast what it just crushed, and it cannot dither. Those three are the difference
    /// between "the screen got darker and bluer" and "it is night".
    ///
    /// The bloom is the other half of the same argument: the project renders into an HDR buffer
    /// and every additive VFX writes energy above 1.0 that the framebuffer then clamps away.
    /// <see cref="ScreenBloomPass"/> lets that energy blossom. It runs BEFORE the grade so the
    /// vignette closes over the halos rather than under them, which is why the two share one
    /// feature: enqueue order inside one feature is a fact, across two it is a list somebody
    /// has to keep sorted in the renderer asset.
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

        [SerializeField, Tooltip("Hidden/Valkur/ScreenBloom. Serialized for the same reason. " +
                                  "Leaving it empty disables the bloom and costs nothing else.")]
        private Shader bloomShader;

        private Material        _material;
        private Material        _bloomMaterial;
        private ScreenGradePass _pass;
        private ScreenBloomPass _bloomPass;
        private bool            _warnedMissingShader;

        public override void Create()
        {
            // Called from OnEnable AND OnValidate, so it has to be idempotent — leaking a Material
            // per inspector keystroke is the classic way this component eats memory in the Editor.
            CoreUtils.Destroy(_material);
            _material = shader != null ? CoreUtils.CreateEngineMaterial(shader) : null;

            CoreUtils.Destroy(_bloomMaterial);
            _bloomMaterial = bloomShader != null ? CoreUtils.CreateEngineMaterial(bloomShader) : null;

            _pass ??= new ScreenGradePass();
            _pass.SetMaterial(_material);

            _bloomPass ??= new ScreenBloomPass();
            _bloomPass.SetMaterial(_bloomMaterial);
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

            // An offscreen camera is not the screen. The minimap bakes the world through one,
            // chunk by chunk, and a vignette graded into every chunk would tile the map with
            // dark corners; the spell and particle previews render through one too. Those are
            // measurements of the art, and the grade is a property of the frame the player sees.
            // The same rule covers the title's luminance probe: a bloom baked into that
            // measurement would resolve the logo's plate against a frame the player never sees.
            if (renderingData.cameraData.camera != null && renderingData.cameraData.camera.targetTexture != null) return;

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

            // Bloom first, so the grade's vignette closes over the halos.
            if (_bloomMaterial != null && ScreenGradeSettings.BloomWouldChangeTheFrame)
            {
                _bloomPass.UploadSettings();
                _bloomPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                _bloomPass.ConfigureInput(ScriptableRenderPassInput.Color);
                renderer.EnqueuePass(_bloomPass);
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
            CoreUtils.Destroy(_bloomMaterial);
            _bloomMaterial = null;
            _pass?.Dispose();
            _bloomPass?.Dispose();
        }
    }
}
