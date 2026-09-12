using System.Collections.Generic;
using UnityEngine;

namespace Valkur.UI.MainMenu.Title
{
    /// <summary>
    /// How bright the carousel is behind the title, measured off the actual image.
    ///
    /// <para><b>Why this exists.</b> The word sits over a painted face that changes every seven
    /// seconds, and its plate carried a CONSTANT. Measured on the shipped menu, the same title
    /// read at <b>7.5:1</b> over the dark arch and <b>4.2:1</b> over the bright one — the logo's
    /// legibility was decided by which frame the carousel happened to be holding, and no value of
    /// a constant fixes both: raise it and the dark frames wear a black bar, lower it and the
    /// bright ones swallow the word. What closes it is measuring the ground and solving for the
    /// plate, which turns a look decision into arithmetic.</para>
    ///
    /// <para><b>It cannot be a `GetPixels`.</b> The carousel art is imported non-readable, like
    /// every shipped texture — reading it directly throws. The measurement is a GPU blit into a
    /// tiny RenderTexture and ONE readback, which works on any texture whatever its import
    /// settings, costs a 16x8 read, and happens once per image rather than once per frame. The
    /// result is cached by sprite, so a carousel that loops pays for each face exactly once.</para>
    ///
    /// <para>It answers RELATIVE LUMINANCE (the WCAG one), not average RGB: the plate is solving
    /// a contrast ratio, and that ratio is defined on luminance.</para>
    /// </summary>
    public static class BackdropLuminance
    {
        /// <summary>The band of the image the title actually covers, in normalized sprite space.</summary>
        private static readonly Rect TitleBand = new Rect(0.18f, 0.62f, 0.64f, 0.30f);

        private const int SampleWidth = 16;
        private const int SampleHeight = 8;

        private static readonly Dictionary<Sprite, float> s_cache = new Dictionary<Sprite, float>();

        /// <summary>
        /// Static mutable state with Domain Reload off. <c>Clear()</c> on the field itself, which
        /// is the form <c>DomainReloadStaticResetTests</c> recognises — it reads this method's raw
        /// IL and a call that passes the field as an ARGUMENT counts as no reset at all.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_cache.Clear();
        }

        /// <summary>
        /// Relative luminance of the strip of <paramref name="sprite"/> the title covers, 0..1.
        /// Answers a mid grey for anything it cannot measure, which is the value that asks the
        /// plate for neither its strongest nor its weakest setting.
        /// </summary>
        public static float Measure(Sprite sprite)
        {
            if (sprite == null) return 0.18f;
            if (s_cache.TryGetValue(sprite, out float cached)) return cached;

            float value = 0.18f;
            var texture = sprite.texture;
            if (texture != null && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                RenderTexture rt = null;
                var previous = RenderTexture.active;
                Texture2D readback = null;
                try
                {
                    rt = RenderTexture.GetTemporary(SampleWidth, SampleHeight, 0,
                                                    RenderTextureFormat.ARGB32);
                    // The sprite's own rect inside its texture — an atlased sprite is a window on
                    // a page, and blitting the whole page would measure five other images.
                    var r = sprite.textureRect;
                    var scale = new Vector2(r.width / texture.width * TitleBand.width,
                                            r.height / texture.height * TitleBand.height);
                    var offset = new Vector2(r.x / texture.width + r.width / texture.width * TitleBand.x,
                                             r.y / texture.height + r.height / texture.height * TitleBand.y);
                    Graphics.Blit(texture, rt, scale, offset);

                    RenderTexture.active = rt;
                    readback = new Texture2D(SampleWidth, SampleHeight, TextureFormat.RGBA32, false);
                    readback.ReadPixels(new Rect(0, 0, SampleWidth, SampleHeight), 0, 0);
                    readback.Apply(false);

                    var pixels = readback.GetPixels();
                    double sum = 0.0;
                    for (int i = 0; i < pixels.Length; i++) sum += Luminance(pixels[i]);
                    if (pixels.Length > 0) value = (float)(sum / pixels.Length);
                }
                catch (System.Exception)
                {
                    // A measurement that fails must not take the menu with it: the plate simply
                    // keeps its authored strength, which is where it was before this existed.
                    value = 0.18f;
                }
                finally
                {
                    RenderTexture.active = previous;
                    if (rt != null) RenderTexture.ReleaseTemporary(rt);
                    if (readback != null) Object.DestroyImmediate(readback);
                }
            }

            s_cache[sprite] = value;
            return value;
        }

        /// <summary>
        /// The plate alpha that puts <paramref name="inkLuminance"/> at <paramref name="target"/>
        /// against a ground of <paramref name="groundLuminance"/>, given a plate of
        /// <paramref name="plateLuminance"/>.
        ///
        /// <para>Solved rather than tuned. The plate lerps the ground toward its own tint, so the
        /// ground the word ends up on is <c>lerp(ground, plate, a)</c>, and the contrast ratio
        /// <c>(ink + 0.05) / (g + 0.05)</c> gives the <c>g</c> that hits the target directly. The
        /// floor is what stops a dark frame leaving the word with no seat at all, and the ceiling
        /// is what stops a bright one drawing a black bar across the art.</para>
        /// </summary>
        public static float PlateAlphaFor(float inkLuminance, float groundLuminance,
                                          float plateLuminance, float target,
                                          float floor, float ceiling)
        {
            float needed = (inkLuminance + 0.05f) / Mathf.Max(1.01f, target) - 0.05f;
            if (groundLuminance <= needed) return Mathf.Clamp01(floor);

            float span = groundLuminance - plateLuminance;
            if (span <= 0.0001f) return Mathf.Clamp01(ceiling);

            float a = (groundLuminance - needed) / span;
            return Mathf.Clamp(a, Mathf.Clamp01(floor), Mathf.Clamp01(ceiling));
        }

        /// <summary>WCAG relative luminance. The same one every contrast test in this project uses.</summary>
        public static float Luminance(Color c)
        {
            return 0.2126f * Channel(c.r) + 0.7152f * Channel(c.g) + 0.0722f * Channel(c.b);
        }

        private static float Channel(float v)
            => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
    }
}
