using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// The two procedural sprites the ground aim indicator draws with, built once and SHARED.
    ///
    /// <para>The old chevron built a fresh <c>Texture2D</c>, <c>Sprite</c> and <c>Material</c>
    /// per instance and resolved its shader with a runtime <c>Shader.Find</c>. There is one
    /// player, so the waste was small — but the pattern is the one that scales badly the
    /// moment anything else wants an aim indicator, and a per-instance material is a material
    /// nothing else can batch with.</para>
    ///
    /// <para>Both sprites point along <b>+X</b>, so the aim transform's Z rotation is the
    /// heading angle itself. The old sprite pointed up and every consumer had to remember to
    /// subtract 90 degrees — a hand-derived offset on a 2D aim is exactly the shape that put
    /// <c>FlameConeFX</c>'s fire ninety degrees away from its own damage.</para>
    ///
    /// <para>Every sprite here is created at <c>pixelsPerUnit = texture size</c>, so it is
    /// exactly ONE WORLD UNIT across whatever its resolution — the same contract
    /// <c>ElementalSprites</c> keeps, which is what lets a scale constant be read as a world
    /// diameter.</para>
    /// </summary>
    internal static class FacingIndicatorSprites
    {
        private const int TIP_PX = 64;

        // Not `readonly`: the Domain-Reload ratchet only recognises a reset that assigns the
        // field (stsfld), and Domain Reload is OFF, so a cached sprite from a previous Play
        // session is a destroyed native object the next one would draw with.
        private static Sprite _tip;
        private static Sprite _aura;

        public static Sprite Tip { get { EnsureBuilt(); return _tip; } }
        public static Sprite Aura { get { EnsureBuilt(); return _aura; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            _tip = null;
            _aura = null;
        }

        /// <summary>Build both sprites if they are missing. Safe to call every frame.</summary>
        public static void EnsureBuilt()
        {
            if (_tip == null) _tip = BuildChevron(hard: true);
            if (_aura == null) _aura = BuildChevron(hard: false);
        }

        /// <summary>
        /// The chevron, pointing +X, built twice from the SAME two segments.
        ///
        /// <para><paramref name="hard"/> draws the tip: a solid core with a two-texel ramp, the
        /// one piece of the rig with a real edge. Without it the aura alone gives the eye
        /// nothing to lock onto.</para>
        ///
        /// <para>Otherwise it draws the aura: the same arms with a wide, smooth falloff, so the
        /// glow is CHEVRON-SHAPED. A round halo behind an arrow reads as two objects that
        /// happen to overlap; this reads as one thing that is lit. Sharing the generator is
        /// what stops the glow drifting from the shape it frames when either is retuned.</para>
        /// </summary>
        private static Sprite BuildChevron(bool hard)
        {
            var tex = new Texture2D(TIP_PX, TIP_PX, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = hard ? "FacingIndicatorTip" : "FacingIndicatorAura"
            };

            float half = TIP_PX * 0.5f;
            var pixels = new Color[TIP_PX * TIP_PX];
            const float thickness = 0.21f;
            const float apexX = 0.80f;
            const float tailX = -0.52f;
            const float tailY = 0.62f;

            for (int y = 0; y < TIP_PX; y++)
            {
                float ny = (y + 0.5f - half) / half;
                for (int x = 0; x < TIP_PX; x++)
                {
                    float nx = (x + 0.5f - half) / half;

                    float d = Mathf.Min(
                        DistToSegment(nx, ny, tailX, -tailY, apexX, 0f),
                        DistToSegment(nx, ny, tailX, tailY, apexX, 0f));

                    // The tip is a solid core with a two-texel ramp; the aura is the same
                    // arms bled outward. Both fade to nothing before the sprite's edge, or the
                    // glow would end on a hard square.
                    float alpha = hard
                        ? 1f - Smooth01((d - thickness * 0.55f) / (thickness * 0.45f))
                        : 1f - Smooth01((d - thickness * 0.5f) / FacingIndicatorStyle.AuraSpread);
                    pixels[y * TIP_PX + x] = alpha <= 0.002f
                        ? Color.clear
                        : new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0, 0, TIP_PX, TIP_PX),
                new Vector2(0.5f, 0.5f), TIP_PX);
        }

        private static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static float DistToSegment(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float lenSq = dx * dx + dy * dy;
            if (lenSq < 0.0001f) return Mathf.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));

            float t = Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / lenSq);
            float projX = ax + t * dx;
            float projY = ay + t * dy;
            return Mathf.Sqrt((px - projX) * (px - projX) + (py - projY) * (py - projY));
        }
    }
}
