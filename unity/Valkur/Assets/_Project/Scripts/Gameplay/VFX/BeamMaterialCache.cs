using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Shared additive materials for beam <see cref="LineRenderer"/>s, keyed by texture.
    ///
    /// Two problems solved at once.
    ///
    /// The beam was alpha-blended. LaserBeamController built its lines with
    /// Sprite-Unlit-Default and never touched the blend factors, so the beam occluded what
    /// was behind it instead of adding to it. A laser is light; with alpha blending it can
    /// never read as incandescent, only as a coloured bar. Same defect the particle system
    /// had before <see cref="ParticleMaterialCache"/>.
    ///
    /// And it allocated a Material per beam per line, destroyed on teardown — the pattern
    /// this project has been removing everywhere else.
    ///
    /// Callers assign the result to <c>sharedMaterial</c> and drive per-renderer tiling and
    /// scroll through a <see cref="MaterialPropertyBlock"/>, so two beams can scroll
    /// independently without either owning a material.
    /// </summary>
    public static class BeamMaterialCache
    {
        private static readonly Dictionary<int, Material> _cache = new Dictionary<int, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _cache.Clear();

        /// <summary>
        /// The shared additive material for a beam texture.
        ///
        /// Delegates to <see cref="ParticleMaterialCache"/> rather than building its own.
        /// The first attempt here used `Universal Render Pipeline/2D/Sprite-Unlit-Default`,
        /// which is what the beam already used — and that shader exposes no `_SrcBlend` or
        /// `_DstBlend` at all, so every blend-mode assignment was a silent no-op and the beam
        /// stayed alpha-blended. Its blending is fixed premultiplied-alpha and cannot be
        /// switched.
        ///
        /// `URP/Particles/Unlit`, which the particle cache already uses, does expose them.
        /// Nothing about it is particle-specific: a LineRenderer renders with whatever
        /// material it is given.
        ///
        /// The local dictionary is kept so a beam texture always maps to the same instance
        /// even if the particle cache's keying ever changes.
        /// </summary>
        public static Material Get(Texture texture)
        {
            int key = texture != null ? texture.GetInstanceID() : 0;

            if (_cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var mat = ParticleMaterialCache.Get(texture, additive: true);
            _cache[key] = mat;
            return mat;
        }

        // ApplyScroll USED to live here, writing tiling and offset into _MainTex_ST and
        // _BaseMap_ST on a MaterialPropertyBlock so the beam's texture could travel. It was
        // deleted because it never did anything, and it is worth recording why so nobody
        // rebuilds it:
        //
        //   * "Universal Render Pipeline/Particles/Unlit" has no _MainTex property at all —
        //     its texture is _BaseMap. Writing _MainTex_ST addresses nothing.
        //   * It declares _BaseMap_ST inside CBUFFER_START(UnityPerMaterial), but its forward
        //     pass never reads it. There is no TRANSFORM_TEX anywhere in
        //     ParticlesUnlitForwardPass.hlsl; GetParticleTexcoords assigns
        //     `outputTexcoord = inputTexcoords.xy` and that raw UV0 is what gets sampled.
        //     URP's particle shaders animate UVs through flipbook data, not through ST.
        //
        // So the scroll was a silent no-op for as long as it existed, and the tests that
        // covered it asserted only that the values were written into the block — never that
        // they changed a pixel. The travelling charge is geometry now: LaserBeamController
        // slides short LineRenderers along the beam, which depends on no shader feature.
    }
}
