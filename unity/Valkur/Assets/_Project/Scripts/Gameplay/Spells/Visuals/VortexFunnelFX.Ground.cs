using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Everything the vortex does to the FLOOR: the touchdown, the suction streaks, and the
    /// debris it tears loose.
    ///
    /// <para>These three exist because the funnel alone is drawn OVER the world rather than
    /// acting on it. A column of light with nothing happening at its base reads as a decal on
    /// the lens — the same failure the single-system weather effects had, and the same one the
    /// old flat-disc shield had. The floor is also where the force actually acts, so it is the
    /// honest place to state direction and reach.</para>
    ///
    /// <para>THE DEBRIS IS THE ONLY OPAQUE LAYER, deliberately. Everything else here is
    /// additive light; chips of ground are MATTER, and matter is the one thing that says the
    /// world is being affected rather than merely lit. <c>KiAuraFX</c> makes the same choice
    /// for the same reason.</para>
    ///
    /// <para>The streaks live under a single squashed plane rather than being squashed one by
    /// one. A streak points along a radius, so squashing it individually would foreshorten its
    /// LENGTH without turning its direction — it would slide across the floor instead of lying
    /// on it. One parent carrying the squash and the rotation on the children is the same split
    /// the bands use.</para>
    /// </summary>
    internal sealed partial class VortexFunnelFX
    {
        private const int STREAK_COUNT = 16;
        private const int DEBRIS_COUNT = 18;

        /// <summary>One debris chip in this many is a HEAVY one: bigger, slower, tumbling less.
        /// A field of identically sized scraps reads as static, however fast it moves.</summary>
        private const int HEAVY_DEBRIS_EVERY = 5;

        private const float SHOCKWAVE_SECONDS = 0.55f;

        /// <summary>How far past the force radius the touchdown ring runs before it dies.</summary>
        private const float SHOCKWAVE_OVERSHOOT = 1.85f;

        /// <summary>
        /// The outer end of the ground layers, as a multiple of the force radius — where a pull
        /// reaches out FROM and where a push throws out TO.
        ///
        /// <para>ONE constant for both directions, because the two runs have to be each other
        /// backwards. They were not: a push threw its streaks to 1.39x the ring and its debris
        /// to 1.25x while a pull only worked between the rim and the neck, so the same piece
        /// count covered 46 % more ground. Measured at radius 3.7 the spans were 3.10 against
        /// 4.54 — push was sparser, moved faster in world units for the same cycle rate, and
        /// spilled a third of its ground layer outside the circle the ring exists to state.
        /// It read as the worse-looking of the two and the cause was not the colour.</para>
        ///
        /// <para>Slightly over 1 on purpose: a pull that starts exactly at the rim never reaches
        /// past what it claims, and a push that stops there has nothing escape it.</para>
        /// </summary>
        private const float GROUND_REACH = 1.08f;

        private const float STREAK_TRAVEL_SPEED = 1.35f;
        private const float DEBRIS_CYCLE_SPEED = 0.62f;

        // Sorting. The ground pieces share the FloorDecals layer with the ring so they are
        // painted ON the floor; the debris is in the air and belongs with the funnel.
        private const int ORDER_GROUND_STREAK = 42;
        private const int ORDER_SHOCKWAVE = 43;
        private const int ORDER_DEBRIS = ORDER_DUST + 1;

        private SpriteRenderer _shockwave;
        private float _shockAge = float.MaxValue;   // starts spent, so nothing draws until fired

        private Transform _streakPlane;
        private Transform[] _streaks;
        private SpriteRenderer[] _streakRenderers;
        private float[] _streakAngle;
        private float[] _streakPhase;

        private Transform[] _debris;
        private SpriteRenderer[] _debrisRenderers;
        private float[] _debrisAngle;
        private float[] _debrisPhase;
        private float[] _debrisSize;
        private float[] _debrisSpin;
        private bool[] _debrisHeavy;

        // ── build ────────────────────────────────────────────────────────────────────

        private void BuildGroundLayers()
        {
            _shockwave = MakeSprite("Shockwave", ElementalSprites.Ring,
                WithAlpha(_palette.Core, 0f), ORDER_SHOCKWAVE, SortingConfig.LAYER_FLOOR_DECALS);

            BuildStreaks();
            BuildDebris();
        }

        private void BuildStreaks()
        {
            var plane = new GameObject("GroundPlane").transform;
            plane.SetParent(_root, false);
            plane.localScale = new Vector3(1f, GROUND_SQUASH, 1f);
            _streakPlane = plane;

            _streaks = new Transform[STREAK_COUNT];
            _streakRenderers = new SpriteRenderer[STREAK_COUNT];
            _streakAngle = new float[STREAK_COUNT];
            _streakPhase = new float[STREAK_COUNT];

            for (int i = 0; i < STREAK_COUNT; i++)
            {
                _streakAngle[i] = i / (float)STREAK_COUNT * Mathf.PI * 2f + Random.Range(-0.08f, 0.08f);
                _streakPhase[i] = Random.value;

                var go = new GameObject("Streak" + i.ToString("00"));
                go.transform.SetParent(plane, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = KiSprites.Streak;
                sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
                sr.sortingLayerName = SortingConfig.LAYER_FLOOR_DECALS;
                sr.sortingOrder = ORDER_GROUND_STREAK;
                sr.color = WithAlpha(_palette.Mid, 0f);

                _streaks[i] = go.transform;
                _streakRenderers[i] = sr;
            }
        }

        private void BuildDebris()
        {
            _debris = new Transform[DEBRIS_COUNT];
            _debrisRenderers = new SpriteRenderer[DEBRIS_COUNT];
            _debrisAngle = new float[DEBRIS_COUNT];
            _debrisPhase = new float[DEBRIS_COUNT];
            _debrisSize = new float[DEBRIS_COUNT];
            _debrisSpin = new float[DEBRIS_COUNT];
            _debrisHeavy = new bool[DEBRIS_COUNT];

            // Ground the player can believe was torn up: the edge colour taken well down
            // towards black, because this layer is lit by nothing and must not glow.
            Color rubble = Color.Lerp(_palette.Edge, Color.black, 0.45f);

            for (int i = 0; i < DEBRIS_COUNT; i++)
            {
                _debrisHeavy[i] = i % HEAVY_DEBRIS_EVERY == 0;
                _debrisAngle[i] = Random.Range(0f, Mathf.PI * 2f);
                _debrisPhase[i] = Random.value;
                _debrisSize[i] = _radius * (_debrisHeavy[i]
                    ? Random.Range(0.075f, 0.105f)
                    : Random.Range(0.030f, 0.055f));
                _debrisSpin[i] = Random.Range(90f, 320f) * (Random.value < 0.5f ? -1f : 1f);

                var go = new GameObject("Debris" + i.ToString("00"));
                go.transform.SetParent(_root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = KiSprites.Pebble;
                // NOT additive. On the additive material a dark chip adds nothing and simply
                // is not there; opacity is the whole point of this layer.
                sr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
                sr.sortingLayerName = SortingConfig.LAYER_VFX;
                sr.sortingOrder = ORDER_DEBRIS;
                sr.color = WithAlpha(rubble, 0f);

                _debris[i] = go.transform;
                _debrisRenderers[i] = sr;
            }
        }

        // ── tick ─────────────────────────────────────────────────────────────────────

        /// <summary>Restart the touchdown ring. Fired once when the vortex bites, and again as
        /// it lets go — an effect that ends by dimming looks switched off.</summary>
        private void FireShockwave() => _shockAge = 0f;

        private void TickShockwave(float deltaTime, float fade)
        {
            if (_shockwave == null) return;
            if (_shockAge > SHOCKWAVE_SECONDS) { SetAlpha(_shockwave, 0f); return; }

            _shockAge += deltaTime;
            float t = Mathf.Clamp01(_shockAge / SHOCKWAVE_SECONDS);

            // Decelerating, because a ring that expands linearly reads as a UI animation.
            float eased = EaseOutCubic(t);
            float span = Mathf.Lerp(_radius * NECK_FRAC, _radius * SHOCKWAVE_OVERSHOOT, eased) / RING_BAND;
            _shockwave.transform.localScale = new Vector3(span, span * GROUND_SQUASH, 1f);
            SetAlpha(_shockwave, Mathf.Pow(1f - t, 1.6f) * 0.85f * Mathf.Max(fade, 0.35f));
        }

        private void TickStreaks(float deltaTime, float grown, float fade, float dissipate)
        {
            if (_streaks == null) return;

            // Pull drags its streaks toward the neck, push drives them at the rim. This is the
            // clearest statement of direction anywhere in the effect, because it happens on the
            // plane the force is actually applied on.
            bool inward = _spinSign > 0f;
            float length = _radius * 0.42f;

            for (int i = 0; i < _streaks.Length; i++)
            {
                _streakPhase[i] = Mathf.Repeat(_streakPhase[i] + STREAK_TRAVEL_SPEED * deltaTime, 1f);
                _streakAngle[i] += DUST_SWEEP * 0.35f * _spinSign * deltaTime;

                float p = _streakPhase[i];
                float outer = _radius * GROUND_REACH;
                float neck = _radius * NECK_FRAC;
                float distance = inward ? Mathf.Lerp(outer, neck, p) : Mathf.Lerp(neck, outer, p);

                float angle = _streakAngle[i];
                _streaks[i].localPosition = new Vector3(Mathf.Cos(angle) * distance,
                                                        Mathf.Sin(angle) * distance, 0f);
                // The sprite is drawn tall, so its own +Y has to be turned onto the radius.
                _streaks[i].localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg - 90f);
                _streaks[i].localScale = new Vector3(length * 0.16f, length, 1f);

                // Faded at both ends of the run: a streak that pops into existence at a fixed
                // radius draws a second circle nobody asked for.
                float ends = Mathf.Sin(p * Mathf.PI);
                SetAlpha(_streakRenderers[i], ends * 0.42f * grown * fade * (1f - dissipate));
            }
        }

        private void TickDebris(float deltaTime, float grown, float fade, float dissipate)
        {
            if (_debris == null) return;

            bool inward = _spinSign > 0f;

            for (int i = 0; i < _debris.Length; i++)
            {
                // Heavy chips are slower: mass is read as reluctance, not as size alone.
                float speed = DEBRIS_CYCLE_SPEED * (_debrisHeavy[i] ? 0.55f : 1f);
                _debrisPhase[i] = Mathf.Repeat(_debrisPhase[i] + speed * deltaTime, 1f);
                _debrisAngle[i] += DUST_SWEEP * 0.8f * _spinSign * deltaTime;

                float p = _debrisPhase[i];
                // Torn off the floor at the rim and carried in and UP, or blasted out and up —
                // over the SAME distance either way, so the two read as one motion reversed
                // rather than as two effects with different reach.
                float outer = _radius * GROUND_REACH;
                float neck = _radius * NECK_FRAC;
                float distance = inward ? Mathf.Lerp(outer, neck, p) : Mathf.Lerp(neck, outer, p);

                // Stays low. This is ground being dragged along it, not confetti — a chip that
                // climbs the whole funnel is indistinguishable from the dust already up there.
                float height = Height * 0.30f * Mathf.Sin(p * Mathf.PI) * grown;

                float angle = _debrisAngle[i];
                float depth = Mathf.Sin(angle);

                // Lagged, not rigid: ground that has been thrown into the air is no longer
                // attached to the thing that threw it, so a travelling funnel leaves a plume.
                _debris[i].localPosition = new Vector3(
                    Mathf.Cos(angle) * distance,
                    height + depth * distance * GROUND_SQUASH, 0f) + DebrisLag();
                _debris[i].localRotation = Quaternion.Euler(0f, 0f, _debrisSpin[i] * _age);

                float near01 = 0.5f - depth * 0.5f;
                _debris[i].localScale = Vector3.one * (_debrisSize[i] * Mathf.Lerp(0.75f, 1.25f, near01));
                _debrisRenderers[i].sortingOrder = depth < 0f ? ORDER_DEBRIS : ORDER_BAND - 3;

                float ends = Mathf.Sin(Mathf.Clamp01(p) * Mathf.PI);
                SetAlpha(_debrisRenderers[i], ends * 0.95f * grown * fade * (1f - dissipate));
            }
        }
    }
}
