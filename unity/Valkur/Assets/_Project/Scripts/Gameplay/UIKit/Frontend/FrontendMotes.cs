using UnityEngine;
using Valkur.UI.MainMenu;

namespace Valkur.UI.Frontend
{
    /// <summary>
    /// Emitting into a <see cref="MenuFxLayer"/> from a point of ANOTHER rect, and the two
    /// particle colours of the language.
    ///
    /// <para><b>The colours are the tint pushed toward white by a quarter, or toward ember by
    /// half — never further.</b> On the additive material light saturates: a spark that is
    /// already half white reads as a white dot, which is the one thing the loading bar's embers
    /// were tuned out of. Keep the hue and let stacking make the hot core.</para>
    /// </summary>
    public static class FrontendMotes
    {
        public static Color Hot(Color tint) => Color.Lerp(tint, Color.white, 0.25f) * 0.9f;
        public static Color Warm(Color tint) => Color.Lerp(tint, FrontendPalette.EmberDeep, 0.45f) * 0.9f;

        /// <summary>
        /// Emits one mote born at <paramref name="localPoint"/> of <paramref name="from"/>'s own
        /// rect space. Works whichever canvas mode, because it goes through world space.
        /// </summary>
        public static bool EmitFrom(MenuFxLayer layer, RectTransform from, Vector2 localPoint, Vector2 velocity,
                                    Color colour, float life, MenuMoteShape shape, float gravity, float drag, float size)
        {
            if (layer == null || from == null) return false;
            var layerRt = layer.rectTransform;
            Vector2 p = layerRt.InverseTransformPoint(from.TransformPoint(localPoint));
            p -= layerRt.rect.min;
            colour.a = 1f;
            return layer.Emit(p, velocity, colour, life, shape, gravity, drag, size, twinkle: true);
        }
    }
}
