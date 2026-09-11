using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The two GPU bakes the panel needs, both because the source art is in a packed, NON-readable
    /// atlas texture that the CPU cannot sample.
    ///
    /// <list type="bullet">
    /// <item><b>Spell icons</b> are painted at 1024x1024 and packed bilinear with no mipmaps. Drawn
    /// straight into a 32-pixel slot, bilinear samples four texels out of every thousand and the
    /// icon aliases into noise. <see cref="Icon"/> halves it on the GPU until it is the slot's own
    /// pixel size — each halving at exact texel centres is a 2x2 box filter — and reads it back
    /// once, so the slot draws a properly minified icon 1:1.</item>
    /// <item><b>The portrait</b> is the character's idle frame. The frames are FullRect sprites
    /// (four vertices), so the sprite mesh says nothing about where the head is, and the legacy
    /// strips pad a 128-pixel cell around a much smaller figure. <see cref="Portrait"/> reads the
    /// frame back, finds the silhouette from its alpha, crops the head and shoulders, reduces by a
    /// WHOLE factor towards the target size and draws a one-texel dark outline around it.</item>
    /// </list>
    ///
    /// <para>Both refuse on a null graphics device (batch mode with <c>-nographics</c>), where a
    /// blit silently draws nothing and a read-back returns black — the caller then falls back to
    /// the raw sprite rather than showing a black square.</para>
    /// </summary>
    public static class HudTextureBaker
    {
        private static Dictionary<(int sprite, int px), Texture2D> s_icons =
            new Dictionary<(int, int), Texture2D>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetHudTextureBakerStatics() =>
            s_icons = new Dictionary<(int, int), Texture2D>();

        /// <summary>True when a GPU exists to blit and read back through.</summary>
        public static bool CanBake => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        // -- Icons -------------------------------------------------------------------

        /// <summary>
        /// <paramref name="sprite"/> minified to <paramref name="px"/> square pixels, cached per
        /// sprite and size. Null when it cannot be baked.
        /// </summary>
        public static Texture2D Icon(Sprite sprite, int px)
        {
            if (sprite == null || px <= 0 || !CanBake) return null;
            var key = (sprite.GetInstanceID(), px);
            if (s_icons.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = MinifySprite(sprite, px, px);
            if (tex != null)
            {
                tex.name = "HudIcon_" + sprite.name + "_" + px;
                s_icons[key] = tex;
            }
            return tex;
        }

        private static Texture2D MinifySprite(Sprite sprite, int outW, int outH)
        {
            var source = sprite.texture;
            if (source == null) return null;
            GetUvBounds(sprite, out var uvMin, out var uvMax);

            int w = Mathf.Max(1, Mathf.RoundToInt(sprite.rect.width));
            int h = Mathf.Max(1, Mathf.RoundToInt(sprite.rect.height));
            var prevActive = RenderTexture.active;
            RenderTexture current = null;
            try
            {
                bool first = true;
                do
                {
                    int nw = Mathf.Max(outW, w / 2), nh = Mathf.Max(outH, h / 2);
                    if (w <= outW && h <= outH) { nw = outW; nh = outH; }
                    var rt = RenderTexture.GetTemporary(nw, nh, 0, RenderTextureFormat.ARGB32);
                    rt.filterMode = FilterMode.Bilinear;
                    if (first) Graphics.Blit(source, rt, uvMax - uvMin, uvMin);
                    else Graphics.Blit(current, rt);
                    if (current != null) RenderTexture.ReleaseTemporary(current);
                    current = rt;
                    current.filterMode = FilterMode.Bilinear;
                    w = nw;
                    h = nh;
                    first = false;
                } while (w != outW || h != outH);

                return ReadBack(current, outW, outH, FilterMode.Bilinear);
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (current != null) RenderTexture.ReleaseTemporary(current);
            }
        }

        // -- Portrait -------------------------------------------------------------------

        /// <summary>What the portrait bake found, for the tests and the probe.</summary>
        public struct PortraitInfo
        {
            public int SourceWidth, SourceHeight;
            public int BodyTop, BodyBottom, HeadCentreX;
            public int Downsample;
        }

        /// <summary>
        /// The head and shoulders of <paramref name="sprite"/>, <paramref name="outW"/> by
        /// <paramref name="outH"/> texels, outlined. <paramref name="bodyTexels"/> is the height
        /// the whole figure should come out at; the source is reduced by the whole factor nearest
        /// to that and never resampled by a fraction.
        /// </summary>
        public static Texture2D Portrait(Sprite sprite, int outW, int outH, int bodyTexels, Color outline,
                                         out PortraitInfo info)
        {
            info = default;
            if (sprite == null || outW <= 0 || outH <= 0 || !CanBake) return null;
            var source = sprite.texture;
            if (source == null) return null;

            int sw = Mathf.Max(1, Mathf.RoundToInt(sprite.rect.width));
            int sh = Mathf.Max(1, Mathf.RoundToInt(sprite.rect.height));
            GetUvBounds(sprite, out var uvMin, out var uvMax);

            Color32[] src;
            var prevActive = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(sw, sh, 0, RenderTextureFormat.ARGB32);
            try
            {
                rt.filterMode = FilterMode.Point;
                // Point sampling at 1:1 so a pixel of the frame is a pixel of the bake.
                Graphics.Blit(source, rt, uvMax - uvMin, uvMin);
                var tmp = ReadBack(rt, sw, sh, FilterMode.Point);
                src = tmp.GetPixels32();
                HudLifetime.Release(tmp);
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }

            // Silhouette: the opaque rows, and where the head sits in the top of them.
            int top = -1, bottom = -1;
            for (int y = sh - 1; y >= 0 && top < 0; y--)
                for (int x = 0; x < sw; x++)
                    if (src[y * sw + x].a > 96) { top = y; break; }
            for (int y = 0; y < sh && bottom < 0; y++)
                for (int x = 0; x < sw; x++)
                    if (src[y * sw + x].a > 96) { bottom = y; break; }
            if (top < 0 || bottom < 0) return null;

            int bodyH = Mathf.Max(1, top - bottom + 1);
            int k = Mathf.Max(1, Mathf.RoundToInt(bodyH / (float)Mathf.Max(8, bodyTexels)));

            // The head is the centroid of the ink in the top fifth of the figure. A raised weapon
            // is thin and moves the centroid little; the head is the widest mass up there.
            int headRows = Mathf.Max(3, bodyH / 5);
            long sumX = 0, count = 0;
            for (int y = top; y > top - headRows && y >= 0; y--)
                for (int x = 0; x < sw; x++)
                    if (src[y * sw + x].a > 96) { sumX += x; count++; }
            int headX = count > 0 ? (int)(sumX / count) : sw / 2;

            info = new PortraitInfo
            {
                SourceWidth = sw, SourceHeight = sh, BodyTop = top, BodyBottom = bottom,
                HeadCentreX = headX, Downsample = k,
            };

            // Window in SOURCE pixels: the head's top a few texels under the frame, centred on
            // the head. Everything outside the source reads as transparent.
            int winW = outW * k, winH = outH * k;
            int winTop = top + 3 * k;
            int winLeft = headX - winW / 2;
            int winBottom = winTop - winH + 1;

            var outPx = new Color32[outW * outH];
            for (int oy = 0; oy < outH; oy++)
            {
                for (int ox = 0; ox < outW; ox++)
                {
                    // Alpha-weighted box average of a k x k block: premultiplied, so a transparent
                    // neighbour's black RGB does not darken the edge.
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int by = 0; by < k; by++)
                        for (int bx = 0; bx < k; bx++)
                        {
                            int sx = winLeft + ox * k + bx, sy = winBottom + oy * k + by;
                            if (sx < 0 || sy < 0 || sx >= sw || sy >= sh) continue;
                            var p = src[sy * sw + sx];
                            float pa = p.a / 255f;
                            r += p.r * pa; g += p.g * pa; b += p.b * pa; a += pa;
                        }
                    float n = k * k;
                    if (a <= 0f) { outPx[oy * outW + ox] = new Color32(0, 0, 0, 0); continue; }
                    float alpha = a / n;
                    // Pixel art stays binary: a block more than half covered is solid.
                    byte oa = (byte)(alpha >= 0.5f ? 255 : 0);
                    outPx[oy * outW + ox] = new Color32((byte)(r / a), (byte)(g / a), (byte)(b / a), oa);
                }
            }

            // One-texel outline around the silhouette, 4-neighbourhood so it stays one texel
            // wide on diagonals — an 8-neighbour ring doubles up at every corner of a sprite.
            var outlined = (Color32[])outPx.Clone();
            Color32 edge = outline;
            for (int y = 0; y < outH; y++)
                for (int x = 0; x < outW; x++)
                {
                    if (outPx[y * outW + x].a != 0) continue;
                    bool near =
                        (x > 0 && outPx[y * outW + x - 1].a != 0) ||
                        (x < outW - 1 && outPx[y * outW + x + 1].a != 0) ||
                        (y > 0 && outPx[(y - 1) * outW + x].a != 0) ||
                        (y < outH - 1 && outPx[(y + 1) * outW + x].a != 0);
                    if (near) outlined[y * outW + x] = edge;
                }

            var tex = new Texture2D(outW, outH, TextureFormat.RGBA32, false, false)
            {
                name = "HudPortrait_" + sprite.name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels32(outlined);
            tex.Apply(false, false);
            return tex;
        }

        // -- Helpers -------------------------------------------------------------------

        private static void GetUvBounds(Sprite sprite, out Vector2 min, out Vector2 max)
        {
            var uvs = sprite.uv;
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < uvs.Length; i++)
            {
                min = Vector2.Min(min, uvs[i]);
                max = Vector2.Max(max, uvs[i]);
            }
            if (uvs.Length == 0) { min = Vector2.zero; max = Vector2.one; }
        }

        private static Texture2D ReadBack(RenderTexture rt, int w, int h, FilterMode filter)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            tex.Apply(false, false);
            RenderTexture.active = prev;
            return tex;
        }
    }
}
