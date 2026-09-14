using UnityEngine;
using Valkur.Data;

namespace Valkur.UI.Frontend
{
    /// <summary>
    /// The baked resources every pre-game surface shares: the additive material, a radial glow
    /// whose alpha reaches EXACTLY zero at its rim, and the loading bar's fill ramp and gloss.
    ///
    /// <para><b>Its own glow, not the title's mote.</b> That disc keeps a few percent of alpha
    /// at its rim, invisible on an ember and a hard-edged SQUARE once a flash is blown up to the
    /// width of a bar or a panel row — the first capture of the loading bar's finale showed
    /// exactly that.</para>
    ///
    /// <para><b>One per style, cached, freed on the Play-mode boundary.</b> Every menu list,
    /// panel and the loading bar used to build (or would have built) its own material; a shared
    /// cache is what makes adopting the language in forty widgets cost one material. Nothing that
    /// borrows from here may destroy what it borrowed — the kit owns it.</para>
    /// </summary>
    public sealed class FrontendKit
    {
        private static FrontendKit s_instance;
        private static MenuStyle s_style;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance?.Release();
            s_instance = null;
            s_style = null;
        }

        /// <summary>SrcAlpha / One on <c>Valkur/UI/HudFx</c>. Null only when the shader is missing.</summary>
        public Material Additive { get; private set; }

        /// <summary>A soft disc: bright core over a long tail, alpha 0 at the rim.</summary>
        public Sprite Radial { get; private set; }

        /// <summary>The loading bar's fill body, grey so the tint decides the hue.</summary>
        public Sprite BarRamp { get; private set; }

        /// <summary>The loading bar's additive gloss.</summary>
        public Sprite BarGloss { get; private set; }

        public static FrontendKit Get(MenuStyle style)
        {
            if (s_instance != null && s_style == style && s_instance.Radial != null) return s_instance;
            s_instance?.Release();
            s_style = style;
            s_instance = new FrontendKit(style);
            return s_instance;
        }

        private FrontendKit(MenuStyle style)
        {
            Additive = BuildAdditive(style);
            Radial = BakeRadial("FrontendGlow", 96);
            BarRamp = BakeColumn("LoadingBarFillRamp", 64, v =>
            {
                byte b = (byte)Mathf.RoundToInt(FrontendRamp.Bar(v) * 255f);
                return new Color32(b, b, b, 255);
            });
            BarGloss = BakeColumn("LoadingBarGloss", 64, v =>
                new Color32(255, 255, 255, (byte)Mathf.RoundToInt(FrontendRamp.Gloss(v) * 255f)));
        }

        private void Release()
        {
            DestroyOwned(Additive);
            foreach (var s in new[] { Radial, BarRamp, BarGloss })
            {
                if (s == null) continue;
                DestroyOwned(s.texture);
                DestroyOwned(s);
            }
            Additive = null; Radial = null; BarRamp = null; BarGloss = null;
        }

        /// <summary><c>Destroy</c> is an ERROR in Edit Mode, and every fixture builds these there.</summary>
        public static void DestroyOwned(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        private static Material BuildAdditive(MenuStyle style)
        {
            var shader = style != null && style.hudFxShader != null
                ? style.hudFxShader
                : Shader.Find("Valkur/UI/HudFx");
            if (shader == null) return null;
            var mat = new Material(shader) { name = "FrontendAdditive", hideFlags = HideFlags.DontSave };
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            return mat;
        }

        private static Sprite BakeColumn(string name, int h, System.Func<float, Color32> pixel)
        {
            const int w = 2;
            var tex = NewTexture(name, w, h);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                var c = pixel((y + 0.5f) / h);
                for (int x = 0; x < w; x++) px[y * w + x] = c;
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return MakeSprite(name, tex, w, h);
        }

        /// <summary>A soft disc whose alpha reaches exactly zero at its rim, so any size is edgeless.</summary>
        private static Sprite BakeRadial(string name, int size)
        {
            var tex = NewTexture(name, size, size);
            var px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                float t = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                // A bright core over a long tail: t^2 alone reads as a disc, t^4 alone as a dot.
                float a = 0.55f * t * t + 0.45f * t * t * t * t;
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return MakeSprite(name, tex, size, size);
        }

        private static Texture2D NewTexture(string name, int w, int h) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };

        private static Sprite MakeSprite(string name, Texture2D tex, int w, int h)
        {
            // FullRect: Tight would trace an alpha outline nothing reads (the 20 ms trap).
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
