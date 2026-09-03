using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The shell's envelopes: the white flash, the shockwave ring and the <c>Light2D</c> pair.
    ///
    /// <para>Everything here is a RAMP. The version this replaces lit a point light at a fixed
    /// intensity and called <c>Destroy(lightGo, 0.20f)</c> — a square pulse, on and off, which
    /// is the one shape a detonation never has. A flash that pops off reads as a rendering
    /// glitch rather than as light.</para>
    /// </summary>
    public partial class FireworkBurstFX
    {
        private Light2D _lightBody;
        private Light2D _lightCore;
        private float _lightLife;
        private float _bodyIntensity;
        private float _coreIntensity;

        /// <summary>
        /// URP's 2D path never converts <c>Light2D.color</c>, so in Linear space an authored
        /// sRGB colour is consumed as linear radiance and every channel ratio is pulled toward
        /// 1 on the way out. Same one-liner <c>WorldLightLoader</c> and the arcane flame apply.
        /// </summary>
        private static Color ToRadiance(Color authored)
            => QualitySettings.activeColorSpace == ColorSpace.Linear ? authored.linear : authored;

        private void BuildLight()
        {
            _lightLife = STAR_LIFETIME * LIGHT_LIFE_FRACTION;

            var go = new GameObject("BurstLight");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            float outer = _radius * LIGHT_RADIUS_MUL;
            Color radiance = ToRadiance(_palette.Flash);

            // Two lights, because URP hardcodes a blend style as purely multiplicative or
            // purely additive: one light cannot both illuminate a surface and glow over it.
            // The BODY lights the world; the CORE is what makes it read as emissive rather
            // than as a stain.
            _lightBody = go.AddComponent<Light2D>();
            _lightBody.lightType = Light2D.LightType.Point;
            _lightBody.blendStyleIndex = 0;                  // Multiply — the ambient buffer
            _lightBody.color = radiance;
            _lightBody.pointLightOuterRadius = outer;
            _lightBody.pointLightInnerRadius = outer * 0.10f;
            _lightBody.falloffIntensity = 0.70f;
            _lightBody.shadowsEnabled = false;               // URP derives caster shape from
                                                             // Renderer bounds; every building
                                                             // would throw a rectangle.
            _lightBody.intensity = 0f;

            var coreGo = new GameObject("Core");
            coreGo.transform.SetParent(go.transform, false);
            coreGo.transform.localPosition = Vector3.zero;

            _lightCore = coreGo.AddComponent<Light2D>();
            _lightCore.lightType = Light2D.LightType.Point;
            _lightCore.blendStyleIndex = 1;                  // Additive
            _lightCore.color = radiance;
            _lightCore.pointLightOuterRadius = outer * 0.45f;
            _lightCore.pointLightInnerRadius = outer * 0.05f;
            _lightCore.falloffIntensity = 0.70f;
            _lightCore.shadowsEnabled = false;
            _lightCore.intensity = 0f;

            _bodyIntensity = LIGHT_BODY_INTENSITY;
            _coreIntensity = LIGHT_CORE_INTENSITY;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            AnimateFlash();
            AnimateRing();
            AnimateLight();
        }

        /// <summary>
        /// The white core. Alpha is COVERAGE on an additive material, so the intensity dial is
        /// the COLOUR and it is allowed past 1 — <c>Camera.allowHDR</c> and the URP asset's
        /// <c>supportsHDR</c> are both on, so the excess survives to the framebuffer. Reaching
        /// for alpha instead would widen the flash into fog rather than harden it.
        /// </summary>
        private void AnimateFlash()
        {
            if (_flashCore == null) return;

            float t = _age / FLASH_SECONDS;
            if (t >= 1f)
            {
                SetFlashAlpha(0f, 0f);
                return;
            }

            // Attack in three frames, release over the rest. A symmetric envelope reads as a
            // lamp being turned up and down.
            const float attack = 0.10f;
            float env = t < attack ? t / attack : Mathf.Pow(1f - (t - attack) / (1f - attack), 1.6f);

            // The flash also EXPANDS slightly, which is what says the shell is opening rather
            // than a light being switched on at a point.
            float grow = Mathf.Lerp(0.75f, 1.25f, Mathf.Clamp01(t));
            SetFlashAlpha(env, grow);
        }

        private void SetFlashAlpha(float env, float grow)
        {
            const float coreGain = 2.6f;    // overdriven: see AnimateFlash
            const float glowGain = 1.7f;
            const float haloGain = 1.15f;

            Tint(_flashCore, _palette.Flash, coreGain * env, env, _radius * 0.55f * grow);
            Tint(_flashGlow, _palette.Flash, glowGain * env, env * 0.85f, _radius * 1.15f * grow);
            Tint(_flashHalo, _palette.Sky, haloGain * env, env * 0.55f, _radius * 2.30f * grow);
        }

        private static void Tint(SpriteRenderer sr, Color hue, float gain, float alpha, float size)
        {
            if (sr == null) return;
            sr.color = new Color(hue.r * gain, hue.g * gain, hue.b * gain, Mathf.Clamp01(alpha));
            if (size > 0f) sr.transform.localScale = Vector3.one * size;
        }

        /// <summary>
        /// The shockwave. It expands to exactly the star radius and stops there, which is the
        /// rig's one promise about how big the shell is — a ring that overshoots makes the
        /// stars look like they fell short.
        /// </summary>
        private void AnimateRing()
        {
            if (_ring == null) return;

            float t = _age / RING_SECONDS;
            if (t >= 1f)
            {
                _ring.color = new Color(1f, 1f, 1f, 0f);
                return;
            }

            // Fast out, easing into its final radius.
            float eased = 1f - Mathf.Pow(1f - t, 2.2f);
            _ring.transform.localScale = Vector3.one * (_ringSpan * eased);

            float alpha = Mathf.Pow(1f - t, 1.4f) * 0.75f;
            Color hue = _palette.Flash;
            _ring.color = new Color(hue.r * 1.4f, hue.g * 1.4f, hue.b * 1.4f, alpha);
        }

        /// <summary>
        /// Both lights breathe together, or the burst changes COLOUR as it fades — the two
        /// reach the frame through different terms of the same composite.
        /// </summary>
        private void AnimateLight()
        {
            if (_lightBody == null) return;

            float t = _age / _lightLife;
            if (t >= 1f)
            {
                _lightBody.intensity = 0f;
                if (_lightCore != null) _lightCore.intensity = 0f;
                return;
            }

            const float attack = 0.06f;
            float env = t < attack
                ? t / attack
                : Mathf.Pow(1f - (t - attack) / (1f - attack), 1.9f);

            _lightBody.intensity = _bodyIntensity * env;
            if (_lightCore != null) _lightCore.intensity = _coreIntensity * env;
        }
    }
}
