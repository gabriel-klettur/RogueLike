using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// FireballVisual — procedural sprite / material generation partial.
    /// Owns all static shared textures, sprites, and the unlit material.
    /// Domain Reload OFF: static cache is reset via SubsystemRegistration.
    /// </summary>
    public partial class FireballVisual
    {
        // ── Shared procedural assets ──────────────────────────────────
        private static Sprite   _coreSprite;
        private static Sprite   _glowSprite;
        private static Sprite   _haloSprite;
        private static Sprite   _hotCoreSprite;
        private static Sprite   _emberSprite;
        private static Sprite   _ringSprite;
        private static Material _unlitMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticSprites()
        {
            _coreSprite    = null;
            _glowSprite    = null;
            _haloSprite    = null;
            _hotCoreSprite = null;
            _emberSprite   = null;
            _ringSprite    = null;
            _unlitMaterial = null;
        }

        // ── Internal accessors (used by partials + FireballImpactFX) ──

        internal static Material SharedUnlitMaterial  { get { EnsureSharedAssets(); return _unlitMaterial; } }
        internal static Sprite   SharedCoreSprite     { get { EnsureSharedAssets(); return _coreSprite; } }
        internal static Sprite   SharedGlowSprite     { get { EnsureSharedAssets(); return _glowSprite; } }
        internal static Sprite   SharedHaloSprite     { get { EnsureSharedAssets(); return _haloSprite; } }
        internal static Sprite   SharedRingSprite     { get { EnsureSharedAssets(); return _ringSprite; } }
        internal static Sprite   SharedEmberSprite    { get { EnsureSharedAssets(); return _emberSprite; } }
        internal static Sprite   SharedHotCoreSprite  { get { EnsureSharedAssets(); return _hotCoreSprite; } }

        internal static void EnsureSharedAssets()
        {
            if (_coreSprite    == null) _coreSprite    = MakeRadial(48,  CorePixel);
            if (_glowSprite    == null) _glowSprite    = MakeRadial(96,  GlowPixel);
            if (_haloSprite    == null) _haloSprite    = MakeRadial(128, HaloPixel);
            if (_hotCoreSprite == null) _hotCoreSprite = MakeRadial(32,  HotCorePixel);
            if (_emberSprite   == null) _emberSprite   = MakeRadial(16,  EmberPixel);
            if (_ringSprite    == null) _ringSprite    = MakeRadial(128, RingPixel);

            if (_unlitMaterial == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                         ?? Shader.Find("Sprites/Default");
                _unlitMaterial = new Material(sh);
            }
        }

        private static Sprite MakeRadial(int size, System.Func<float, Color> fn)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp,
            };
            var px = new Color[size * size];
            float c = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c + 0.5f) / c;
                    float dy = (y - c + 0.5f) / c;
                    float d  = Mathf.Sqrt(dx * dx + dy * dy);
                    px[y * size + x] = fn(d);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private static Color CorePixel(float d)
        {
            if (d > 1f) return Color.clear;
            float a     = Mathf.Pow(1f - d, 1.6f);
            float white = Mathf.Pow(1f - d, 0.6f);
            return new Color(1f, Mathf.Lerp(0.55f, 1f, white), Mathf.Lerp(0.10f, 0.85f, white), a);
        }

        private static Color GlowPixel(float d)
        {
            if (d > 1f) return Color.clear;
            float a = Mathf.Pow(1f - d, 2.4f) * 0.85f;
            return new Color(1f, 0.42f, 0.06f, a);
        }

        private static Color HaloPixel(float d)
        {
            if (d > 1f) return Color.clear;
            float a = Mathf.Pow(1f - d, 3.2f) * 0.55f;
            return new Color(1f, 0.22f, 0.03f, a);
        }

        private static Color HotCorePixel(float d)
        {
            if (d > 1f) return Color.clear;
            float a     = Mathf.Pow(1f - d, 1.1f);
            float white = Mathf.Pow(1f - d, 0.4f);
            return new Color(1f, Mathf.Lerp(0.85f, 1f, white), Mathf.Lerp(0.55f, 1f, white), a);
        }

        private static Color EmberPixel(float d)
        {
            if (d > 1f) return Color.clear;
            float a = Mathf.Pow(1f - d, 1.8f);
            return new Color(1f, 0.7f, 0.25f, a);
        }

        private static Color RingPixel(float d)
        {
            if (d > 1f) return Color.clear;
            float ringPos  = 0.78f;
            float thickness = 0.18f;
            float diff = Mathf.Abs(d - ringPos);
            float a    = Mathf.Clamp01(1f - diff / thickness);
            a = Mathf.Pow(a, 1.6f);
            return new Color(1f, 0.55f, 0.15f, a);
        }
    }
}
