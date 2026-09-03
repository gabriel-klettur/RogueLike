using UnityEngine;
using UnityEngine.Rendering.Universal;
using Valkur.Core;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// What the shell looks like on the way up: the burning head, its trail, and the mortar
    /// flash left behind at the caster.
    ///
    /// <para>THE TRAIL IS THE REASON THIS FILE EXISTS. The preset it replaces was
    /// <c>loops: false</c> and left <c>worldSpace</c> at its default — so it emitted twelve
    /// particles once, at the spawn frame, in LOCAL space, and they were then carried along by
    /// the projectile for the rest of the flight. That is not a trail, it is a blob being
    /// dragged, and it is the exact failure <c>ParticleEmitter</c>'s own simulation-space
    /// comment describes. World space is what makes a trail a trail.</para>
    /// </summary>
    public partial class FireworkShellController
    {
        private const float MUZZLE_SECONDS = 0.30f;
        private const int LAUNCH_SPARKS = 26;

        /// <summary>
        /// Drawn size of the burning head. Named because the climb stretch rewrites the scale
        /// every frame and a second literal there would drift from the one used to build it.
        /// </summary>
        private const float HEAD_SIZE = 0.34f;

        private SpriteRenderer _head;
        private SpriteRenderer _headGlow;
        private ParticleSystem _trail;

        private Light2D _muzzleLight;
        private float _muzzleIntensity;

        private void Build()
        {
            ElementalSprites.EnsureAll();

            _head = MakeHeadSprite("Head", ElementalSprites.HotCore, 4, HEAD_SIZE, _palette.Flash, 2.2f);
            _headGlow = MakeHeadSprite("HeadGlow", ElementalSprites.Glow, 2, 0.85f, _palette.Trail, 1.3f);

            BuildTrail();
            BuildLaunchSparks();
            BuildMuzzleLight();
        }

        private SpriteRenderer MakeHeadSprite(string name, Sprite sprite, int order,
                                              float size, Color hue, float gain)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * size;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            sr.sortingLayerName = SortingConfig.LAYER_OVERHEAD;
            sr.sortingOrder = order;
            // Overdriven colour, untouched alpha: on an additive material alpha is COVERAGE
            // and the colour is the brightness dial.
            sr.color = new Color(hue.r * gain, hue.g * gain, hue.b * gain, 1f);
            return sr;
        }

        private void BuildTrail()
        {
            var go = new GameObject("Trail");
            go.transform.SetParent(transform, false);

            _trail = go.AddComponent<ParticleSystem>();
            // AddComponent starts the system immediately and main.duration cannot be written
            // while one is playing — Stop, configure, Play.
            _trail.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _trail.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.20f, 0.42f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.75f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
            main.gravityModifier = 0.35f;
            main.maxParticles = 220;
            main.stopAction = ParticleSystemStopAction.None;
            // WORLD. Anything else and the sparks travel with the shell and nothing is left
            // behind — see the class doc.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(
                Warm(_palette.Trail, 1.0f), Warm(_palette.RandomStar(), 0.75f));

            var emission = _trail.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            // Over DISTANCE, not over time: a shell that is climbing slowly should lay a
            // thinner trail, and a rate over time draws the same density at any speed.
            emission.rateOverDistance = 44f;

            var shape = _trail.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;

            var col = _trail.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = _trail.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = ParticleMaterialCache.Get(ElementalSprites.Sparkle.texture, true);
            renderer.sortingLayerName = SortingConfig.LAYER_OVERHEAD;
            renderer.sortingOrder = 1;

            _trail.Play();
        }

        /// <summary>
        /// The mortar throwing sparks at the caster's feet. One system rather than the
        /// twenty-six <c>GameObject</c>s this replaces, each of which carried its own
        /// <c>SpriteRenderer</c> and <c>MonoBehaviour</c>, per cast, with no pooling.
        /// </summary>
        private void BuildLaunchSparks()
        {
            var go = new GameObject("LaunchSparks");
            go.transform.position = _launchPos;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.4f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.32f, 0.75f);
            main.startSpeed = 0f;             // per-particle velocity below
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.28f);
            main.gravityModifier = 1.1f;
            main.maxParticles = LAUNCH_SPARKS + 4;
            main.stopAction = ParticleSystemStopAction.Destroy;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = false;

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.separateAxes = false;
            limit.limit = new ParticleSystem.MinMaxCurve(0.6f);
            limit.dampen = 0.09f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = ParticleMaterialCache.Get(ElementalSprites.SparkleStar.texture, true);
            renderer.sortingLayerName = SortingConfig.LAYER_VFX;
            renderer.sortingOrder = 50;

            ps.Play();

            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < LAUNCH_SPARKS; i++)
            {
                // A fan, not a circle: the mortar throws sparks UP and outward, and a full
                // circle puts a quarter of them into the ground.
                float angle = Mathf.Lerp(Mathf.PI * 0.05f, Mathf.PI * 0.95f, i / (float)(LAUNCH_SPARKS - 1))
                              + Random.Range(-0.12f, 0.12f);
                float speed = Random.Range(2.4f, 5.2f);

                p.position = _launchPos;
                p.velocity = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * speed;
                p.startColor = _palette.RandomStar();
                p.rotation = Random.Range(0f, 360f);
                p.angularVelocity = Random.Range(-320f, 320f);

                ps.Emit(p, 1);
            }

            // stopAction Destroy tears the object down once the last spark is gone, so nothing
            // has to time it — but only after the system itself has stopped, which is why the
            // duration is short and the lifetimes are not. Guarded: Destroy is refused in Edit
            // Mode, where the contract tests build this rig.
            if (Application.isPlaying) Destroy(go, 2f);
        }

        private void BuildMuzzleLight()
        {
            var go = new GameObject("MuzzleFlash");
            go.transform.position = _launchPos;

            _muzzleLight = go.AddComponent<Light2D>();
            _muzzleLight.lightType = Light2D.LightType.Point;
            _muzzleLight.blendStyleIndex = 1;                 // Additive: this is a flare, not a stain
            _muzzleLight.color = QualitySettings.activeColorSpace == ColorSpace.Linear
                ? _palette.Trail.linear
                : _palette.Trail;
            _muzzleLight.pointLightOuterRadius = 2.6f;
            _muzzleLight.pointLightInnerRadius = 0.3f;
            _muzzleLight.falloffIntensity = 0.75f;
            _muzzleLight.shadowsEnabled = false;
            _muzzleLight.intensity = 0f;

            _muzzleIntensity = 1.6f;
            if (Application.isPlaying) Destroy(go, MUZZLE_SECONDS + 0.05f);
        }

        /// <summary>
        /// Per-frame look while climbing. The head stretches along travel and dims as the
        /// shell gets further away, and the mortar flash decays on its own clock.
        /// </summary>
        private void AnimateClimb(float t, Vector3 delta)
        {
            // The mortar flash. A RAMP: the version this replaces held a fixed intensity and
            // then destroyed the light outright, which is a square pulse and reads as a
            // rendering glitch rather than as a flash.
            if (_muzzleLight != null)
            {
                float m = _age / MUZZLE_SECONDS;
                _muzzleLight.intensity = m >= 1f ? 0f : _muzzleIntensity * Mathf.Pow(1f - m, 2.1f);
            }

            if (_head == null) return;

            // Stretch along travel. Cheap, and it is what says the head is MOVING rather than
            // being repositioned.
            float speed = delta.magnitude / Mathf.Max(0.0001f, Time.deltaTime);
            float stretch = 1f + Mathf.Clamp01(speed / 14f) * 0.85f;
            _head.transform.localScale = new Vector3(HEAD_SIZE, HEAD_SIZE * stretch, 1f);

            // Dims toward the apex: the shell is burning out as it coasts, which is what makes
            // the burst read as an event rather than as the same light getting bigger.
            float fade = Mathf.Lerp(1f, 0.55f, t);
            Tint(_head, _palette.Flash, 2.2f * fade);
            Tint(_headGlow, _palette.Trail, 1.3f * fade);
        }

        private static void Tint(SpriteRenderer sr, Color hue, float gain)
        {
            if (sr == null) return;
            sr.color = new Color(hue.r * gain, hue.g * gain, hue.b * gain, sr.color.a);
        }

        /// <summary>
        /// The head is gone the instant the shell opens. The trail is DETACHED rather than
        /// destroyed, so the sparks already in the air fade where they were emitted instead of
        /// being deleted mid-flight — the same reason <c>ParticleProjectileVisual</c> detaches
        /// before it stops.
        /// </summary>
        private void HideShell()
        {
            if (_head != null) _head.enabled = false;
            if (_headGlow != null) _headGlow.enabled = false;

            if (_trail != null)
            {
                _trail.transform.SetParent(null, worldPositionStays: true);
                var emission = _trail.emission;
                emission.enabled = false;
                _trail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(_trail.gameObject, 1.2f);
                _trail = null;
            }
        }

        private static Color Warm(Color c, float gain)
            => new Color(c.r * gain, c.g * gain, c.b * gain, 1f);
    }
}
