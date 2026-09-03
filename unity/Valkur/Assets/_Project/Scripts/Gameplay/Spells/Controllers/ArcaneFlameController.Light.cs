using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The arcane flame's <c>Light2D</c>, built to the same recipe every torch and lamp
    /// in the world uses (<see cref="WorldLightLoader"/>) rather than the reflection
    /// path this file replaced.
    ///
    /// Five things that path got wrong, all fixed here:
    ///
    /// * BLEND STYLE. <c>Light2D.blendStyleIndex</c> defaults to 0 = MULTIPLY, and the
    ///   old code never set it — so the flame's light could only ever DARKEN the ground
    ///   it fell on. URP hardcodes a blend style as purely multiplicative or purely
    ///   additive, so one light cannot both illuminate a surface and glow over it. The
    ///   project's answer is two lights: a BODY on multiply and a small additive CORE.
    /// * COLOUR SPACE. The project renders Linear and URP's 2D path hands
    ///   <c>Light2D.color</c> to the shader with no conversion, so an authored sRGB
    ///   purple arrives as desaturated pink-lavender. <see cref="ToRadiance"/> is the
    ///   same one-liner <c>WorldLightLoader</c> applies to every world light.
    /// * RADIUS. The old light hung off a root scaled by the spell radius, so its
    ///   authored 3.5 u rendered at ~8.75 u. This controller never scales its root, so
    ///   there is nothing to counter-scale.
    /// * INTENSITY. A flat additive 1.6 was roughly 18x the additive energy of a shipped
    ///   torch. The numbers here sit in the same band as the four catalog presets.
    /// * FLICKER. The old remap was applied twice and compressed the variation to 2.25 %.
    ///   This is the two-octave Perlin shape the loader uses, whose <c>*2-1</c> keeps the
    ///   MEAN at the authored intensity instead of drifting brighter.
    /// </summary>
    public partial class ArcaneFlameController
    {
        // Same band as the shipped presets (Candle 0.22 / Lamp 0.30 / Torch 0.35 /
        // Magic 0.40). A little hotter than Magic because this one is a hazard.
        private const float LightBaseIntensity = 0.45f;
        private const float LightSurfaceMix    = 0.55f;   // as LightPreset_Magic
        private const float LightSurfaceGain   = 5f;      // every shipped preset uses 5
        private const float LightFalloff       = 0.60f;
        private const float LightCenterScale   = 0.05f;
        private const float LightCoreScale     = 0.35f;
        private const float LightOuterMul      = 1.15f;   // just past the damage edge
        private const float LightFlickerAmp    = 0.16f;
        private const float LightFlickerSpeed  = 0.90f;

        private GameObject _lightGo;
        private Light2D _lightBody;
        private Light2D _lightCore;
        private float _bodyIntensity;
        private float _coreIntensity;
        private float _lightFlickerOffset;

        /// <summary>
        /// URP's 2D path never converts <c>Light2D.color</c>, so in Linear space an
        /// authored sRGB colour is consumed as linear radiance and every channel ratio is
        /// pulled toward 1 on the way back out. Measured on the Magic preset: authored
        /// saturation 0.529 rendered as 0.225 without this and 0.410 with it. The peak
        /// channel is untouched, so the brightness ceiling is preserved.
        /// </summary>
        private static Color ToRadiance(Color authored)
            => QualitySettings.activeColorSpace == ColorSpace.Linear ? authored.linear : authored;

        private void AttachLight()
        {
            _lightFlickerOffset = Random.Range(0f, 10f);

            _lightGo = new GameObject("FlameLight");
            _lightGo.transform.SetParent(transform, false);
            _lightGo.transform.localPosition = Vector3.zero;

            float outer = _radius * LightOuterMul;
            Color radiance = ToRadiance(_palette.lightColor);

            _lightBody = _lightGo.AddComponent<Light2D>();
            _lightBody.lightType = Light2D.LightType.Point;
            _lightBody.blendStyleIndex = 0;                 // Multiply — the ambient buffer
            _lightBody.color = radiance;
            _lightBody.pointLightOuterRadius = outer;
            _lightBody.pointLightInnerRadius = outer * LightCenterScale;
            _lightBody.falloffIntensity = Mathf.Clamp01(LightFalloff);
            _lightBody.shadowsEnabled = false;              // URP derives the caster shape
                                                            // from Renderer bounds; every
                                                            // caster throws a rectangle.

            // The additive core is what makes a multiply-buffer light read as EMISSIVE
            // rather than as a stain. It must be a CHILD at local zero so it inherits the
            // body's transform exactly.
            var coreGo = new GameObject("Core");
            coreGo.transform.SetParent(_lightGo.transform, false);
            coreGo.transform.localPosition = Vector3.zero;
            _lightCore = coreGo.AddComponent<Light2D>();
            _lightCore.lightType = Light2D.LightType.Point;
            _lightCore.blendStyleIndex = 1;                 // Additive
            _lightCore.color = radiance;                    // already converted
            _lightCore.pointLightOuterRadius = outer * LightCoreScale;
            _lightCore.pointLightInnerRadius = outer * LightCoreScale * LightCenterScale;
            _lightCore.falloffIntensity = Mathf.Clamp01(LightFalloff);
            _lightCore.shadowsEnabled = false;

            _bodyIntensity = LightBaseIntensity * LightSurfaceMix * Mathf.Max(0.5f, LightSurfaceGain);
            _coreIntensity = LightBaseIntensity * (1f - LightSurfaceMix);

            _lightBody.intensity = 0f;   // ramps up with the ignition envelope
            _lightCore.intensity = 0f;
        }

        private void AnimateLight()
        {
            if (_lightBody == null) return;

            // Two-octave Perlin. A sine is periodic and the eye finds the period within a
            // second or two, which reads as a pulsing bulb rather than as fire.
            float t = (Time.time + _lightFlickerOffset) * LightFlickerSpeed;
            float body    = Mathf.PerlinNoise(t,        _lightFlickerOffset)       * 2f - 1f;
            float flutter = Mathf.PerlinNoise(t * 3.7f, _lightFlickerOffset + 17f) * 2f - 1f;
            float factor  = 1f + (body * 0.7f + flutter * 0.3f) * LightFlickerAmp;

            // Both halves breathe together, or the light changes COLOUR as it flickers —
            // the two reach the frame through different terms of the same composite.
            float env = _envelopeAlpha * (1f + 0.55f * _pulsePhase);
            _lightBody.intensity = _bodyIntensity * factor * env;
            if (_lightCore != null) _lightCore.intensity = _coreIntensity * factor * env;
        }

        private void HideLight()
        {
            if (_lightBody != null) _lightBody.intensity = 0f;
            if (_lightCore != null) _lightCore.intensity = 0f;
        }

        // ── Why this light is NOT gated on the day/night cycle ─────────────────
        //
        // It used to be, through DayNightCycle.OnLightsEnabledChanged, matching what
        // WorldLightLoader does to every torch and lamp — SetActive(false) on the whole
        // light object during the daylight window. That is right for a WORLD FIXTURE, which
        // is a thing that exists all day and should only be seen to burn at night, and it
        // made the arcane flame the only one of the eleven spell controllers carrying a
        // Light2D that went dark at noon. A spell is not a fixture: it lives five seconds,
        // the player casts it in whatever hour they are in, and the half of the rig that
        // says "this ground is dangerous" cannot be absent for most of the session.
        //
        // Keeping it lit costs nothing at noon either. The BODY is on the multiply blend
        // style, and multiplying into an ambient buffer that is already at full daylight
        // changes very little; what still reads is the additive CORE, which is exactly the
        // half that should. Dropping the subscription also removes a static-delegate
        // lifetime (Domain Reload is OFF) that had to be unwound on all five exit paths.

    }
}
