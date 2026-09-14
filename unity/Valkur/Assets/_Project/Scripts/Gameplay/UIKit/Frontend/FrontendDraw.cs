using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.Frontend
{
    /// <summary>
    /// The composite pieces of the language, drawn into a <see cref="VertexHelper"/>: the soft
    /// shadow, the bevelled frame with its recessed channel, the corner brackets, the gem, the
    /// diamond notch and the spike a gem sits on.
    ///
    /// <para><b>Stateless and positional</b>, so a panel, a card, a button and the loading bar
    /// draw the SAME frame by calling the same method with a different rect — the only way "one
    /// piece" stays true after the next person tunes a colour.</para>
    ///
    /// <para><b>Order is the caller's</b>: uGUI draws a mesh's triangles in the order they were
    /// added, so a gem added before the frame sits under the frame's edge, which is exactly what
    /// the loading bar's end spikes rely on.</para>
    /// </summary>
    public static class FrontendDraw
    {
        /// <summary>Two soft layers under and slightly below the rect, so the object sits ON the art.</summary>
        public static void Shadow(VertexHelper vh, float x, float y, float w, float h, float scale = 1f)
        {
            float s = scale;
            FrontendMesh.Quad(vh, x - 9f * s, y - 11f * s, w + 18f * s, h + 15f * s, new Color(0f, 0f, 0f, 0.14f));
            FrontendMesh.Quad(vh, x - 4f * s, y - 6f * s, w + 8f * s, h + 8f * s, new Color(0f, 0f, 0f, 0.32f));
        }

        /// <summary>
        /// Outline, a bevel lit from above (bronze at the foot, gold at the head), a one-unit top
        /// light, the inner outline and the recessed channel whose lower lip catches a little
        /// warm light — which is what makes it read as a groove rather than a hole.
        /// </summary>
        public static void BevelFrame(VertexHelper vh, float x, float y, float w, float h, float t, float glow)
        {
            var outline = FrontendPalette.Outline;
            FrontendMesh.Quad(vh, x - 2f, y - 2f, w + 4f, h + 4f, outline);
            Color top = Color.Lerp(FrontendPalette.GoldLight, Color.white, glow * 0.55f);
            Color bottom = Color.Lerp(FrontendPalette.BronzeDark, FrontendPalette.GoldLight, glow * 0.35f);
            FrontendMesh.QuadV(vh, x, y, w, h, bottom, top);
            FrontendMesh.Quad(vh, x, y + h - 1f, w, 1f, FrontendMesh.WithAlpha(Color.white, 0.55f + 0.35f * glow));
            FrontendMesh.Quad(vh, x, y, w, 1f, FrontendMesh.WithAlpha(outline, 0.6f));
            Recess(vh, x + t, y + t, w - 2f * t, h - 2f * t);
        }

        /// <summary>A recessed channel with its one-unit outline and its lit lower lip.</summary>
        public static void Recess(VertexHelper vh, float x, float y, float w, float h)
        {
            if (w <= 0f || h <= 0f) return;
            FrontendMesh.Quad(vh, x - 1f, y - 1f, w + 2f, h + 2f, FrontendPalette.Outline);
            FrontendMesh.QuadV(vh, x, y, w, h, FrontendPalette.RecessBottom, FrontendPalette.RecessTop);
            FrontendMesh.Quad(vh, x, y, w, 1f, FrontendPalette.LipLight);
        }

        /// <summary>L-shaped brackets a few units outside each corner: a rectangle, framed.</summary>
        public static void Brackets(VertexHelper vh, float x, float y, float w, float h, float glow,
                                    float gap = 6f, float len = 13f, float th = 2f)
        {
            Color32 c = FrontendMesh.WithAlpha(Color.Lerp(FrontendPalette.BronzeDark, FrontendPalette.GoldLight,
                                                          0.65f + 0.35f * glow), 0.9f);
            float l = x - gap, rgt = x + w + gap, b = y - gap, tp = y + h + gap;
            float leg = len * 0.7f;
            FrontendMesh.Quad(vh, l - th, b - th, len, th, c);
            FrontendMesh.Quad(vh, l - th, b - th, th, leg, c);
            FrontendMesh.Quad(vh, l - th, tp, len, th, c);
            FrontendMesh.Quad(vh, l - th, tp + th - leg, th, leg, c);
            FrontendMesh.Quad(vh, rgt + th - len, b - th, len, th, c);
            FrontendMesh.Quad(vh, rgt, b - th, th, leg, c);
            FrontendMesh.Quad(vh, rgt + th - len, tp, len, th, c);
            FrontendMesh.Quad(vh, rgt, tp + th - leg, th, leg, c);
        }

        /// <summary>A short gilded rod from <paramref name="from"/> to <paramref name="to"/> that a gem sits on.</summary>
        public static void Spike(VertexHelper vh, float from, float to, float cy)
        {
            FrontendMesh.Quad(vh, from, cy - 2f, to - from, 4f, FrontendPalette.Outline);
            FrontendMesh.QuadV(vh, from, cy - 1f, to - from, 2f, FrontendPalette.BronzeDark, FrontendPalette.GoldLight);
        }

        /// <summary>
        /// A gem: outline, a gold setting, a dark inner ring and a core that "lights" from dim stone
        /// toward <paramref name="tint"/> as <paramref name="lit"/> rises, with a glint when lit.
        /// </summary>
        public static void Gem(VertexHelper vh, float cx, float cy, float r, float lit, Color tint)
        {
            var outline = FrontendPalette.Outline;
            FrontendMesh.Diamond(vh, cx, cy, r + 2.5f, r + 2.5f, outline, outline);
            FrontendMesh.Diamond(vh, cx, cy, r, r, FrontendPalette.GoldLight, FrontendPalette.BronzeDark);
            FrontendMesh.Diamond(vh, cx, cy, r * 0.62f, r * 0.62f, outline, outline);

            Color hot = Color.Lerp(tint, Color.white, 0.55f);
            Color core = Color.Lerp(FrontendPalette.GemDim, hot, lit);
            Color coreDark = Color.Lerp(FrontendPalette.GemDim * 0.6f, tint, lit);
            coreDark.a = 1f;
            FrontendMesh.Diamond(vh, cx, cy, r * 0.5f, r * 0.5f, core, coreDark);
            if (lit > 0.05f)
                FrontendMesh.Diamond(vh, cx - r * 0.12f, cy + r * 0.18f, r * 0.16f, r * 0.16f,
                                     FrontendMesh.WithAlpha(Color.white, lit), FrontendMesh.WithAlpha(Color.white, lit * 0.5f));
        }

        /// <summary>A diamond notch with its outline. <paramref name="passed"/> lights it in the tint.</summary>
        public static void Notch(VertexHelper vh, float cx, float cy, float n, bool passed, Color tint, float glow = 0f)
        {
            Color on = Color.Lerp(Color.Lerp(tint, Color.white, 0.35f), Color.white, glow);
            Color light = passed ? on : Color.Lerp(FrontendPalette.NotchDim, Color.white, 0.12f);
            Color dark = passed ? tint * 0.75f : FrontendPalette.NotchDim * 0.6f;
            light.a = 1f; dark.a = 1f;
            FrontendMesh.Diamond(vh, cx, cy, n + 1.6f, n + 1.6f, FrontendPalette.Outline, FrontendPalette.Outline);
            FrontendMesh.Diamond(vh, cx, cy, n, n, light, dark);
        }

        /// <summary>
        /// A horizontal rule: a dark line with a warm light line under it, ending in two notches.
        /// The menu's divider, and the separator between the load panel's columns when vertical.
        /// </summary>
        public static void Rule(VertexHelper vh, float x, float y, float w, Color tint, bool vertical = false)
        {
            if (!vertical)
            {
                FrontendMesh.Quad(vh, x, y, w, 2f, new Color(0f, 0f, 0f, 0.55f));
                FrontendMesh.Quad(vh, x, y - 1f, w, 1f, FrontendMesh.WithAlpha(FrontendPalette.GoldLight, 0.22f));
                Notch(vh, x, y + 0.5f, 2.6f, true, tint);
                Notch(vh, x + w, y + 0.5f, 2.6f, true, tint);
            }
            else
            {
                FrontendMesh.Quad(vh, x, y, 2f, w, new Color(0f, 0f, 0f, 0.55f));
                FrontendMesh.Quad(vh, x + 2f, y, 1f, w, FrontendMesh.WithAlpha(FrontendPalette.GoldLight, 0.22f));
                Notch(vh, x + 1f, y, 2.6f, true, tint);
                Notch(vh, x + 1f, y + w, 2.6f, true, tint);
            }
        }
    }
}
