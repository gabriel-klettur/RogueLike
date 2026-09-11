using UnityEngine;
using Valkur.Data;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The few pieces the tool dialect draws that the player panel's atlas does not have: its
    /// frame, a dash for the budget lines, and one glyph per faction. Everything else — glyphs,
    /// the recess, the white fill, the motes — comes from <see cref="HudArt"/>, so the two
    /// surfaces share their letters and their motes.
    ///
    /// <para><b>The frame has no gold and no bevel</b> (H1): outline, a one-texel shadow line,
    /// flat stone. That absence is the whole visual difference between an instrument of the
    /// game and an instrument ABOUT the game, and it is the one a glance picks up first.</para>
    ///
    /// <para>Sprites use <see cref="HudArt.SpritePixelsPerUnit"/> so one texture pixel is one
    /// panel texel under <c>Image.Type.Sliced</c>, exactly like the player panel.</para>
    /// </summary>
    public sealed class DebugHudArt
    {
        private const int Size = 32;

        public Texture2D Texture { get; private set; }
        public Sprite Frame { get; private set; }
        public Texture2D DashTexture { get; private set; }
        public Sprite Hostile { get; private set; }
        public Sprite Neutral { get; private set; }
        public Sprite Ally { get; private set; }

        /// <summary>Destroys the generated textures and sprites. Safe to call twice.</summary>
        public void Dispose()
        {
            DestroySafe(Frame);
            DestroySafe(Hostile); DestroySafe(Neutral); DestroySafe(Ally);
            DestroySafe(Texture); DestroySafe(DashTexture);
            Frame = Hostile = Neutral = Ally = null;
            Texture = null;
            DashTexture = null;
        }

        public static DebugHudArt Build(DebugHudStyle style)
        {
            style.ResolveSurfaces(out var outline, out var panel, out _, out _, out _, out _);
            var art = new DebugHudArt();
            var px = new Color32[Size * Size];

            // Frame: 7x7, 3-texel borders — chamfered corner, outline, a shadow line, stone.
            var shadow = Color.Lerp(outline, panel, 0.35f);
            for (int y = 0; y < 7; y++)
                for (int x = 0; x < 7; x++)
                {
                    int d = Mathf.Min(Mathf.Min(x, y), Mathf.Min(6 - x, 6 - y));
                    bool corner = (x == 0 || x == 6) && (y == 0 || y == 6);
                    Color c = corner ? Color.clear : d == 0 ? outline : d == 1 ? shadow : panel;
                    px[(1 + y) * Size + 1 + x] = c;
                }

            // Dash: its own 4x1 texture, two on two off, white — tinted and TILED by a RawImage,
            // which needs Repeat wrapping the shared texture cannot have.
            var dash = new Texture2D(4, 1, TextureFormat.RGBA32, false, false)
            {
                name = "DebugHud_Dash",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.DontSave,
            };
            dash.SetPixels32(new[]
            {
                new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
                new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 0),
            });
            dash.Apply(false, false);
            art.DashTexture = dash;

            // Faction glyphs, 5x5 plus a baked outline: a diamond hunts you, a disc does not,
            // a cross fights for you. Shape first, colour second (R6).
            WritePattern(px, 16, 1, new[] { "  #  ", " ### ", "#####", " ### ", "  #  " });
            WritePattern(px, 24, 1, new[] { " ### ", "#####", "#####", "#####", " ### " });
            WritePattern(px, 16, 9, new[] { "  #  ", "  #  ", "#####", "  #  ", "  #  " });

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false, false)
            {
                name = "DebugHud_Art",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            art.Texture = tex;

            art.Frame = Make(tex, "frame", new Rect(1, 1, 7, 7), new Vector4(3, 3, 3, 3));
            art.Hostile = Make(tex, "hostile", new Rect(16, 1, 7, 7), Vector4.zero);
            art.Neutral = Make(tex, "neutral", new Rect(24, 1, 7, 7), Vector4.zero);
            art.Ally = Make(tex, "ally", new Rect(16, 9, 7, 7), Vector4.zero);
            return art;
        }

        private static void WritePattern(Color32[] px, int ox, int oy, string[] rows)
        {
            var glyph = HudArt.Rasterise(rows, c => c == '#' ? (Color32?)new Color32(255, 255, 255, 255) : null,
                                         outline: true, out int w, out int h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[(oy + y) * Size + ox + x] = glyph[y * w + x];
        }

        private static Sprite Make(Texture2D tex, string name, Rect r, Vector4 border)
        {
            var s = Sprite.Create(tex, r, new Vector2(0.5f, 0.5f), HudArt.SpritePixelsPerUnit, 0,
                                  SpriteMeshType.FullRect, border);
            s.name = "DebugHud_" + name;
            s.hideFlags = HideFlags.DontSave;
            return s;
        }

        private static void DestroySafe(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
