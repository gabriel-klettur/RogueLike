using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The two particle layers of a shell: the burning stars, and the embers that fall out of
    /// them. The <c>Light2D</c> pair and the envelopes live in the Update partial.
    /// </summary>
    public partial class FireworkBurstFX
    {
        private ParticleSystem _stars;
        private ParticleSystem _embers;

        /// <summary>
        /// Stars are emitted with an explicit per-particle velocity, so the shell's SHAPE is
        /// decided here rather than by a shape module. That is the point of the rig: a sphere
        /// emitter would give a uniformly random cloud, and a chrysanthemum is a sphere whose
        /// stars are evenly spread over directions and unevenly spread over SPEED.
        /// </summary>
        private void BuildStars()
        {
            _stars = MakeSystem("Stars", ElementalSprites.SparkleStar, additive: true,
                                ORDER_STAR, STAR_LIFETIME, STARS + 8);

            var main = _stars.main;
            // The droop. A shell that expands and simply fades is an explosion; what makes it
            // a firework is that the stars run out of speed and fall.
            main.gravityModifier = 0.55f;

            // Air drag, so the sphere decelerates into its final radius instead of coasting
            // out of frame. Dampen is applied above the limit, which is set low enough that
            // every star is being slowed from the first frame.
            var limit = _stars.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.separateAxes = false;
            limit.limit = new ParticleSystem.MinMaxCurve(0.35f);
            limit.dampen = 0.055f;

            // Ribbons. A star with no trail is a dot moving; the trail is what draws the
            // radial lines the shape is actually made of.
            var trails = _stars.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.ratio = 1f;
            trails.lifetime = new ParticleSystem.MinMaxCurve(0.16f);
            trails.minVertexDistance = 0.05f;
            trails.inheritParticleColor = true;
            trails.dieWithParticles = false;
            trails.sizeAffectsWidth = true;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, FadeCurve());

            var renderer = _stars.GetComponent<ParticleSystemRenderer>();
            renderer.trailMaterial = renderer.sharedMaterial;

            FadeOverLife(_stars, holdFor: 0.45f);

            // Playing before emitting is mandatory: a system that has never played swallows
            // Emit outright, which is one of the two ways a probe measures nothing at all.
            _stars.Play();
            EmitShell();
        }

        private void EmitShell()
        {
            // The speed the sphere would need to reach its radius under drag alone. Derived so
            // that a designer moving the radius moves the shell rather than only the ring.
            float baseSpeed = _radius / (STAR_LIFETIME * 0.32f);

            var p = new ParticleSystem.EmitParams
            {
                applyShapeToPosition = false,
            };

            for (int i = 0; i < STARS; i++)
            {
                // Evenly spread over directions, jittered. A pure ring of equal angles reads
                // as a gear; pure randomness leaves visible gaps in a shell this size.
                float angle = (i / (float)STARS) * Mathf.PI * 2f + Random.Range(-0.06f, 0.06f);

                // Speed varies per star so the shell has THICKNESS. All-equal speeds draw a
                // perfect circle, which is the one thing a real burst never is.
                float speed = baseSpeed * Random.Range(0.72f, 1.12f);

                p.position = Vector3.zero;
                p.velocity = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * speed;
                p.startLifetime = STAR_LIFETIME * Random.Range(0.78f, 1f);
                p.startSize = Random.Range(0.22f, 0.40f);
                p.startColor = StarColour();
                p.rotation = Random.Range(0f, 360f);
                p.angularVelocity = Random.Range(-220f, 220f);

                _stars.Emit(p, 1);
            }
        }

        /// <summary>
        /// One star's colour. The alpha carries the per-count division, so the SUM the shell
        /// adds to the frame is independent of <see cref="STARS"/>.
        /// </summary>
        private Color StarColour()
        {
            Color c = _palette.RandomStar();
            return new Color(c.r, c.g, c.b, StarAlphaFor(STARS));
        }

        /// <summary>
        /// The embers. The ONE non-additive layer in the rig, and the only one that says
        /// something material is up there rather than only light. Folding it into the additive
        /// stack would not make it dimmer, it would make it VANISH — a dark chip added to a
        /// bright pixel changes almost nothing.
        /// </summary>
        private void BuildEmbers()
        {
            _embers = MakeSystem("Embers", ElementalSprites.Sparkle, additive: false,
                                 ORDER_EMBER, EMBER_LIFETIME, EMBERS + 4);

            var main = _embers.main;
            main.gravityModifier = 1.25f;   // heavier than the stars: these are falling, not burning

            var limit = _embers.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.separateAxes = false;
            limit.limit = new ParticleSystem.MinMaxCurve(1.6f);
            limit.dampen = 0.10f;

            FadeOverLife(_embers, holdFor: 0.25f);
            _embers.Play();

            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < EMBERS; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float speed = (_radius / EMBER_LIFETIME) * Random.Range(0.25f, 0.75f);

                p.position = Random.insideUnitSphere * (_radius * 0.25f);
                p.velocity = new Vector3(Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed * 0.6f, 0f);
                p.startLifetime = EMBER_LIFETIME * Random.Range(0.60f, 1f);
                p.startSize = Random.Range(0.10f, 0.20f);

                // Deliberately dark and warm — a cooling cinder, not a light source.
                Color star = _palette.RandomStar();
                p.startColor = new Color(star.r * 0.55f, star.g * 0.34f, star.b * 0.22f, 0.85f);

                _embers.Emit(p, 1);
            }
        }

        private ParticleSystem MakeSystem(string name, Sprite sprite, bool additive,
                                          int order, float lifetime, int capacity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            var ps = go.AddComponent<ParticleSystem>();

            // AddComponent starts a ParticleSystem immediately (playOnAwake defaults true) and
            // main.duration cannot be written while one is playing — it logs and keeps the old
            // value. Stop first, configure, then Play, exactly as every emitter builder does.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = Mathf.Max(0.05f, lifetime);
            main.startLifetime = lifetime;
            main.startSpeed = 0f;               // every particle carries its own velocity
            main.startSize = 0.2f;
            main.maxParticles = capacity;
            main.stopAction = ParticleSystemStopAction.None;
            // LOCAL on purpose: this rig is stationary by construction — the shell opens at a
            // fixed point and nothing carries it. A rig that MOVES needs World, which is the
            // rule the projectile trail lives by.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = ps.emission;
            emission.enabled = false;           // emission is explicit, through Emit

            var shape = ps.shape;
            shape.enabled = false;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            // A material handed to a ParticleSystemRenderer must carry its own texture — a
            // SpriteRenderer supplies one and a particle renderer does not, so an untextured
            // shared material draws hard white squares.
            renderer.sharedMaterial = ParticleMaterialCache.Get(sprite.texture, additive);
            renderer.sortingLayerName = SortingConfig.LAYER_OVERHEAD;
            renderer.sortingOrder = order;

            return ps;
        }

        /// <summary>
        /// Alpha over life: hold, then fall away. Applied through colorOverLifetime so it
        /// MULTIPLIES the per-particle start colour instead of replacing it — the stars would
        /// otherwise all end up the same colour.
        /// </summary>
        private static void FadeOverLife(ParticleSystem ps, float holdFor)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, Mathf.Clamp01(holdFor)),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        private static AnimationCurve FadeCurve()
            => new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
    }
}
