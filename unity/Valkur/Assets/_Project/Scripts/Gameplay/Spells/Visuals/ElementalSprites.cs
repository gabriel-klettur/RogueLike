using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Shared procedural sprite library used by elemental visuals. Generates radial
    /// gradients, rings, snowflakes, lightning bolts, sparkles, blades and wisps once
    /// and caches them statically.
    /// </summary>
    internal static class ElementalSprites
    {
        private static Sprite _hotCore, _core, _glow, _halo, _ring, _sparkle, _sparkleStar, _snowflake, _bolt, _blade, _wisp;
        private static Material _unlitMaterial;
        private static Material _additiveMaterial;

        public static Sprite HotCore       { get { EnsureAll(); return _hotCore; } }
        public static Sprite Core          { get { EnsureAll(); return _core; } }
        public static Sprite Glow          { get { EnsureAll(); return _glow; } }
        public static Sprite Halo          { get { EnsureAll(); return _halo; } }
        public static Sprite Ring          { get { EnsureAll(); return _ring; } }
        public static Sprite Sparkle       { get { EnsureAll(); return _sparkle; } }
        public static Sprite SparkleStar   { get { EnsureAll(); return _sparkleStar; } }
        public static Sprite Snowflake     { get { EnsureAll(); return _snowflake; } }
        public static Sprite Bolt          { get { EnsureAll(); return _bolt; } }
        public static Sprite Blade         { get { EnsureAll(); return _blade; } }
        public static Sprite Wisp          { get { EnsureAll(); return _wisp; } }

        public static Material SharedUnlitMaterial { get { EnsureAll(); return _unlitMaterial; } }

        /// <summary>
        /// Additive twin of <see cref="SharedUnlitMaterial"/>, for SpriteRenderer quads
        /// that must ADD light instead of replacing the pixel under them.
        ///
        /// This cannot be expressed by patching the unlit material: its shader
        /// (<c>URP/2D/Sprite-Unlit-Default</c>) declares no <c>_SrcBlend</c>/<c>_DstBlend</c>,
        /// so a blend-mode assignment against it compiles, logs nothing, and changes
        /// nothing — the same trap <see cref="Valkur.Gameplay.VFX.BeamMaterialCache"/>
        /// documents. <c>Valkur/SpriteAdditive</c> is a separate shader whose blend is
        /// fixed at <c>SrcAlpha One</c>, matching
        /// <see cref="Valkur.Gameplay.VFX.ParticleMaterialCache"/>'s additive particle
        /// material so sprite layers and particle layers composite identically.
        ///
        /// Like every other static here it survives Domain Reload (which is OFF) only
        /// through the <c>== null</c> guard in <see cref="EnsureAll"/> — Unity's
        /// overloaded null catches the destroyed native object on the second Play and
        /// rebuilds. Never copy this reference into another un-guarded static field.
        /// </summary>
        public static Material SharedAdditiveMaterial { get { EnsureAll(); return _additiveMaterial; } }

        /// <summary>
        /// Domain Reload is OFF, so these twelve statics survive into the next Play session
        /// holding DESTROYED native objects. They were self-healing only by accident —
        /// Unity's overloaded <c>== null</c> reports a destroyed object as null, so the
        /// guards in <see cref="EnsureAll"/> happened to rebuild them. That is a property
        /// of the guards, not a reset, and it fails the moment any caller copies one of
        /// these into its own un-guarded static field.
        ///
        /// Nulling them here makes the rebuild explicit and gets this class off the
        /// unreset-statics baseline, where all twelve sat as accepted debt. Everything is
        /// regenerated from constants on the next touch, so dropping them costs one
        /// rebuild and nothing else.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _hotCore = _core = _glow = _halo = _ring = _sparkle = _sparkleStar =
                _snowflake = _bolt = _blade = _wisp = null;
            _unlitMaterial = null;
            _additiveMaterial = null;
        }

        public static void EnsureAll()
        {
            if (_hotCore == null)     _hotCore     = Radial(32, HotPx);
            if (_core == null)        _core        = Radial(48, CorePx);
            if (_glow == null)        _glow        = Radial(96, GlowPx);
            if (_halo == null)        _halo        = Radial(128, HaloPx);
            if (_ring == null)        _ring        = Radial(128, RingPx);
            if (_sparkle == null)     _sparkle     = Radial(16, SparkPx);
            if (_sparkleStar == null) _sparkleStar = Star(48);
            if (_snowflake == null)   _snowflake   = MakeSnowflake(48);
            if (_bolt == null)        _bolt        = MakeBolt(48);
            if (_blade == null)       _blade       = MakeBlade(48);
            if (_wisp == null)        _wisp        = MakeWisp(48);

            if (_unlitMaterial == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                         ?? Shader.Find("Sprites/Default");
                _unlitMaterial = new Material(sh);
            }

            if (_additiveMaterial == null)
            {
                // Fall back to the alpha shader rather than to Sprites/Default: a
                // missing custom shader should degrade to "looks like it used to",
                // not to an untinted magenta error quad.
                var sh = Shader.Find("Valkur/SpriteAdditive")
                         ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                         ?? Shader.Find("Sprites/Default");
                _additiveMaterial = new Material(sh) { name = "ElementalSprites_Additive" };
            }
        }

        // Radial gradient generator
        private static Sprite Radial(int size, System.Func<float, Color> fn)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            float c = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c + 0.5f) / c;
                    float dy = (y - c + 0.5f) / c;
                    px[y * size + x] = fn(Mathf.Sqrt(dx * dx + dy * dy));
                }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private static Color HotPx(float d) { if (d > 1f) return Color.clear; float a = Mathf.Pow(1f - d, 1.1f); return new Color(1f, 1f, 1f, a); }
        private static Color CorePx(float d) { if (d > 1f) return Color.clear; float a = Mathf.Pow(1f - d, 1.6f); return new Color(1f, 1f, 1f, a); }
        private static Color GlowPx(float d) { if (d > 1f) return Color.clear; float a = Mathf.Pow(1f - d, 2.4f) * 0.85f; return new Color(1f, 1f, 1f, a); }
        private static Color HaloPx(float d) { if (d > 1f) return Color.clear; float a = Mathf.Pow(1f - d, 3.2f) * 0.55f; return new Color(1f, 1f, 1f, a); }
        private static Color SparkPx(float d) { if (d > 1f) return Color.clear; float a = Mathf.Pow(1f - d, 1.8f); return new Color(1f, 1f, 1f, a); }
        private static Color RingPx(float d)
        {
            if (d > 1f) return Color.clear;
            float ringPos = 0.78f, thickness = 0.18f;
            float diff = Mathf.Abs(d - ringPos);
            float a = Mathf.Pow(Mathf.Clamp01(1f - diff / thickness), 1.6f);
            return new Color(1f, 1f, 1f, a);
        }

        // 4-pointed star (sparkle starburst)
        private static Sprite Star(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size]; float c = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c + 0.5f) / c;
                    float dy = (y - c + 0.5f) / c;
                    float horiz = Mathf.Max(0f, 1f - Mathf.Abs(dy) * 8f) * Mathf.Max(0f, 1f - Mathf.Abs(dx));
                    float vert  = Mathf.Max(0f, 1f - Mathf.Abs(dx) * 8f) * Mathf.Max(0f, 1f - Mathf.Abs(dy));
                    float diagA = Mathf.Max(0f, 1f - Mathf.Abs(dx + dy) * 12f) * Mathf.Max(0f, 1f - Mathf.Sqrt(dx * dx + dy * dy));
                    float diagB = Mathf.Max(0f, 1f - Mathf.Abs(dx - dy) * 12f) * Mathf.Max(0f, 1f - Mathf.Sqrt(dx * dx + dy * dy));
                    float center = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)), 2.2f);
                    float a = Mathf.Clamp01(horiz + vert + 0.6f * (diagA + diagB) + 0.7f * center);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        // Snowflake: 6-armed star with cross-arms
        private static Sprite MakeSnowflake(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size]; float c = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c + 0.5f) / c;
                    float dy = (y - c + 0.5f) / c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = 0f;
                    if (r <= 1f)
                    {
                        // 6 arms via 3 line equations
                        for (int k = 0; k < 3; k++)
                        {
                            float ang = k * Mathf.PI / 3f;
                            float cx = Mathf.Cos(ang), cy = Mathf.Sin(ang);
                            float along = dx * cx + dy * cy;
                            float perp = -dx * cy + dy * cx;
                            float arm = Mathf.Max(0f, 1f - Mathf.Abs(perp) * 14f) * Mathf.Max(0f, 1f - Mathf.Abs(along));
                            // small cross-arms at 0.4 and 0.7
                            float cross1 = Mathf.Max(0f, 1f - Mathf.Abs(Mathf.Abs(along) - 0.4f) * 22f) * Mathf.Max(0f, 1f - Mathf.Abs(perp) * 6f);
                            float cross2 = Mathf.Max(0f, 1f - Mathf.Abs(Mathf.Abs(along) - 0.7f) * 22f) * Mathf.Max(0f, 1f - Mathf.Abs(perp) * 6f);
                            a = Mathf.Max(a, arm + 0.7f * (cross1 + cross2));
                        }
                        a = Mathf.Clamp01(a);
                        a *= Mathf.Pow(1f - r, 0.4f); // soft fade outward
                    }
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        // Lightning bolt: stylised zig-zag
        private static Sprite MakeBolt(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = Color.clear;

            float c = size * 0.5f;
            // Polyline points (normalised -1..1)
            var pts = new[]
            {
                new Vector2(-0.05f, 0.95f),
                new Vector2( 0.20f, 0.30f),
                new Vector2(-0.10f, 0.10f),
                new Vector2( 0.15f, -0.20f),
                new Vector2(-0.20f, -0.45f),
                new Vector2( 0.05f, -0.95f),
            };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c + 0.5f) / c;
                    float dy = (y - c + 0.5f) / c;
                    float bestDist = 10f;
                    for (int k = 0; k < pts.Length - 1; k++)
                        bestDist = Mathf.Min(bestDist, DistSegment(new Vector2(dx, dy), pts[k], pts[k + 1]));
                    float core = Mathf.Max(0f, 1f - bestDist * 18f);
                    float halo = Mathf.Max(0f, 1f - bestDist * 6f) * 0.45f;
                    float a = Mathf.Clamp01(core + halo);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        // Curved blade (boomerang)
        private static Sprite MakeBlade(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size]; float c = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c + 0.5f) / c;
                    float dy = (y - c + 0.5f) / c;
                    // Arc along radius ~0.75 with thickness band
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float arc = Mathf.Max(0f, 1f - Mathf.Abs(r - 0.78f) * 14f);
                    // Cut to lower-half + diagonal arms
                    float ang = Mathf.Atan2(dy, dx);
                    float wedge1 = Mathf.Clamp01(1f - Mathf.Abs(ang - Mathf.PI * 0.25f) * 1.6f);
                    float wedge2 = Mathf.Clamp01(1f - Mathf.Abs(ang - Mathf.PI * 0.75f) * 1.6f);
                    float a = arc * Mathf.Max(wedge1, wedge2);
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        // Void wisp: smoky tendril (vertical anisotropic gradient)
        private static Sprite MakeWisp(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size]; float c = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c + 0.5f) / c;
                    float dy = (y - c + 0.5f) / c;
                    // Stretch vertically
                    float sx = dx;
                    float sy = dy * 0.55f;
                    float r = Mathf.Sqrt(sx * sx + sy * sy);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f);
                    // Wavy edges
                    a *= 0.9f + 0.1f * Mathf.Sin(dy * 8f + dx * 5f);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private static float DistSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
