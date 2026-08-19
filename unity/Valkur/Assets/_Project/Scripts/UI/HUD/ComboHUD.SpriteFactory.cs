using UnityEngine;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Procedural sprites for the combo badge. Everything is generated once
    /// into small RGBA32 textures and cached statically — Domain Reload is OFF
    /// in Valkur, so a SubsystemRegistration hook drops the cache on Play Mode
    /// entry before it can hand out handles to destroyed textures.
    ///
    /// All pixels are white; the live tint comes from <c>Image.color</c>, so one
    /// generated sprite serves every tier of the ladder.
    /// </summary>
    public sealed partial class ComboHUD
    {
        private const int RoundedSize   = 32;   // 9-sliced panel body
        private const int RoundedRadius = 10;
        private const int GlowSize      = 96;   // radial halo behind the number
        private const int DotSize       = 24;   // tier pip

        private static Sprite _solidSprite;
        private static Sprite _roundedSprite;
        private static Sprite _glowSprite;
        private static Sprite _dotSprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSpriteCacheOnPlayModeEnter()
        {
            _solidSprite = _roundedSprite = _glowSprite = _dotSprite = null;
        }

        /// <summary>Flat 4x4 white quad — bars, accents, anything sharp-edged.</summary>
        private static Sprite SolidSprite()
        {
            if (_solidSprite != null) return _solidSprite;

            var tex = NewTexture(4);
            var px  = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();

            _solidSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            _solidSprite.name = "ComboHUD_Solid";
            return _solidSprite;
        }

        /// <summary>
        /// Anti-aliased rounded rectangle with 9-slice borders, so the panel keeps
        /// a constant corner radius at any size. Use with <c>Image.Type.Sliced</c>.
        /// </summary>
        private static Sprite RoundedSprite()
        {
            if (_roundedSprite != null) return _roundedSprite;

            var tex  = NewTexture(RoundedSize);
            var px   = new Color32[RoundedSize * RoundedSize];
            float h  = RoundedSize * 0.5f;

            for (int y = 0; y < RoundedSize; y++)
            for (int x = 0; x < RoundedSize; x++)
            {
                float dx = x - h + 0.5f;
                float dy = y - h + 0.5f;
                float d  = RoundedRectDistance(dx, dy, h, h, RoundedRadius);
                float a  = Mathf.Clamp01(0.5f - d);   // 1 px of edge softening
                px[y * RoundedSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }

            tex.SetPixels32(px);
            tex.Apply();

            float border = RoundedRadius + 1f;
            _roundedSprite = Sprite.Create(
                tex, new Rect(0, 0, RoundedSize, RoundedSize), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
            _roundedSprite.name = "ComboHUD_Rounded";
            return _roundedSprite;
        }

        /// <summary>Soft radial halo — sits behind the number and pulses with the tier.</summary>
        private static Sprite GlowSprite()
        {
            if (_glowSprite != null) return _glowSprite;

            var tex = NewTexture(GlowSize);
            var px  = new Color32[GlowSize * GlowSize];
            float r = GlowSize * 0.5f;

            for (int y = 0; y < GlowSize; y++)
            for (int x = 0; x < GlowSize; x++)
            {
                float dx = x - r + 0.5f;
                float dy = y - r + 0.5f;
                float d  = Mathf.Sqrt(dx * dx + dy * dy) / r;
                // Squared falloff reads as light rather than as a flat disc.
                float a  = Mathf.Clamp01(1f - d);
                a = a * a * (0.55f + 0.45f * a);
                px[y * GlowSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }

            tex.SetPixels32(px);
            tex.Apply();

            _glowSprite = Sprite.Create(tex, new Rect(0, 0, GlowSize, GlowSize), new Vector2(0.5f, 0.5f));
            _glowSprite.name = "ComboHUD_Glow";
            return _glowSprite;
        }

        /// <summary>Small filled circle — one per tier in the pip row.</summary>
        private static Sprite DotSprite()
        {
            if (_dotSprite != null) return _dotSprite;

            var tex = NewTexture(DotSize);
            var px  = new Color32[DotSize * DotSize];
            float r = DotSize * 0.5f;

            for (int y = 0; y < DotSize; y++)
            for (int x = 0; x < DotSize; x++)
            {
                float dx = x - r + 0.5f;
                float dy = y - r + 0.5f;
                float d  = Mathf.Sqrt(dx * dx + dy * dy);
                float a  = Mathf.Clamp01(r - 0.5f - d);
                px[y * DotSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }

            tex.SetPixels32(px);
            tex.Apply();

            _dotSprite = Sprite.Create(tex, new Rect(0, 0, DotSize, DotSize), new Vector2(0.5f, 0.5f));
            _dotSprite.name = "ComboHUD_Dot";
            return _dotSprite;
        }

        // Signed distance to a rounded rectangle centred on the origin.
        // Negative inside, positive outside, zero on the edge.
        private static float RoundedRectDistance(float px, float py, float halfW, float halfH, float radius)
        {
            float qx = Mathf.Abs(px) - (halfW - radius);
            float qy = Mathf.Abs(py) - (halfH - radius);
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                                       Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            float inside  = Mathf.Min(Mathf.Max(qx, qy), 0f);
            return outside + inside - radius;
        }

        private static Texture2D NewTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp,
                hideFlags  = HideFlags.HideAndDontSave,
            };
            return tex;
        }
    }
}
