using System.Collections.Generic;
using UnityEngine;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The map's particles: short-lived glows, sparks and rings drawn as additive quads.
    ///
    /// <para><b>An event, never a state.</b> The rule is the one <c>FacingIndicator</c> records
    /// for the aim marker: a particle may confirm something that just HAPPENED — ground
    /// revealed, a quest mark appearing, a blow landing, a pin reached, a new zone — and it
    /// may not become a readout the player has to watch. Nothing here loops except the night
    /// motes, and those are ambience with no meaning to decode.</para>
    ///
    /// <para><b>Why not a ParticleSystem.</b> The minimap is a Screen Space Overlay canvas. A
    /// <c>ParticleSystem</c> is a world renderer: it does not sort against canvas graphics and
    /// would need a second canvas and camera for a 170-pixel disc. A quad list on a
    /// <see cref="MinimapQuadGraphic"/> batches with the map, clips to the disc by its own
    /// arithmetic, and costs nothing while nothing is alive.</para>
    ///
    /// <para><b>World-anchored or map-anchored.</b> A reveal mote belongs to a place: it has to
    /// stay over that patch of ground while the map scrolls under the player, so it stores a
    /// world anchor and a local offset. A rim spark belongs to the dial and stores only the
    /// offset.</para>
    /// </summary>
    public sealed class MinimapFx
    {
        public const int Capacity = 160;

        private struct Particle
        {
            public bool Anchored;
            public Vector2 Anchor;
            public Vector2 Offset;
            public Vector2 Velocity;
            public float Drag;
            public float Age, Life;
            public float Size0, Size1;
            public float Spin, Rotation;
            public Color Color;
            public MinimapIcon Icon;
            public bool Over;
            public bool Ambient;
            /// <summary>Clipped to the disc although map-anchored (weather falls INSIDE the dial).</summary>
            public bool Clip;
            /// <summary>Length multiplier along the particle's rotation (rain streaks).</summary>
            public float Stretch;
        }

        private readonly List<Particle> _particles = new List<Particle>(Capacity);

        /// <summary>Particles alive right now.</summary>
        public int Count => _particles.Count;

        /// <summary>Remove everything (a view closed, a world swapped).</summary>
        public void Clear() => _particles.Clear();

        // ── Emitters ───────────────────────────────────────────────────────

        /// <summary>A gold mote rising off freshly revealed ground.</summary>
        public void RevealMote(Vector2 world, Color color)
        {
            Emit(new Particle
            {
                Anchored = true, Anchor = world,
                Offset = Random.insideUnitCircle * 1.5f,
                Velocity = new Vector2(Random.Range(-4f, 4f), Random.Range(6f, 14f)),
                Drag = 1.2f,
                Life = Random.Range(0.7f, 1.2f),
                Size0 = Random.Range(3.5f, 6f), Size1 = 1.5f,
                Color = color, Icon = MinimapIcon.Spark, Over = true,
                Spin = Random.Range(-90f, 90f),
            });
        }

        /// <summary>A ring opening from a place: a new mark, a reached pin, a completed errand.</summary>
        public void Pulse(Vector2 world, Color color, float sizePx, float life = 0.6f)
        {
            Emit(new Particle
            {
                Anchored = true, Anchor = world,
                Life = life, Size0 = sizePx * 0.4f, Size1 = sizePx * 2.4f,
                Color = color, Icon = MinimapIcon.SoftRing, Over = false,
            });
            Emit(new Particle
            {
                Anchored = true, Anchor = world,
                Life = life * 0.6f, Size0 = sizePx * 1.8f, Size1 = sizePx * 0.8f,
                Color = new Color(color.r, color.g, color.b, color.a * 0.8f), Icon = MinimapIcon.Glow, Over = false,
            });
        }

        /// <summary>A starburst of sparks from a place.</summary>
        public void Burst(Vector2 world, Color color, int count, float speed)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (i + Random.value * 0.6f) / count * Mathf.PI * 2f;
                Emit(new Particle
                {
                    Anchored = true, Anchor = world,
                    Velocity = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed * Random.Range(0.7f, 1.2f),
                    Drag = 3f,
                    Life = Random.Range(0.45f, 0.75f),
                    Size0 = Random.Range(5f, 8f), Size1 = 1f,
                    Color = color, Icon = MinimapIcon.Spark, Over = true,
                    Spin = Random.Range(-180f, 180f),
                });
            }
        }

        /// <summary>
        /// Sparks from the rim at a bearing, falling inward: where a blow came from. The
        /// DIRECTION is the information; the colour only confirms it was a hit.
        /// </summary>
        public void RimStrike(float bearingDeg, float rimRadius, Color color)
        {
            float a = bearingDeg * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var tangent = new Vector2(-dir.y, dir.x);
            for (int i = 0; i < 7; i++)
            {
                float spread = Random.Range(-1f, 1f);
                Emit(new Particle
                {
                    Offset = dir * rimRadius + tangent * spread * 10f,
                    Velocity = -dir * Random.Range(40f, 85f) + tangent * spread * 18f,
                    Drag = 4f,
                    Life = Random.Range(0.35f, 0.6f),
                    Size0 = Random.Range(6f, 10f), Size1 = 1.5f,
                    Color = color, Icon = MinimapIcon.Spark, Over = true,
                    Spin = Random.Range(-200f, 200f),
                });
            }
            Emit(new Particle
            {
                Offset = dir * (rimRadius - 6f),
                Life = 0.45f, Size0 = 34f, Size1 = 22f,
                Color = new Color(color.r, color.g, color.b, 0.55f), Icon = MinimapIcon.Glow, Over = false,
            });
        }

        /// <summary>Sparks travelling around the rim — a new zone was entered.</summary>
        public void RimSweep(float rimRadius, Color color)
        {
            for (int i = 0; i < 18; i++)
            {
                float a = (90f - i * 20f) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Emit(new Particle
                {
                    Offset = dir * rimRadius,
                    Velocity = new Vector2(dir.y, -dir.x) * 30f,
                    Drag = 1f,
                    Age = -i * 0.035f,   // staggered: the sweep travels clockwise from north
                    Life = 0.55f,
                    Size0 = 9f, Size1 = 2f,
                    Color = color, Icon = MinimapIcon.Spark, Over = true,
                    Spin = 120f,
                });
            }
        }

        /// <summary>One slow ambient mote somewhere on the map (night).</summary>
        public void AmbientMote(float radius, Color color)
        {
            Emit(new Particle
            {
                Offset = Random.insideUnitCircle * radius * 0.9f,
                Velocity = Random.insideUnitCircle * 5f + new Vector2(0f, 2.5f),
                Life = Random.Range(3f, 5.5f),
                Size0 = Random.Range(3f, 5f), Size1 = Random.Range(2f, 4f),
                Color = color, Icon = MinimapIcon.Glow, Over = true, Ambient = true,
            });
        }

        private float _rainCredit, _snowCredit;

        /// <summary>
        /// Weather over the dial: rain as short diagonal streaks, snow as drifting flakes, at the
        /// density the world's own weather is actually rendering. A map that stays sunny in a
        /// storm reads as a picture of the world rather than a window onto it.
        /// </summary>
        public void Weather(float rain, float snow, float wind, float radius, float dt)
        {
            _rainCredit += rain * 55f * dt;
            while (_rainCredit >= 1f)
            {
                _rainCredit -= 1f;
                var start = new Vector2(Random.Range(-radius, radius), radius * Random.Range(0.6f, 1.05f));
                float slant = -12f - wind * 26f;
                Emit(new Particle
                {
                    Offset = start,
                    Velocity = new Vector2(slant, -150f),
                    Life = Random.Range(0.55f, 0.9f),
                    Size0 = 1f, Size1 = 1f,
                    Stretch = 7f,
                    Rotation = Mathf.Atan2(-150f, slant) * Mathf.Rad2Deg,
                    Color = new Color(0.72f, 0.82f, 1f, 0.34f * Mathf.Clamp01(rain + 0.3f)),
                    Icon = MinimapIcon.Bar, Over = true, Clip = true,
                });
            }

            _snowCredit += snow * 14f * dt;
            while (_snowCredit >= 1f)
            {
                _snowCredit -= 1f;
                Emit(new Particle
                {
                    Offset = new Vector2(Random.Range(-radius, radius), radius * Random.Range(0.5f, 1.0f)),
                    Velocity = new Vector2(Random.Range(-6f, 6f) + wind * 14f, Random.Range(-16f, -24f)),
                    Life = Random.Range(3.5f, 6f),
                    Size0 = Random.Range(2.5f, 4f), Size1 = Random.Range(2f, 3.5f),
                    Spin = Random.Range(-40f, 40f),
                    Color = new Color(1f, 1f, 1f, 0.55f),
                    Icon = MinimapIcon.Glow, Over = true, Clip = true,
                });
            }
        }

        /// <summary>Ambient motes alive now, so the caller can keep a steady count.</summary>
        public int AmbientCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _particles.Count; i++)
                    if (_particles[i].Ambient) n++;
                return n;
            }
        }

        private void Emit(Particle p)
        {
            if (_particles.Count >= Capacity) _particles.RemoveAt(0);
            _particles.Add(p);
        }

        // ── Simulation and drawing ─────────────────────────────────────────

        public void Tick(float dt)
        {
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.Age += dt;
                if (p.Age >= p.Life) { _particles.RemoveAt(i); continue; }
                if (p.Age > 0f)
                {
                    p.Offset += p.Velocity * dt;
                    p.Velocity *= Mathf.Max(0f, 1f - p.Drag * dt);
                    p.Rotation += p.Spin * dt;
                }
                _particles[i] = p;
            }
        }

        /// <summary>
        /// Draw every live particle. <paramref name="project"/> turns a world anchor into a
        /// map-local position; <paramref name="clipRadius"/> fades particles near a circular
        /// edge (0 for a rectangular view).
        /// </summary>
        public void Draw(MinimapQuadGraphic under, MinimapQuadGraphic over, System.Func<Vector2, Vector2> project, float clipRadius)
        {
            for (int i = 0; i < _particles.Count; i++)
            {
                var p = _particles[i];
                if (p.Age < 0f) continue;
                float t = Mathf.Clamp01(p.Age / p.Life);
                Vector2 pos = (p.Anchored ? project(p.Anchor) : Vector2.zero) + p.Offset;

                float a = t < 0.15f ? t / 0.15f : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.15f) / 0.85f);
                // Only world-anchored particles are clipped: they belong to the map and must stay
                // inside the disc. Dial particles (rim strikes, the zone sweep) live ON the ring.
                if (clipRadius > 0f && (p.Anchored || p.Clip))
                {
                    float m = pos.magnitude;
                    a *= Mathf.Clamp01((clipRadius - m) / 6f);
                }
                if (a <= 0.003f) continue;

                float ease = 1f - (1f - t) * (1f - t);
                float size = Mathf.Lerp(p.Size0, p.Size1, ease);
                var c = p.Color;
                c.a *= a;
                var dims = p.Stretch > 0f ? new Vector2(size * p.Stretch, size) : new Vector2(size, size);
                (p.Over ? over : under).Add(pos, dims, p.Icon, c, p.Rotation);
            }
        }
    }
}
