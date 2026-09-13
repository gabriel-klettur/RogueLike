using UnityEngine;

namespace Valkur.Gameplay.World.Sky
{
    /// <summary>
    /// The generated art the shadow layer draws with: one soft ellipse for the contact blob.
    ///
    /// Generated rather than authored because its size comes FROM the body it sits under and a
    /// radial falloff has no pixels to hand-place. 64x32 at PPU 64 so it is exactly 1.0 x 0.5
    /// world units at scale 1, which makes the caster's scale arithmetic a plain ratio.
    ///
    /// Domain Reload is OFF: the texture and sprite are destroyed with the Play session that
    /// made them, so a cached handle would surface as a MissingReferenceException on the next.
    /// </summary>
    public static class ShadowSprites
    {
        private const int   BlobW = 64;
        private const int   BlobH = 32;
        private const float BlobPpu = 64f;

        private static Sprite s_blob;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_blob = null;

        /// <summary>World size of <see cref="Blob"/> at scale 1.</summary>
        public static readonly Vector2 BlobUnitSize = new Vector2(BlobW / BlobPpu, BlobH / BlobPpu);

        /// <summary>A white ellipse fading to transparent at its rim. Colour it with the renderer.</summary>
        public static Sprite Blob
        {
            get
            {
                if (s_blob != null) return s_blob;

                var tex = new Texture2D(BlobW, BlobH, TextureFormat.RGBA32, false)
                {
                    name       = "ShadowBlob",
                    filterMode = FilterMode.Bilinear,
                    wrapMode   = TextureWrapMode.Clamp,
                    hideFlags  = HideFlags.HideAndDontSave,
                };
                var px = new Color32[BlobW * BlobH];
                for (int y = 0; y < BlobH; y++)
                {
                    float ny = (y + 0.5f) / BlobH * 2f - 1f;
                    for (int x = 0; x < BlobW; x++)
                    {
                        float nx = (x + 0.5f) / BlobW * 2f - 1f;
                        float r  = Mathf.Sqrt(nx * nx + ny * ny);
                        // Flat through the middle, then a wide soft rim: a hard-edged ellipse
                        // reads as a puddle, a gaussian reads as fog. This is a cushion.
                        float a  = 1f - Mathf.SmoothStep(0.35f, 1f, r);
                        byte  ab = (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f);
                        px[y * BlobW + x] = new Color32(255, 255, 255, ab);
                    }
                }
                tex.SetPixels32(px);
                tex.Apply(false, true);

                s_blob = Sprite.Create(tex, new Rect(0, 0, BlobW, BlobH), new Vector2(0.5f, 0.5f), BlobPpu,
                                       0, SpriteMeshType.FullRect);
                s_blob.name = "ShadowBlob";
                s_blob.hideFlags = HideFlags.HideAndDontSave;
                return s_blob;
            }
        }
    }
}
