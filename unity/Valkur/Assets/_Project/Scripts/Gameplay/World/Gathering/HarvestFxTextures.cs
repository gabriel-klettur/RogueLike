using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// The three tiny textures the harvest particles are drawn with, generated once.
    ///
    /// <para><b>WHY NOT THE WHITE TEXTURE.</b> A particle renderer draws whatever texture its
    /// material carries, and a <c>Texture2D.whiteTexture</c> quad is a hard square. Measured on the
    /// first live capture: the dust of a falling tree was a stack of beige tiles and the leaves were
    /// green confetti squares — the one moment the activity builds up to, drawn as a rendering
    /// fault. A soft puff, a pointed leaf and a splinter are a few hundred bytes each.</para>
    ///
    /// <para>Point-filtered and small on purpose: at 16-32 PPU a smooth 64 px gradient would be the
    /// only anti-aliased thing on screen and read as a foreign asset.</para>
    /// </summary>
    public static class HarvestFxTextures
    {
        private static Texture2D _puff, _leaf, _chip, _stroke;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _puff = null;
            _leaf = null;
            _chip = null;
            _stroke = null;
        }

        /// <summary>
        /// A blade stroke: long, pointed at both ends, hot along its spine. Bilinear on purpose —
        /// it is LIGHT drawn additively over the trunk, and a point-filtered glow reads as a stair.
        /// </summary>
        public static Texture2D Stroke => _stroke != null ? _stroke : (_stroke = BuildWide(48, 12, (u, v) =>
        {
            float along = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 0.7f);
            float half = 0.5f * along;
            float d = Mathf.Abs(v - 0.5f);
            if (d > half) return 0f;
            float core = 1f - d / Mathf.Max(0.001f, half);
            return Mathf.Clamp01(core * core * 1.4f) * along;
        }, "HarvestStroke"));

        private static Texture2D BuildWide(int w, int h, System.Func<float, float, float> alpha, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = new Color32(255, 255, 255,
                        (byte)(Mathf.Clamp01(alpha((x + 0.5f) / w, (y + 0.5f) / h)) * 255f));
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        /// <summary>A soft round puff with a dithered edge. Dust and smoke.</summary>
        public static Texture2D Puff => _puff != null ? _puff : (_puff = Build(16, (x, y, n) =>
        {
            float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Clamp01(1f - r);
            a = Mathf.Round(a * a * 4f) / 4f;
            return a;
        }, "HarvestPuff"));

        /// <summary>A pointed leaf along the diagonal.</summary>
        public static Texture2D Leaf => _leaf != null ? _leaf : (_leaf = Build(8, (x, y, n) =>
        {
            float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
            float along = (u + v) * 0.5f;
            float across = Mathf.Abs(u - v);
            float width = Mathf.Sin(along * Mathf.PI) * 0.36f;
            return across <= width ? 1f : 0f;
        }, "HarvestLeaf"));

        /// <summary>A short splinter, wider in the middle.</summary>
        public static Texture2D Chip => _chip != null ? _chip : (_chip = Build(8, (x, y, n) =>
        {
            float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
            float half = Mathf.Sin(u * Mathf.PI) * 0.28f;
            return Mathf.Abs(v - 0.5f) <= half ? 1f : 0f;
        }, "HarvestChip"));

        private static Texture2D Build(int size, System.Func<int, int, int, float> alpha, string name)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(x, y, size)) * 255f));
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
