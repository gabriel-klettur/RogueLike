using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Valkur.Core.Rendering
{
    /// <summary>
    /// The bloom half of <see cref="ScreenGradeFeature"/>: a dual-Kawase pyramid at half
    /// resolution and below, composited back onto the camera colour BEFORE the grade runs.
    ///
    /// Owned by the same feature as the grade rather than being a second renderer feature,
    /// because the two have one contract to keep: the grade's vignette must close over the
    /// bloom, not under it — a halo that survives the vignette reads as a lamp on the lens.
    /// Both passes sit at <see cref="RenderPassEvent.AfterRenderingPostProcessing"/> and are
    /// enqueued bloom-first, which URP's stable pass sort preserves.
    ///
    /// Not a URP Volume Bloom for the reason the grade is not a Volume: the project keeps
    /// <c>renderPostProcessing</c> off because UberPost costs ~18 ms even at weight 0.
    /// </summary>
    internal sealed class ScreenBloomPass : ScriptableRenderPass
    {
        private const int PassPrefilter  = 0;
        private const int PassDownsample = 1;
        private const int PassUpsample   = 2;
        private const int PassComposite  = 3;

        /// <summary>Half, quarter, eighth, sixteenth. Deeper buys nothing at 800 px tall.</summary>
        private const int MipCount = 4;

        private static readonly int BloomParamsId = Shader.PropertyToID("_BloomParams");
        private static readonly int BloomTintId   = Shader.PropertyToID("_BloomTint");
        private static readonly int BloomTexelId  = Shader.PropertyToID("_BloomTexel");
        private static readonly int BloomTexId    = Shader.PropertyToID("_BloomTex");

        private Material   _material;
        private RTHandle[] _mips = new RTHandle[MipCount];
        private int        _liveMips;

        internal ScreenBloomPass()
        {
            profilingSampler = new ProfilingSampler("Valkur/ScreenBloom");
        }

        internal void SetMaterial(Material material) => _material = material;

        /// <summary>Push the live bloom settings onto the material. Main thread, once per camera.</summary>
        internal void UploadSettings()
        {
            if (_material == null) return;
            _material.SetVector(BloomParamsId, new Vector4(
                Mathf.Max(0f, ScreenGradeSettings.BloomThreshold),
                Mathf.Max(1e-3f, ScreenGradeSettings.BloomSoftKnee),
                Mathf.Max(0f, ScreenGradeSettings.BloomIntensity),
                0f));
            _material.SetColor(BloomTintId, ScreenGradeSettings.BloomTint);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples     = 1;
            desc.useMipMap       = false;

            int w = Mathf.Max(1, desc.width  / 2);
            int h = Mathf.Max(1, desc.height / 2);

            _liveMips = 0;
            for (int i = 0; i < MipCount; i++)
            {
                // Stop the pyramid where a further level would be a handful of pixels: a
                // 16x8 mip contributes a smear the width of the screen and nothing else.
                if (w < 8 || h < 8) break;
                desc.width  = w;
                desc.height = h;
                RenderingUtils.ReAllocateIfNeeded(ref _mips[i], desc, FilterMode.Bilinear,
                                                  TextureWrapMode.Clamp, name: "_ValkurBloomMip" + i);
                _liveMips++;
                w = Mathf.Max(1, w / 2);
                h = Mathf.Max(1, h / 2);
            }

            ResetTarget();
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_material == null || _liveMips == 0) return;
            var source = renderingData.cameraData.renderer.cameraColorTargetHandle;
            if (source == null) return;

            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                var camDesc = renderingData.cameraData.cameraTargetDescriptor;

                // Prefilter: camera colour -> mip 0, thresholded.
                SetTexel(cmd, camDesc.width, camDesc.height);
                Blitter.BlitCameraTexture(cmd, source, _mips[0], _material, PassPrefilter);

                // Down the pyramid.
                for (int i = 1; i < _liveMips; i++)
                {
                    SetTexel(cmd, _mips[i - 1]);
                    Blitter.BlitCameraTexture(cmd, _mips[i - 1], _mips[i], _material, PassDownsample);
                }

                // Back up, each level ADDED onto the one above it.
                for (int i = _liveMips - 1; i >= 1; i--)
                {
                    SetTexel(cmd, _mips[i]);
                    Blitter.BlitCameraTexture(cmd, _mips[i], _mips[i - 1], _material, PassUpsample);
                }

                // Composite onto the camera colour through the front-buffer swap, the same
                // shape ScreenGradePass uses — never a blit of a texture onto itself.
                cmd.SetGlobalTexture(BloomTexId, _mips[0]);
                Blit(cmd, ref renderingData, _material, PassComposite);
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        private static void SetTexel(CommandBuffer cmd, RTHandle rt)
        {
            var rtd = rt.rt;
            int w = rtd != null ? rtd.width  : 1;
            int h = rtd != null ? rtd.height : 1;
            SetTexel(cmd, w, h);
        }

        private static void SetTexel(CommandBuffer cmd, int w, int h)
        {
            cmd.SetGlobalVector(BloomTexelId, new Vector4(1f / Mathf.Max(1, w), 1f / Mathf.Max(1, h), 0f, 0f));
        }

        internal void Dispose()
        {
            for (int i = 0; i < _mips.Length; i++)
            {
                _mips[i]?.Release();
                _mips[i] = null;
            }
            _liveMips = 0;
        }
    }
}
