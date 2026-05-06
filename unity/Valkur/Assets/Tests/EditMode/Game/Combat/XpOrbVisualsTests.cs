using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Pins the canonical XP-orb visual hierarchy: SpriteRenderer with the
    /// blue gradient sprite, a sparkle ParticleSystem child, and a
    /// XpOrbPulse animator. Build is idempotent. Sprite samples carry a
    /// blue-dominant tone (regression guard against accidental palette
    /// rollback to the old green orb).
    /// </summary>
    [TestFixture]
    public class XpOrbVisualsTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _go = new GameObject("XpOrb");
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void BuildVisuals_AddsSpriteRenderer_WithGradientSprite()
        {
            XpOrb.BuildVisuals(_go);

            var sr = _go.GetComponent<SpriteRenderer>();
            Assert.IsNotNull(sr, "BuildVisuals must add a SpriteRenderer.");
            Assert.IsNotNull(sr.sprite, "SpriteRenderer must reference the orb sprite.");
            Assert.AreEqual("XpOrbSprite", sr.sprite.name);
            Assert.AreEqual(SortingConfig.LAYER_ENTITIES, sr.sortingLayerName);
        }

        [Test]
        public void BuildVisuals_AddsSparklesChild_WithParticleSystem()
        {
            XpOrb.BuildVisuals(_go);

            var sparkles = _go.transform.Find("Sparkles");
            Assert.IsNotNull(sparkles, "BuildVisuals must add a 'Sparkles' child.");
            Assert.IsNotNull(sparkles.GetComponent<ParticleSystem>(),
                "Sparkles child must carry a ParticleSystem.");
        }

        [Test]
        public void BuildVisuals_AddsPulseAnimator()
        {
            XpOrb.BuildVisuals(_go);
            Assert.IsNotNull(_go.GetComponent<XpOrbPulse>(),
                "BuildVisuals must attach the pulse animator for the breathing scale.");
        }

        [Test]
        public void BuildVisuals_IsIdempotent()
        {
            XpOrb.BuildVisuals(_go);
            XpOrb.BuildVisuals(_go);

            Assert.AreEqual(1, _go.GetComponents<SpriteRenderer>().Length,
                "BuildVisuals must not stack SpriteRenderers across calls.");
            Assert.AreEqual(1, _go.GetComponents<XpOrbPulse>().Length,
                "BuildVisuals must not stack pulse animators across calls.");

            int sparkleChildren = 0;
            foreach (Transform t in _go.transform)
                if (t.name == "Sparkles") sparkleChildren++;
            Assert.AreEqual(1, sparkleChildren,
                "BuildVisuals must not stack Sparkles children across calls.");
        }

        [Test]
        public void OrbSprite_CenterPixel_IsBlueDominant()
        {
            var sprite = XpOrb.GetOrbSprite();
            var tex = sprite.texture;
            int cx = tex.width / 2;
            int cy = tex.height / 2;

            // Sample a 3x3 patch around the centre to dampen single-pixel noise.
            float r = 0, g = 0, b = 0;
            int n = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                Color px = tex.GetPixel(cx + dx, cy + dy);
                r += px.r; g += px.g; b += px.b; n++;
            }
            r /= n; g /= n; b /= n;

            Assert.That(b, Is.GreaterThanOrEqualTo(g),
                $"Centre patch must be blue-dominant. Sampled (r={r:F2}, g={g:F2}, b={b:F2}).");
            Assert.That(b, Is.GreaterThan(0.7f),
                "Centre blue must be bright (>=0.7).");
        }

        [Test]
        public void OrbSprite_EdgePixel_IsTransparent()
        {
            var sprite = XpOrb.GetOrbSprite();
            var tex = sprite.texture;

            Color corner = tex.GetPixel(0, 0);
            Assert.That(corner.a, Is.LessThan(0.05f),
                "Corner pixel must be transparent so the halo fades cleanly.");
        }

        [Test]
        public void OrbSprite_MidRingPixel_IsBlueDominant()
        {
            // Sample roughly halfway out from the centre — that's the gem
            // body, the slice that should read unmistakably blue rather
            // than the white hot-spot core.
            var sprite = XpOrb.GetOrbSprite();
            var tex = sprite.texture;
            int cx = tex.width / 2;
            int cy = tex.height / 2;
            int dx = tex.width / 4; // 12 px out at 48 px size

            Color px = tex.GetPixel(cx + dx, cy);
            Assert.That(px.b, Is.GreaterThan(px.r + 0.15f),
                $"Mid-ring pixel must be blue-dominant. Sampled (r={px.r:F2}, g={px.g:F2}, b={px.b:F2}).");
            Assert.That(px.b, Is.GreaterThan(0.85f),
                "Mid-ring blue channel must be saturated.");
        }

        [Test]
        public void BuildVisuals_AssignsUnlitMaterial_ToSpriteRenderer()
        {
            XpOrb.BuildVisuals(_go);
            var sr = _go.GetComponent<SpriteRenderer>();
            Assert.IsNotNull(sr.sharedMaterial,
                "SpriteRenderer must have an explicit material so URP doesn't render it black without a Light2D.");

            string shaderName = sr.sharedMaterial.shader != null
                ? sr.sharedMaterial.shader.name
                : "";
            // Accept any of the known unlit / sprites shaders. Reject the
            // default Sprite-Lit-Default which would go black under URP 2D
            // without a scene Light2D.
            Assert.That(shaderName, Does.Match(@"Sprites/Default|Sprite-Unlit-Default|Unlit/Transparent"),
                $"Unexpected sprite shader '{shaderName}'. Want unlit / sprites variant.");
        }

        [Test]
        public void Sparkles_UseUrpCompatibleMaterial()
        {
            XpOrb.BuildVisuals(_go);
            var sparkles = _go.transform.Find("Sparkles");
            var renderer = sparkles.GetComponent<ParticleSystemRenderer>();
            Assert.IsNotNull(renderer.sharedMaterial,
                "ParticleSystemRenderer must have an explicit material — the default 'Default-Particle' shader renders magenta in URP.");

            string shaderName = renderer.sharedMaterial.shader != null
                ? renderer.sharedMaterial.shader.name
                : "";
            Assert.That(shaderName, Does.Match(@"Sprites/Default|Sprite-Unlit-Default|Unlit/Transparent"),
                $"Unexpected sparkle shader '{shaderName}'. Want unlit / sprites variant.");
        }

        [Test]
        public void Sparkles_StartColor_IsWhite()
        {
            XpOrb.BuildVisuals(_go);
            var ps = _go.transform.Find("Sparkles").GetComponent<ParticleSystem>();
            var main = ps.main;
            Color start = main.startColor.color;
            Assert.That(start.r, Is.GreaterThan(0.95f), "Particle red channel should be near white.");
            Assert.That(start.g, Is.GreaterThan(0.95f), "Particle green channel should be near white.");
            Assert.That(start.b, Is.GreaterThan(0.95f), "Particle blue channel should be near white.");
        }

        [Test]
        public void Pulse_TickAdvancesScaleAroundBase()
        {
            XpOrb.BuildVisuals(_go);
            var pulse = _go.GetComponent<XpOrbPulse>();
            Vector3 baseScale = _go.transform.localScale;

            // Sample several phases — scale must drift above and below base.
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 60; i++)
            {
                pulse.Tick(0.05f);
                float k = _go.transform.localScale.x / baseScale.x;
                if (k < min) min = k;
                if (k > max) max = k;
            }

            Assert.That(min, Is.LessThan(1f),
                "Pulse must reach below base scale at some phase.");
            Assert.That(max, Is.GreaterThan(1f),
                "Pulse must reach above base scale at some phase.");
        }

        [Test]
        public void Pulse_ResetForReuse_RestoresBaseScale()
        {
            XpOrb.BuildVisuals(_go);
            var pulse = _go.GetComponent<XpOrbPulse>();
            Vector3 baseScale = _go.transform.localScale;

            // Drive enough ticks to drift the scale far from base.
            for (int i = 0; i < 50; i++) pulse.Tick(0.05f);
            pulse.ResetForReuse();

            Assert.That(_go.transform.localScale, Is.EqualTo(baseScale).Using(new Vector3EqualityComparer(0.0001f)),
                "ResetForReuse must restore the captured base scale.");
        }

        // Lightweight Vector3 comparer — keeps the test self-contained.
        private class Vector3EqualityComparer : System.Collections.Generic.IEqualityComparer<Vector3>
        {
            private readonly float _eps;
            public Vector3EqualityComparer(float eps) { _eps = eps; }
            public bool Equals(Vector3 a, Vector3 b) =>
                Mathf.Abs(a.x - b.x) <= _eps &&
                Mathf.Abs(a.y - b.y) <= _eps &&
                Mathf.Abs(a.z - b.z) <= _eps;
            public int GetHashCode(Vector3 v) => v.GetHashCode();
        }
    }
}
