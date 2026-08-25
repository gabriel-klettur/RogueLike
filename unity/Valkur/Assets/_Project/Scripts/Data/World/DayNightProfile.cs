using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Authored 24-hour look of the day/night cycle: one <see cref="Gradient"/> for the ambient
    /// colour plus curves for intensity and vignette strength.
    ///
    /// Replaces the two hardcoded keyframes the cycle used to carry. Those could only ever
    /// interpolate along the straight RGB segment from day-white to night-blue, which made a
    /// warm sunrise or a golden hour <b>mathematically unreachable</b> no matter how the
    /// sliders were set — see <c>.github/DAY_NIGHT_AUDIT_AND_ROADMAP.md</c> §2.
    ///
    /// A Gradient also brings Unity's own editor for free and, unlike the old
    /// <c>[SerializeField]</c> literals on a component the bootstrap creates with
    /// <c>AddComponent</c>, this is an asset a designer can actually open and edit.
    ///
    /// Note Unity caps a Gradient at 8 colour keys. That is the real budget for the whole day,
    /// so every key has to earn its place; the ramp below spends them on the two moments the
    /// eye cares about (dawn and the golden-hour/sunset pair) and lets the flat day and night
    /// bands hold.
    /// </summary>
    [CreateAssetMenu(fileName = "DayNightProfile", menuName = "Valkur/Lighting/Day-Night Profile")]
    public class DayNightProfile : ScriptableObject
    {
        [Tooltip("Ambient light colour across the day. x = normalized time (0 = midnight, 0.5 = noon).")]
        [SerializeField] private Gradient ambientColor = new Gradient();

        [Tooltip("Light2D intensity across the day. Day should sit at 1.0 so the world reads at native colours.")]
        [SerializeField] private AnimationCurve ambientIntensity = AnimationCurve.Linear(0f, 1f, 1f, 1f);

        [Tooltip("Screen-edge vignette opacity across the day.")]
        [SerializeField] private AnimationCurve vignetteAlpha = AnimationCurve.Linear(0f, 0f, 1f, 0f);

        [Tooltip("Colour saturation across the day, 1 = untouched. Consumed by the ScreenGrade " +
                  "renderer feature — a Multiply light can darken and tint, but it cannot drain " +
                  "saturation, and a night that keeps full daytime saturation reads as a blue filter.")]
        [SerializeField] private AnimationCurve saturation = AnimationCurve.Linear(0f, 1f, 1f, 1f);

        [Tooltip("Contrast across the day, 1 = untouched. Applied in LogC around ACEScc mid-grey " +
                  "so lifting it does not crush a dim frame to black.")]
        [SerializeField] private AnimationCurve contrast = AnimationCurve.Linear(0f, 1f, 1f, 1f);

        [Header("Phase bands (normalized 0..1)")]
        [Tooltip("Start of the dawn ramp — night begins turning into day.")]
        [SerializeField, Range(0f, 1f)] private float dawnStart = 0.18f;
        [Tooltip("Start of the flat day band.")]
        [SerializeField, Range(0f, 1f)] private float dayStart = 0.30f;
        [Tooltip("Start of the dusk ramp — day begins turning into night.")]
        [SerializeField, Range(0f, 1f)] private float duskStart = 0.70f;
        [Tooltip("Start of the flat night band (wraps midnight).")]
        [SerializeField, Range(0f, 1f)] private float nightStart = 0.84f;

        /// <summary>Upper bound for the sampled intensity. See <see cref="Sample"/>.</summary>
        public const float MaxIntensity = 1f;

        public float DawnStart  => dawnStart;
        public float DayStart   => dayStart;
        public float DuskStart  => duskStart;
        public float NightStart => nightStart;

        /// <summary>
        /// False when the asset exists but carries no usable ramp (a freshly created instance,
        /// or one whose curves were emptied). Callers fall back to their own defaults rather
        /// than driving the world to black.
        /// </summary>
        public bool IsUsable =>
            ambientColor != null && ambientColor.colorKeys != null && ambientColor.colorKeys.Length >= 2 &&
            ambientIntensity != null && ambientIntensity.length >= 2 &&
            vignetteAlpha != null && vignetteAlpha.length >= 1;

        /// <summary>Vignette tint. Kept separate from the ambient colour so the screen edges can
        /// stay neutral while the world takes a hue — tying them together is what made the old
        /// overlay inherit every limitation of the light.</summary>
        public Color VignetteTint = new Color(0.03f, 0.04f, 0.09f, 1f);

        /// <summary>
        /// Sample the screen-grade half of the ramp: what the full-screen pass should do that the
        /// ambient light cannot. Separate from <see cref="Sample"/> because the light path runs
        /// every frame and has no use for these.
        /// </summary>
        public void SampleGrade(float t, out float saturationOut, out float contrastOut)
        {
            t = Mathf.Repeat(t, 1f);
            saturationOut = saturation != null ? Mathf.Max(0f, saturation.Evaluate(t)) : 1f;
            contrastOut   = contrast   != null ? Mathf.Max(0f, contrast.Evaluate(t))   : 1f;
        }

        /// <summary>Sample the authored look at normalized time <paramref name="t"/>.</summary>
        public void Sample(float t, out Color color, out float intensity, out float vignette)
        {
            t         = Mathf.Repeat(t, 1f);
            color     = ambientColor.Evaluate(t);
            color.a   = 1f;
            // Clamped, not just floored: a smoothed curve overshoots between a rising key and
            // a plateau, and anything above 1 on a Multiply Light2D with HDREmulationScale 1
            // clips flat to white instead of blooming — a measured 1.05 at noon bleached the
            // whole frame for no visual gain.
            intensity = Mathf.Clamp(ambientIntensity.Evaluate(t), 0f, MaxIntensity);
            vignette  = Mathf.Clamp01(vignetteAlpha.Evaluate(t));
        }

        /// <summary>
        /// Read the look of the flat Day or Night plateau — the values the runtime editors'
        /// Day/Night tabs show. Sampled at the middle of the band so a mid-ramp key cannot
        /// masquerade as the plateau.
        /// </summary>
        public void ReadPlateau(bool night, out Color color, out float intensity, out float vignette)
        {
            // Read the KEY, not a sample. A gradient sample taken inside the plateau is already
            // sliding toward the next key outside it — the night plateau's own midpoint sits
            // 5 % of the way to the blue-hour key — so sampling would answer "what is Night set
            // to?" with a colour nobody ever wrote, and a runtime slider would drift every time
            // the panel round-tripped through it.
            color     = PlateauKeyColor(night);
            intensity = PlateauKeyValue(ambientIntensity, night, MaxIntensity * 0.5f);
            vignette  = PlateauKeyValue(vignetteAlpha,    night, 0f);
        }

        private Color PlateauKeyColor(bool night)
        {
            var keys = ambientColor.colorKeys;
            for (int i = 0; i < keys.Length; i++)
                if (InPlateau(keys[i].time, night)) return keys[i].color;

            // No key inside the band — fall back to sampling its middle.
            Sample(PlateauSampleTime(night), out var sampled, out _, out _);
            return sampled;
        }

        private float PlateauKeyValue(AnimationCurve curve, bool night, float fallback)
        {
            if (curve == null) return fallback;
            for (int i = 0; i < curve.length; i++)
                if (InPlateau(curve[i].time, night)) return curve[i].value;
            return curve.Evaluate(PlateauSampleTime(night));
        }

        /// <summary>
        /// Rewrite the flat Day or Night plateau so a runtime slider edits what the author
        /// expects. Only keys inside the plateau move; the dawn and dusk ramps keep their
        /// shape and simply re-interpolate toward the new plateau value.
        /// </summary>
        public void WritePlateau(bool night, Color color, float intensity, float vignette)
        {
            color.a = 1f;

            var keys = ambientColor.colorKeys;
            for (int i = 0; i < keys.Length; i++)
                if (InPlateau(keys[i].time, night)) keys[i].color = color;
            ambientColor.SetKeys(keys, ambientColor.alphaKeys);

            OverwriteCurveInPlateau(ambientIntensity, night, Mathf.Clamp(intensity, 0f, MaxIntensity));
            OverwriteCurveInPlateau(vignetteAlpha,    night, Mathf.Clamp01(vignette));
        }

        private float PlateauSampleTime(bool night)
        {
            if (!night) return Mathf.Lerp(dayStart, duskStart, 0.5f);
            // Night wraps midnight: [nightStart, 1) ∪ [0, dawnStart). Its midpoint is the
            // middle of that wrapped span, taken modulo 1.
            float span = (1f - nightStart) + dawnStart;
            return Mathf.Repeat(nightStart + span * 0.5f, 1f);
        }

        private bool InPlateau(float t, bool night) => night
            ? (t >= nightStart || t < dawnStart)
            : (t >= dayStart && t < duskStart);

        private void OverwriteCurveInPlateau(AnimationCurve curve, bool night, float value)
        {
            if (curve == null) return;
            for (int i = 0; i < curve.length; i++)
            {
                var k = curve[i];
                if (!InPlateau(k.time, night)) continue;
                k.value = value;
                curve.MoveKey(i, k);
            }
        }

        /// <summary>
        /// The shipped ramp, in code so the asset can be regenerated and so a missing asset
        /// still has something honest to fall back to.
        ///
        /// Night is the level chosen after measuring the real frame: legible town, unambiguous
        /// night. Day sits at pure white and intensity 1.0 — the identity for a Multiply
        /// Light2D — so noon reads at native texture colours with no wash. Everything
        /// interesting happens in the two ramps.
        /// </summary>
        public void LoadShippedRamp()
        {
            var night = new Color(0.30f, 0.36f, 0.62f);

            ambientColor = new Gradient();
            ambientColor.mode = GradientMode.Blend;
            ambientColor.SetKeys(
                new[]
                {
                    new GradientColorKey(night,                          0.00f), // noche
                    new GradientColorKey(new Color(0.34f, 0.38f, 0.74f), 0.19f), // hora azul
                    new GradientColorKey(new Color(0.94f, 0.62f, 0.54f), 0.25f), // amanecer cálido
                    new GradientColorKey(new Color(1.00f, 0.96f, 0.90f), 0.34f), // mañana dorada suave
                    new GradientColorKey(Color.white,                    0.58f), // mediodía neutro
                    new GradientColorKey(new Color(1.00f, 0.78f, 0.52f), 0.74f), // golden hour
                    new GradientColorKey(new Color(0.70f, 0.44f, 0.60f), 0.82f), // malva del ocaso
                    new GradientColorKey(night,                          0.88f), // noche
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

            ambientIntensity = Smooth(new[]
            {
                new Keyframe(0.00f, 0.32f), new Keyframe(0.18f, 0.33f), new Keyframe(0.24f, 0.48f),
                new Keyframe(0.30f, 0.80f), new Keyframe(0.38f, 1.00f), new Keyframe(0.62f, 1.00f),
                new Keyframe(0.70f, 0.95f), new Keyframe(0.76f, 0.72f), new Keyframe(0.81f, 0.48f),
                new Keyframe(0.86f, 0.34f), new Keyframe(1.00f, 0.32f),
            });

            vignetteAlpha = Smooth(new[]
            {
                new Keyframe(0.00f, 0.28f), new Keyframe(0.20f, 0.24f), new Keyframe(0.30f, 0.02f),
                new Keyframe(0.68f, 0.02f), new Keyframe(0.78f, 0.12f), new Keyframe(0.86f, 0.28f),
                new Keyframe(1.00f, 0.28f),
            });

            // Saturation drains as the light goes and recovers with it. Night at 0.72 is the
            // difference between a blue-tinted day and a night; noon stays at 1 so the art reads
            // as authored. Contrast lifts slightly at night to keep the frame from going flat once
            // saturation is gone.
            saturation = Smooth(new[]
            {
                new Keyframe(0.00f, 0.72f), new Keyframe(0.19f, 0.74f), new Keyframe(0.26f, 0.88f),
                new Keyframe(0.34f, 1.00f), new Keyframe(0.66f, 1.00f), new Keyframe(0.78f, 0.90f),
                new Keyframe(0.86f, 0.74f), new Keyframe(1.00f, 0.72f),
            });

            contrast = Smooth(new[]
            {
                new Keyframe(0.00f, 1.10f), new Keyframe(0.30f, 1.00f),
                new Keyframe(0.70f, 1.00f), new Keyframe(0.88f, 1.10f), new Keyframe(1.00f, 1.10f),
            });

            dawnStart  = 0.18f;
            dayStart   = 0.30f;
            duskStart  = 0.70f;
            nightStart = 0.84f;
        }

        /// <summary>
        /// Smooth tangents everywhere except at plateau keys, which are flattened.
        ///
        /// Without the flattening, Unity's smoothing carries the slope of the incoming ramp
        /// past a key that is meant to be the top of the curve, so a 0.80 → 1.00 rise
        /// overshoots to 1.05 before settling. On a Multiply light that is a clipped, bleached
        /// midday; on the vignette curve it is a negative alpha.
        /// </summary>
        private static AnimationCurve Smooth(Keyframe[] keys)
        {
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);

            for (int i = 0; i < curve.length; i++)
            {
                bool flatLeft  = i == 0                || Mathf.Approximately(curve[i - 1].value, curve[i].value);
                bool flatRight = i == curve.length - 1 || Mathf.Approximately(curve[i + 1].value, curve[i].value);
                if (!flatLeft && !flatRight) continue;

                var k = curve[i];
                k.inTangent  = 0f;
                k.outTangent = 0f;
                curve.MoveKey(i, k);
            }
            return curve;
        }
    }
}
