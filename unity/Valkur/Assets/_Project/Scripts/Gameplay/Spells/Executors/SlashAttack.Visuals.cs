using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Rig construction and per-frame drawing. Every style is the same set of parts —
    /// stacked ribbons, a leading glint, an origin ring, air motes and a light — assembled
    /// with different proportions, so they share one update path and still read as four
    /// distinct attacks.
    /// </summary>
    public sealed partial class SlashAttack
    {
        /// <summary>
        /// Sorting orders. The wake sits under the body, the cutting edge over it, and the
        /// inner reflection over that, so the silhouette keeps its depth at any tint.
        /// </summary>
        private const int ORDER_GROUND = 56;
        private const int ORDER_TELEGRAPH = 57;
        private const int ORDER_WAKE = 58;
        private const int ORDER_ECHO = 59;
        private const int ORDER_BODY = 60;
        private const int ORDER_EDGE = 62;
        private const int ORDER_REFLECTION = 63;
        private const int ORDER_MOTE = 64;
        private const int ORDER_GLINT = 66;

        private SlashRibbonMesh _telegraph;
        private readonly System.Collections.Generic.List<SlashLanceMesh> _lances =
            new System.Collections.Generic.List<SlashLanceMesh>(3);

        // ── Construction ──────────────────────────────────────────────────────

        private void BuildVisuals()
        {
            ElementalSprites.EnsureAll();
            Material material = UnlitMeshMaterial.Shared;

            switch (_profile.Style)
            {
                case SlashStyle.Thrust: BuildThrustLayers(material); break;
                case SlashStyle.Cleave: BuildCleaveLayers(material); break;
                case SlashStyle.Whirl: BuildWhirlLayers(material); break;
                default: BuildCrescentLayers(material); break;
            }

            if (_profile.Telegraphs)
            {
                _telegraph = new SlashRibbonMesh(transform, "ReachTelegraph", material,
                    _profile.Segments, _profile.ArcDegrees, 0.86f, 1f,
                    SlashProfile.WithAlpha(_profile.Rim, 1f), _profile.TrailWindow,
                    ORDER_TELEGRAPH);
                _telegraph.Hide();
            }

            BuildGlints();
            BuildMotes();
            BuildLight();
        }

        private void AddRibbon(Material material, string name, float inner, float outer,
                               Color color, float alpha, float trailScale, int order,
                               float taperPower = 0.38f)
        {
            _ribbons.Add(new SlashRibbonMesh(transform, name, material, _profile.Segments,
                _profile.ArcDegrees, inner, outer, SlashProfile.WithAlpha(color, alpha),
                _profile.TrailWindow * trailScale, order, taperPower));
        }

        /// <summary>
        /// A spear of light: wide haze, bright shaft, white-hot core, nested and pointed.
        ///
        /// The haze is sized to the half-width the damage sector actually covers at full
        /// reach, so the widest thing drawn is the widest thing that hits. The shaft and the
        /// core are fractions of it, which is what gives the thrust its gradient instead of
        /// three coincident edges saturating into one white lens.
        /// </summary>
        private void BuildThrustLayers(Material material)
        {
            float sectorHalfWidth = Mathf.Sin(_profile.HalfArc * Mathf.Deg2Rad);
            AddLance(material, "AirLance", sectorHalfWidth, 0.85f,
                     _profile.Atmosphere, 0.30f, ORDER_WAKE);
            AddLance(material, "Lance", sectorHalfWidth * 0.52f, 0.70f,
                     _profile.Body, 0.78f, ORDER_BODY);
            AddLance(material, "LanceCore", sectorHalfWidth * 0.22f, 0.55f,
                     _profile.Edge, 1f, ORDER_EDGE);
        }

        private void AddLance(Material material, string name, float halfWidthFactor,
                              float lengthFactor, Color color, float alpha, int order)
        {
            _lances.Add(new SlashLanceMesh(transform, name, material, _profile.Segments,
                halfWidthFactor, lengthFactor, SlashProfile.WithAlpha(color, alpha), order));
        }

        /// <summary>The classic sweep: wake, crescent body, cutting edge, inner reflection.</summary>
        private void BuildCrescentLayers(Material material)
        {
            AddRibbon(material, "AirWake", 0.42f, 1.08f, _profile.Atmosphere, 0.16f, 1.12f, ORDER_WAKE);
            AddRibbon(material, "CrescentBody", 0.56f, 1f, _profile.Body, 0.70f, 1f, ORDER_BODY);
            AddRibbon(material, "CuttingEdge", 0.80f, 1.015f, _profile.Edge, 0.98f, 0.76f, ORDER_EDGE);
            AddRibbon(material, "InnerReflection", 0.59f, 0.70f, _profile.Rim, 0.45f, 0.46f, ORDER_REFLECTION);
        }

        /// <summary>
        /// A cleave has mass. It gets a thicker body, a slower second wake trailing the
        /// first, and a blunter taper so the shape stays broad instead of needle-pointed.
        /// </summary>
        private void BuildCleaveLayers(Material material)
        {
            AddRibbon(material, "AirWake", 0.30f, 1.14f, _profile.Atmosphere, 0.18f, 1.15f, ORDER_WAKE, 0.30f);
            AddRibbon(material, "WakeEcho", 0.40f, 1.02f, _profile.Atmosphere, 0.30f, 1.35f, ORDER_ECHO, 0.30f);
            AddRibbon(material, "CleaveBody", 0.48f, 1f, _profile.Body, 0.78f, 1f, ORDER_BODY, 0.32f);
            AddRibbon(material, "CuttingEdge", 0.78f, 1.03f, _profile.Edge, 1f, 0.70f, ORDER_EDGE);
            AddRibbon(material, "InnerReflection", 0.52f, 0.66f, _profile.Rim, 0.40f, 0.42f, ORDER_REFLECTION);
        }

        /// <summary>The widest swing — the trail is long enough to close most of the ring.</summary>
        private void BuildWhirlLayers(Material material)
        {
            AddRibbon(material, "AirWake", 0.22f, 1.16f, _profile.Atmosphere, 0.20f, 1.20f, ORDER_WAKE, 0.26f);
            AddRibbon(material, "WakeEcho", 0.34f, 1.06f, _profile.Atmosphere, 0.32f, 1.45f, ORDER_ECHO, 0.26f);
            AddRibbon(material, "WhirlBody", 0.42f, 1f, _profile.Body, 0.82f, 1.05f, ORDER_BODY, 0.28f);
            AddRibbon(material, "CuttingEdge", 0.76f, 1.04f, _profile.Edge, 1f, 0.72f, ORDER_EDGE);
            AddRibbon(material, "InnerReflection", 0.46f, 0.62f, _profile.Rim, 0.42f, 0.40f, ORDER_REFLECTION);
        }

        private void BuildGlints()
        {
            _leadingGlint = CreateSprite("LeadingGlint", ElementalSprites.SparkleStar,
                SlashProfile.WithAlpha(_profile.Rim, 0f), ORDER_GLINT);
            _leadingGlint.transform.localScale = Vector3.one * 0.42f;

            _originRing = CreateSprite("OriginRing", ElementalSprites.Ring,
                SlashProfile.WithAlpha(_profile.LightColor, 0f), ORDER_WAKE - 1);
            _originRing.transform.localScale = Vector3.one * 0.18f;

            if (!_profile.HasGroundWave) return;

            _groundWave = CreateSprite("GroundWave", ElementalSprites.Ring,
                SlashProfile.WithAlpha(_profile.Atmosphere, 0f), ORDER_GROUND);
            _groundWave.transform.localScale = Vector3.one * 0.2f;
        }

        private void BuildMotes()
        {
            int count = _profile.MoteCount;
            _moteTransforms = new Transform[count];
            _moteRenderers = new SpriteRenderer[count];
            _moteRadials = new float[count];

            for (int i = 0; i < count; i++)
            {
                var sr = CreateSprite("AirMote_" + i.ToString("00"), ElementalSprites.Sparkle,
                    SlashProfile.WithAlpha(_profile.Rim, 0f), ORDER_MOTE);
                float scale = Mathf.Lerp(0.055f, 0.15f, (i % 4) / 3f);
                sr.transform.localScale = new Vector3(scale * 2.2f, scale, 1f);
                _moteTransforms[i] = sr.transform;
                _moteRenderers[i] = sr;
                _moteRadials[i] = Mathf.Lerp(0.60f, 1.05f, Mathf.Repeat(i * 0.37f, 1f));
            }
        }

        private void BuildLight()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;

            var lightGo = new GameObject("SlashLight");
            lightGo.transform.SetParent(transform, false);
            try
            {
                _light = lightGo.AddComponent(lightType);
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                if (typeProp != null)
                    typeProp.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 3));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _profile.LightColor);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _profile.Radius * 1.1f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.12f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.82f);
                ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 0f);
            }
            catch
            {
                _light = null;
            }
        }

        private SpriteRenderer CreateSprite(string objectName, Sprite sprite, Color color, int order)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
            sr.sortingLayerName = SortingConfig.LAYER_VFX;
            sr.sortingOrder = order;
            return sr;
        }

        // ── Per-frame drawing ─────────────────────────────────────────────────

        /// <summary>
        /// Anticipation. The blade gathers at the start of its travel while the wide styles
        /// outline the ground they are about to cover.
        /// </summary>
        private void UpdateWindup(float windup01)
        {
            float gather = Mathf.Pow(windup01, 1.8f);

            if (_profile.IsRadial)
                for (int i = 0; i < _lances.Count; i++)
                    _lances[i].Draw(0f, gather * 0.55f, _profile.Radius, 1f);
            else
                for (int i = 0; i < _ribbons.Count; i++)
                    _ribbons[i].Angular(0f, gather * 0.5f, _profile.Radius, 1f);

            _telegraph?.Telegraph(Mathf.Pow(windup01, 1.6f) * TELEGRAPH_PEAK_ALPHA, _profile.Radius);

            if (_leadingGlint != null)
            {
                PlaceGlint(_profile.IsRadial ? 0f : -_profile.HalfArc,
                           _profile.IsRadial ? SlashLanceMesh.RADIAL_START : 0.93f);
                float pulse = 0.20f + gather * 0.30f;
                _leadingGlint.transform.localScale = new Vector3(pulse * 1.5f, pulse, 1f);
                _leadingGlint.color = SlashProfile.WithAlpha(_profile.Rim, gather * 0.8f);
            }

            if (_originRing != null)
            {
                _originRing.transform.localScale = Vector3.one * Mathf.Lerp(0.10f, 0.34f, gather);
                _originRing.color = SlashProfile.WithAlpha(_profile.LightColor, gather * 0.45f);
            }

            UpdateMotes(0f, gather * 0.4f);
            SetLightIntensity(gather * 0.45f);
        }

        /// <summary>Active frames and the dissipation that follows them.</summary>
        private void UpdateActive(float eased, float sweep01, float linger)
        {
            if (_profile.IsRadial)
                for (int i = 0; i < _lances.Count; i++)
                    _lances[i].Draw(eased, linger, _profile.Radius, 1f, 1f - linger);
            else
                for (int i = 0; i < _ribbons.Count; i++)
                    _ribbons[i].Angular(eased, linger, _profile.Radius, 1f);

            // The outline has done its job the moment the blade moves; holding it any
            // longer competes with the swing for the player's eye.
            _telegraph?.Telegraph(
                Mathf.Clamp01(1f - sweep01 * 3f) * TELEGRAPH_PEAK_ALPHA * 0.8f, _profile.Radius);

            UpdateLeadingGlint(eased, sweep01, linger);
            UpdateOriginRing(sweep01, linger);
            UpdateGroundWave(eased, linger);
            UpdateMotes(eased, linger);
            SetLightIntensity((0.55f + Mathf.Sin(sweep01 * Mathf.PI) * 1.25f) * linger);
            PlaceLight(eased);
        }

        private void UpdateLeadingGlint(float eased, float sweep01, float linger)
        {
            if (_leadingGlint == null) return;

            if (_profile.IsRadial)
                PlaceGlint(0f, Mathf.Lerp(SlashLanceMesh.RADIAL_START, 1f, eased));
            else
                PlaceGlint(Mathf.Lerp(-_profile.HalfArc, _profile.HalfArc, eased), 0.93f);

            float pulse = 0.38f + Mathf.Sin(sweep01 * Mathf.PI) * 0.20f;
            // A thrust flashes along its own axis; a swing flashes across the arc it cuts.
            _leadingGlint.transform.localScale = _profile.IsRadial
                ? new Vector3(pulse * 2.4f, pulse * 0.55f, 1f)
                : new Vector3(pulse * 1.5f, pulse, 1f);
            _leadingGlint.color = SlashProfile.WithAlpha(_profile.Rim, linger * 0.95f);
        }

        private void PlaceGlint(float angleDegrees, float reachFraction)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            float distance = _profile.Radius * reachFraction;
            _leadingGlint.transform.localPosition =
                new Vector3(Mathf.Cos(radians) * distance, Mathf.Sin(radians) * distance, 0f);
            _leadingGlint.transform.localRotation = Quaternion.Euler(0f, 0f, angleDegrees);
        }

        private void UpdateOriginRing(float sweep01, float linger)
        {
            if (_originRing == null) return;
            float birth = Mathf.Clamp01(sweep01 * 3.2f);
            _originRing.transform.localScale = Vector3.one * Mathf.Lerp(0.30f, 0.86f, birth);
            _originRing.color = SlashProfile.WithAlpha(_profile.LightColor,
                (1f - birth) * 0.55f * linger);
        }

        /// <summary>
        /// Heavy swings drag a dust ring along the ground at their outer rim. It trails the
        /// blade rather than leading it, which is what sells the weight.
        /// </summary>
        private void UpdateGroundWave(float eased, float linger)
        {
            if (_groundWave == null) return;
            float scale = Mathf.Lerp(_profile.Radius * 0.55f, _profile.Radius * 2.05f,
                                     Mathf.Pow(eased, 0.7f));
            _groundWave.transform.localScale = new Vector3(scale, scale, 1f);
            _groundWave.color = SlashProfile.WithAlpha(_profile.Atmosphere,
                Mathf.Sin(Mathf.Clamp01(eased) * Mathf.PI) * 0.34f * linger);
        }

        private void UpdateMotes(float head01, float linger)
        {
            if (_moteTransforms == null) return;

            for (int i = 0; i < _moteTransforms.Length; i++)
            {
                float lag = 0.07f + i * (0.42f / _moteTransforms.Length);
                float progress = head01 - lag;
                float visibility = Mathf.Clamp01(progress * 16f) *
                                   Mathf.Clamp01((1f - progress) * 10f + 1f) * linger;

                float angle;
                float distance;
                if (_profile.IsRadial)
                {
                    // Speed lines: they stream outward behind the tip, spread across the
                    // narrow arc so the thrust reads as motion rather than a static spike.
                    angle = Mathf.Lerp(-_profile.HalfArc, _profile.HalfArc,
                                       Mathf.Repeat(i * 0.29f, 1f));
                    distance = _profile.Radius * Mathf.Clamp01(progress) * _moteRadials[i];
                }
                else
                {
                    angle = Mathf.Lerp(-_profile.HalfArc, _profile.HalfArc, Mathf.Clamp01(progress));
                    distance = _profile.Radius * _moteRadials[i];
                }

                float radians = angle * Mathf.Deg2Rad;
                _moteTransforms[i].localPosition =
                    new Vector3(Mathf.Cos(radians) * distance, Mathf.Sin(radians) * distance, 0f);
                _moteTransforms[i].localRotation = Quaternion.Euler(0f, 0f,
                    _profile.IsRadial ? angle : angle + 90f);
                _moteRenderers[i].color = SlashProfile.WithAlpha(_profile.Rim, visibility * 0.72f);
            }
        }

        private void PlaceLight(float eased)
        {
            if (_light == null || !_profile.IsRadial) return;
            // A thrust throws its light from the tip, which is the only part of it moving.
            float distance = _profile.Radius * Mathf.Lerp(SlashLanceMesh.RADIAL_START, 1f, eased);
            _light.transform.localPosition = new Vector3(distance, 0f, 0f);
        }

        private void SetLightIntensity(float intensity)
        {
            if (_light == null) return;
            try { ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, intensity); }
            catch { /* URP 2D lighting absent in this project configuration. */ }
        }
    }
}
