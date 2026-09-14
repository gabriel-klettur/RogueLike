using UnityEngine;
using Valkur.UI.MainMenu;

namespace Valkur.UI.Frontend
{
    /// <summary>
    /// An icon's particle answer to an event, in the motion its <see cref="FrontendMoteStyle"/>
    /// says the thing is made of: a flame throws embers up, a cloud lets flakes fall, a portal
    /// blows a ring outward.
    ///
    /// <para><b>Only ever called for an EVENT</b> — a hover arriving, a click, a state turning on.
    /// There is no tick here and nothing that emits at rest, the rule every layer of this kit
    /// keeps.</para>
    /// </summary>
    public static class FrontendIconMotes
    {
        /// <summary>How much bigger than the loading bar's motes an icon's are: sized for a 46 px socket.</summary>
        public const float SizeScale = 1.6f;

        /// <summary>The dimmest a mote's colour may be (HSV value). Additive light adds its colour: a dim one adds a smudge.</summary>
        public const float MinValue = 0.85f;

        /// <summary>The palest a mote's colour may be (HSV saturation). Additive light saturates: a pale one reads as a white dot.</summary>
        public const float MinSaturation = 0.35f;

        /// <summary>
        /// The colour of one mote of <paramref name="style"/> for an icon of <paramref name="accent"/>,
        /// <paramref name="t"/> 0..1 picking along the style's ramp. Pure, so the floor is testable.
        ///
        /// <para><b>Every result is lifted to <see cref="MinValue"/> and <see cref="MinSaturation"/>.</b>
        /// The first launcher shipped Dust as the accent pushed toward BRONZE and Snow as warm GREY;
        /// over the panel's near-black channel an additive dim brown adds almost nothing and a grey
        /// adds grey, so both read on screen as dirt on the glass (reported from Play, 2026-09-14).
        /// On this material brightness is the whole of a particle; the floor makes that a property
        /// of the function instead of a matter of picking the right constants each time.</para>
        /// </summary>
        public static Color MoteColour(FrontendMoteStyle style, Color accent, float t)
        {
            t = Mathf.Clamp01(t);
            Color c;
            switch (style)
            {
                case FrontendMoteStyle.Embers: c = Color.Lerp(accent, FrontendPalette.EmberDeep, t * 0.6f); break;
                case FrontendMoteStyle.Snow: c = Color.Lerp(accent, FrontendIconTheme.Frost, 0.65f + 0.3f * t); break;
                case FrontendMoteStyle.Dust: c = Color.Lerp(accent, FrontendPalette.GoldLight, 0.35f * t); break;
                case FrontendMoteStyle.Orbit: c = Color.Lerp(accent, FrontendPalette.WarmWhite, 0.2f * t); break;
                case FrontendMoteStyle.Glints: c = Color.Lerp(accent, FrontendPalette.WarmWhite, 0.25f); break;
                default: c = Color.Lerp(accent, FrontendPalette.EmberDeep, 0.45f * t); break;
            }
            return Vivid(c);
        }

        /// <summary>Lifts a colour to the brightness and saturation floors, keeping its hue.</summary>
        public static Color Vivid(Color c)
        {
            Color.RGBToHSV(c, out float h, out float sat, out float val);
            var result = Color.HSVToRGB(h, Mathf.Max(sat, MinSaturation), Mathf.Max(val, MinValue));
            result.a = 1f;
            return result;
        }

        /// <summary>
        /// Throws <paramref name="count"/> motes from a circle of <paramref name="radius"/> around
        /// <paramref name="centre"/> (in <paramref name="from"/>'s rect space).
        /// </summary>
        public static int Burst(MenuFxLayer layer, RectTransform from, Vector2 centre, float radius,
                                Color accent, FrontendMoteStyle style, int count)
        {
            if (layer == null || from == null || count <= 0) return 0;
            int emitted = 0;
            for (int i = 0; i < count; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 at = centre + dir * radius * Random.Range(0.35f, 1f);
                Vector2 vel;
                Color colour;
                float life, gravity, drag, size;
                MenuMoteShape shape;
                switch (style)
                {
                    case FrontendMoteStyle.Embers:
                        at = centre + new Vector2(Random.Range(-radius, radius), Random.Range(-radius, radius * 0.2f));
                        vel = new Vector2(Random.Range(-18f, 18f), Random.Range(50f, 120f));
                        colour = MoteColour(style, accent, Random.value);
                        life = Random.Range(0.6f, 1.2f); gravity = -30f; drag = 0.9f; size = Random.Range(0.12f, 0.26f);
                        shape = MenuMoteShape.Dot;
                        break;
                    case FrontendMoteStyle.Snow:
                        at = centre + new Vector2(Random.Range(-radius * 1.2f, radius * 1.2f), radius * Random.Range(0.4f, 1.1f));
                        vel = new Vector2(Random.Range(-14f, 14f), Random.Range(-30f, -12f));
                        colour = MoteColour(style, accent, Random.value);
                        life = Random.Range(0.9f, 1.5f); gravity = 8f; drag = 0.4f; size = Random.Range(0.12f, 0.22f);
                        shape = MenuMoteShape.Dot;
                        break;
                    case FrontendMoteStyle.Dust:
                        at = centre + new Vector2(Random.Range(-radius, radius), -radius * Random.Range(0.6f, 1f));
                        vel = new Vector2(Mathf.Sign(Random.value - 0.5f) * Random.Range(30f, 80f), Random.Range(10f, 45f));
                        colour = MoteColour(style, accent, Random.value);
                        life = Random.Range(0.45f, 0.9f); gravity = 70f; drag = 2.2f; size = Random.Range(0.14f, 0.3f);
                        shape = MenuMoteShape.Dot;
                        break;
                    case FrontendMoteStyle.Orbit:
                        at = centre + dir * radius * 0.55f;
                        vel = dir * Random.Range(60f, 120f) + new Vector2(-dir.y, dir.x) * 40f;
                        colour = MoteColour(style, accent, Random.value);
                        life = Random.Range(0.35f, 0.65f); gravity = 0f; drag = 2.6f; size = Random.Range(0.14f, 0.26f);
                        shape = Random.value < 0.5f ? MenuMoteShape.Spark : MenuMoteShape.Dot;
                        break;
                    case FrontendMoteStyle.Glints:
                        vel = dir * Random.Range(4f, 18f);
                        colour = MoteColour(style, accent, Random.value);
                        life = Random.Range(0.35f, 0.7f); gravity = 0f; drag = 1.5f; size = Random.Range(0.22f, 0.42f);
                        shape = MenuMoteShape.Spark;
                        break;
                    default:
                        vel = new Vector2(dir.x * Random.Range(30f, 90f), Random.Range(60f, 150f));
                        colour = MoteColour(style, accent, Random.value);
                        life = Random.Range(0.35f, 0.7f); gravity = 240f; drag = 1.3f; size = Random.Range(0.14f, 0.28f);
                        shape = Random.value < 0.55f ? MenuMoteShape.Spark : MenuMoteShape.Dot;
                        break;
                }
                // Sized for a 46 px socket: at the loading bar's mote sizes a burst around an icon
                // was a few pixels of dust nobody could see (first capture of the launcher).
                size *= SizeScale;
                if (FrontendMotes.EmitFrom(layer, from, at, vel, colour, life, shape, gravity, drag, size)) emitted++;
            }
            return emitted;
        }
    }
}
