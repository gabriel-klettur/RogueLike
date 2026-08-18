using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Shared materials for particle renderers, keyed by (texture, blend mode).
    ///
    /// Replaces the old "new Material() per emitter" path, which broke SRP batching and
    /// leaked material instances in EditMode tests. Callers must assign the result to
    /// <c>ParticleSystemRenderer.sharedMaterial</c> — assigning to <c>.material</c> would
    /// instantiate a per-renderer copy and undo the point of this cache.
    ///
    /// It also fixes the surface setup: URP's particle shader defaults to Opaque, so the
    /// previous code produced opaque quads for every non-additive preset.
    /// </summary>
    public static class ParticleMaterialCache
    {
        private const string URP_PARTICLE_SHADER = "Universal Render Pipeline/Particles/Unlit";
        private const string FALLBACK_SHADER = "Sprites/Default";

        /// <summary>Transparent geometry queue — keeps particles sorted against sprites.</summary>
        private const int TRANSPARENT_QUEUE = 3000;

        private static readonly Dictionary<int, Material> _cache = new Dictionary<int, Material>();
        private static Shader _shader;

        // Domain Reload is OFF — materials created at runtime die on play-mode exit.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _cache.Clear();
            _shader = null;
        }

        /// <summary>
        /// Returns the shared material for the given texture + blend mode, building it on
        /// first request. <paramref name="texture"/> may be null for the untextured quad.
        /// </summary>
        public static Material Get(Texture texture, bool additive)
        {
            int key = ((texture != null ? texture.GetInstanceID() : 0) * 2) + (additive ? 1 : 0);

            // Unity's overloaded null also catches a material destroyed by a play-mode exit.
            if (_cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var mat = Build(texture, additive);
            _cache[key] = mat;
            return mat;
        }

        private static Material Build(Texture texture, bool additive)
        {
            if (_shader == null)
                _shader = Shader.Find(URP_PARTICLE_SHADER);
            if (_shader == null)
                _shader = Shader.Find(FALLBACK_SHADER);

            var mat = new Material(_shader)
            {
                name = $"ParticleMat_{(texture != null ? texture.name : "Untextured")}_{(additive ? "Add" : "Alpha")}",
                hideFlags = HideFlags.DontSave,
                renderQueue = TRANSPARENT_QUEUE,
            };

            if (texture != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);
            }

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);

            // ── Surface: transparent, no depth write, no alpha clip ──────────────────
            // URP's particle shader ships as Opaque; without this every alpha preset
            // rendered as a solid quad.
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // 1 = Transparent
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)CullMode.Off);

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");

            // ── Blend ────────────────────────────────────────────────────────────────
            if (additive)
            {
                // _Blend 2 = Additive in URP's particle shader GUI enum.
                if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 2f);
                SetBlendFactors(mat, BlendMode.SrcAlpha, BlendMode.One);
                mat.EnableKeyword("_EMISSION");
            }
            else
            {
                if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f); // 0 = Alpha
                SetBlendFactors(mat, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha);
                mat.DisableKeyword("_EMISSION");
            }

            return mat;
        }

        private static void SetBlendFactors(Material mat, BlendMode src, BlendMode dst)
        {
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)src);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)dst);
        }
    }
}
