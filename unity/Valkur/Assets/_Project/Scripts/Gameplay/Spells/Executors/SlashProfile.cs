using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Silhouette family a slash belongs to. Resolved from the authored arc, because the
    /// arc is the one field that already says what the swing is: a 40° attack is a thrust
    /// whatever it is called, and a 260° one is a whirl. Nothing else needs authoring for
    /// a slash to get the right shape, weight and timing.
    /// </summary>
    public enum SlashStyle
    {
        /// <summary>Narrow lance driven outward along the cast direction.</summary>
        Thrust,
        /// <summary>The classic one-handed sweep.</summary>
        Crescent,
        /// <summary>Heavy two-handed swing — more mass, slower, ground dust.</summary>
        Cleave,
        /// <summary>Rotational area sweep, telegraphed before it lands.</summary>
        Whirl,
    }

    /// <summary>
    /// Everything a slash's look and feel derive from, resolved once at spawn.
    ///
    /// The three authored numbers (arc, radius, lifetime) decide the style; the style
    /// decides the beat lengths, the trail length, the segment budget, the shake and the
    /// hit-stop. That keeps a designer tuning three readable values instead of twenty
    /// timing constants, and guarantees two spells with the same arc read the same way.
    /// </summary>
    public readonly struct SlashProfile
    {
        /// <summary>Arc boundaries between the four families, in degrees.</summary>
        private const float THRUST_MAX_ARC = 55f;
        private const float CRESCENT_MAX_ARC = 108f;
        private const float CLEAVE_MAX_ARC = 175f;

        /// <summary>Max channel below which a tint is treated as a void colour.</summary>
        private const float VOID_BRIGHTNESS = 0.25f;

        /// <summary>
        /// Reach the per-style segment and mote budgets are authored against. A slash half
        /// that size covers half the screen distance and needs nowhere near the same number
        /// of segments to look smooth — and NPC basic melee, at roughly one unit of reach,
        /// now draws a crescent on every swing from every monster on screen.
        /// </summary>
        private const float REFERENCE_RADIUS = 2.6f;

        private const int MIN_SEGMENTS = 12;
        private const int MIN_MOTES = 4;

        public readonly SlashStyle Style;
        public readonly float Radius;
        public readonly float ArcDegrees;
        public readonly float HalfArc;

        /// <summary>Anticipation: the blade is drawn back / the danger zone is outlined.</summary>
        public readonly float Windup;
        /// <summary>Active frames. Damage happens here and only here.</summary>
        public readonly float Sweep;
        /// <summary>Dissipation. Cosmetic only.</summary>
        public readonly float Linger;
        public readonly float Total;

        /// <summary>How far behind the head the trail stays lit, in sweep fractions.</summary>
        public readonly float TrailWindow;
        public readonly int Segments;
        public readonly int MoteCount;

        public readonly float ShakeAmplitude;
        public readonly float ShakeDuration;
        public readonly float HitStopSeconds;

        /// <summary>A near-black tint is rendered as a void blade, not lifted to grey.</summary>
        public readonly bool IsVoid;
        public readonly Color Atmosphere;
        public readonly Color Body;
        public readonly Color Edge;
        public readonly Color Rim;
        public readonly Color LightColor;

        /// <summary>True when the head travels outward in radius instead of in angle.</summary>
        public bool IsRadial => Style == SlashStyle.Thrust;

        /// <summary>Wide swings outline their reach during the wind-up so they stay fair.</summary>
        public bool Telegraphs => Style == SlashStyle.Cleave || Style == SlashStyle.Whirl;

        /// <summary>Heavy swings scuff the ground at their outer rim.</summary>
        public bool HasGroundWave => Style == SlashStyle.Cleave || Style == SlashStyle.Whirl;

        public float SweepStart => Windup;
        public float SweepEnd => Windup + Sweep;

        private SlashProfile(SlashStyle style, float radius, float arcDegrees, float lifetime,
                             Color tint)
        {
            Style = style;
            Radius = radius;
            ArcDegrees = arcDegrees;
            HalfArc = arcDegrees * 0.5f;

            float minTotal;
            float windupShare, sweepShare;
            int baseSegments, baseMotes;
            switch (style)
            {
                case SlashStyle.Thrust:
                    // A thrust is mostly anticipation and a very short strike. The share
                    // left over is the recovery, during which the blade is drawn back.
                    minTotal = 0.32f; windupShare = 0.34f; sweepShare = 0.20f;
                    TrailWindow = 0.34f; baseSegments = 20; baseMotes = 8;
                    ShakeAmplitude = 0.14f; ShakeDuration = 0.10f; HitStopSeconds = 0.055f;
                    break;
                case SlashStyle.Cleave:
                    minTotal = 0.50f; windupShare = 0.26f; sweepShare = 0.36f;
                    TrailWindow = 0.86f; baseSegments = 52; baseMotes = 14;
                    ShakeAmplitude = 0.28f; ShakeDuration = 0.20f; HitStopSeconds = 0.060f;
                    break;
                case SlashStyle.Whirl:
                    minTotal = 0.70f; windupShare = 0.40f; sweepShare = 0.32f;
                    TrailWindow = 1.00f; baseSegments = 72; baseMotes = 20;
                    ShakeAmplitude = 0.40f; ShakeDuration = 0.26f; HitStopSeconds = 0.070f;
                    break;
                default:
                    minTotal = 0.38f; windupShare = 0.18f; sweepShare = 0.34f;
                    TrailWindow = 0.72f; baseSegments = 40; baseMotes = 9;
                    ShakeAmplitude = 0.18f; ShakeDuration = 0.14f; HitStopSeconds = 0.045f;
                    break;
            }

            // Budgets scale with reach, floored so even a tiny swing keeps a smooth edge.
            float detail = Mathf.Clamp01(radius / REFERENCE_RADIUS);
            Segments = Mathf.Clamp(Mathf.RoundToInt(baseSegments * detail), MIN_SEGMENTS, baseSegments);
            MoteCount = Mathf.Clamp(Mathf.RoundToInt(baseMotes * detail), MIN_MOTES, baseMotes);

            Total = Mathf.Max(minTotal, lifetime);
            Windup = Total * windupShare;
            Sweep = Total * sweepShare;
            Linger = Total - Windup - Sweep;

            float maxChannel = Mathf.Max(tint.r, Mathf.Max(tint.g, tint.b));
            IsVoid = maxChannel < VOID_BRIGHTNESS;
            if (IsVoid)
            {
                // A void blade must stay dark to read as dark. Instead of lifting the
                // body towards grey — which is what made hostile_slash_dark look like a
                // washed-out smear — the mass stays near-black and the readability is
                // carried entirely by a violet rim and the light it throws.
                Color violet = new Color(0.55f, 0.30f, 1f, 1f);
                Atmosphere = new Color(0.12f, 0.05f, 0.22f, 1f);
                Body = new Color(0.03f, 0.02f, 0.06f, 1f);
                Edge = violet;
                Rim = Color.Lerp(violet, Color.white, 0.55f);
                LightColor = violet;
            }
            else
            {
                Atmosphere = Color.Lerp(tint, new Color(0.28f, 0.62f, 1f, 1f), 0.35f);
                Body = Color.Lerp(tint, Color.white, 0.30f);
                Edge = Color.Lerp(tint, Color.white, 0.78f);
                Rim = Color.Lerp(tint, Color.white, 0.92f);
                LightColor = tint;
            }
        }

        /// <summary>Resolves the family and every derived constant for one cast.</summary>
        public static SlashProfile Build(float arcDegrees, float radius, float lifetime, Color tint)
        {
            float arc = Mathf.Clamp(arcDegrees, 12f, 350f);
            SlashStyle style = arc <= THRUST_MAX_ARC ? SlashStyle.Thrust
                             : arc <= CRESCENT_MAX_ARC ? SlashStyle.Crescent
                             : arc <= CLEAVE_MAX_ARC ? SlashStyle.Cleave
                             : SlashStyle.Whirl;
            return new SlashProfile(style, Mathf.Max(0.25f, radius), arc, lifetime, tint);
        }

        /// <summary>Same colour with an explicit alpha. Saves a local at every call site.</summary>
        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
