using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The wave front, the layers that ride it, and the event layer it trips.
    ///
    /// <para>THE SEQUENCING IS THE WHOLE DESIGN. An expanding circle is read once and then
    /// filed as texture; what resets attention is something that APPEARS and is gone. Each
    /// spike, thorn and fork is seeded at its own radius and fires the frame the front passes
    /// it, so fourteen of them erupt in sequence for free — the same duty-cycle reasoning
    /// <c>VortexFunnelFX</c> records for its discharges, where a continuous version measured
    /// 78 % of frames lit and read as a lamp with a flicker rather than as lightning.</para>
    /// </summary>
    internal sealed partial class AreaBurstFX
    {
        /// <summary>How far behind the front the swept haze trails, seconds.</summary>
        private const float HAZE_LAG = 0.06f;

        /// <summary>Seconds the front takes to dissolve once it reaches the rim.</summary>
        private const float ARRIVE_FADE = 0.22f;

        // Additive alphas. Summed at the centre these stay well under the ~3 at which an
        // additive stack goes flat white and a coloured spell stops being distinguishable
        // from any other — the arithmetic VortexFunnelFX's band count records.
        private const float WAVE_ALPHA = 0.85f;
        private const float RING_ALPHA = 0.50f;
        private const float HAZE_ALPHA = 0.34f;
        private const float CORE_ALPHA = 0.60f;

        private void Update()
        {
            _age += Time.deltaTime;

            float wave = WaveFraction01(_age);
            // Squared, so the rig holds its brightness through the sweep and then goes rather
            // than dimming evenly across a life that is mostly aftermath.
            float life01 = _profile.Life > 0f ? Mathf.Clamp01(_age / _profile.Life) : 1f;
            float fade = (1f - life01) * (1f - life01);

            DriveWave(wave, fade);
            DriveGroundRing(fade);
            DriveHaze(fade);
            DriveCore();
            DriveCracks(fade);
            FireDueEvents();
            DriveLight();

            if (_age >= _profile.Life) Destroy(gameObject);
        }

        /// <summary>
        /// A wave DECELERATES as it spreads — the same energy over a longer circumference —
        /// so the front eases out rather than travelling at a constant rate, which reads as a
        /// circle being scaled by an animation curve.
        /// </summary>
        private float WaveFraction01(float age)
        {
            if (_profile.WaveSeconds <= 0f) return 1f;
            float u = Mathf.Clamp01(age / _profile.WaveSeconds);
            return Mathf.Sin(u * Mathf.PI * 0.5f);
        }

        private void DriveWave(float wave, float fade)
        {
            if (_wave == null) return;

            SetGroundSize(_wave.transform, _profile.Radius * wave / RING_BAND);

            // Dissolves ON the rim rather than at the end of the rig's life: the front has
            // arrived, and a ring that lingers there turns the travelling wave into a second
            // static circle competing with the one that means something.
            float arrive = Mathf.Clamp01((_age - _profile.WaveSeconds) / ARRIVE_FADE);
            _wave.color = Additive(_profile.Palette.hotCore, WAVE_ALPHA * (1f - arrive) * fade);
        }

        /// <summary>
        /// The pinned boundary (Law L5). It pulses in BRIGHTNESS only — a circle that breathes
        /// in SIZE is a promise that moves, and this one is the exact circle
        /// <c>Physics2D.OverlapCircleAll</c> queried.
        /// </summary>
        private void DriveGroundRing(float fade)
        {
            if (_groundRing == null) return;

            float rise = _profile.WaveSeconds > 0f
                ? Mathf.Clamp01(_age / (_profile.WaveSeconds * 0.5f)) : 1f;
            float pulse = 0.74f + 0.26f * Mathf.Sin((_age + _seed) * 9f);
            _groundRing.color = Additive(_profile.Palette.core, RING_ALPHA * rise * pulse * fade);
        }

        /// <summary>
        /// The swept ground behind the front. It expands with the wave and empties as the wave
        /// leaves, so the middle is the first thing to go dark — which is the difference
        /// between a wave passing over the floor and a disc sitting on it. (The middle empties
        /// because the whole disc does; <c>Glow</c> is a radial falloff and has no inside to
        /// hollow out, and swapping it for a second ring would just draw the front twice.)
        /// </summary>
        private void DriveHaze(float fade)
        {
            if (_haze == null) return;

            float trail = WaveFraction01(_age - HAZE_LAG);
            SetGroundSize(_haze.transform, _profile.Radius * 2f * trail);
            _haze.color = Additive(_profile.Palette.glow,
                                   HAZE_ALPHA * (1f - trail * 0.7f) * fade);
        }

        /// <summary>
        /// The flash at the origin. It SHRINKS in both silhouettes that have one, because a
        /// core that grows is a second wave and the rig already has the wave — what a detonation
        /// looks like is a point collapsing while everything it threw off expands.
        /// </summary>
        private void DriveCore()
        {
            if (_core == null) return;

            // Three frames for a clap, a third of a second for a detonation. Sound is fast,
            // and the speed is the whole character of the shock silhouette.
            float coreLife = _profile.Silhouette == AreaSilhouette.Shock ? 0.05f : 0.30f;
            float u = Mathf.Clamp01(_age / coreLife);

            _core.transform.localScale = Vector3.one *
                Mathf.Lerp(_profile.Radius * 0.55f, _profile.Radius * 0.12f, u);

            // The clap's core is near-WHITE, not the spell's hue: a crack of sound has no
            // colour of its own and the forks on the rim are what carry the element.
            Color tint = _profile.Silhouette == AreaSilhouette.Shock
                ? Color.Lerp(_profile.Palette.hotCore, Color.white, 0.6f)
                : _profile.Palette.hotCore;

            _core.color = Additive(tint, CORE_ALPHA * (1f - u));
        }

        private void DriveCracks(float fade)
        {
            if (_cracks == null) return;

            // The ground opens BEFORE anything comes out of it. 0.10 s is short enough to read
            // as one gesture and long enough to be a separate beat — and the thorns cannot
            // arrive early even in principle, because the wave has to reach them first.
            float open = Mathf.Clamp01(_age / 0.10f);
            float alpha = 0.85f * open * Mathf.Clamp01(fade * 1.6f);
            for (int i = 0; i < _cracks.Length; i++)
            {
                if (_cracks[i] == null) continue;
                _cracks[i].color = new Color(_crackColor.r, _crackColor.g, _crackColor.b, alpha);
            }
        }

        private void FireDueEvents()
        {
            if (_eventRadius == null) return;

            float front = WaveRadius;
            for (int i = 0; i < _eventRadius.Length; i++)
            {
                if (_eventFired[i] || _eventRadius[i] > front) continue;
                _eventFired[i] = true;
                FireEvent(i);
            }
        }

        private void FireEvent(int i)
        {
            // Placed on the drawn ELLIPSE, not on a true circle: everything else in the rig
            // lies on the squashed ground plane, and an event on the unsquashed circle would
            // stand a third of the radius too far up the screen.
            Vector3 local = new Vector3(
                Mathf.Cos(_eventAngle[i]) * _eventRadius[i],
                Mathf.Sin(_eventAngle[i]) * _eventRadius[i] * GROUND_SQUASH, 0f);
            Vector3 world = transform.position + local;
            int order = ORDER_EVENT + i;

            switch (_profile.Silhouette)
            {
                case AreaSilhouette.Rime:
                    AreaBurstPieces.IceSpike(world, _profile, order, i);
                    break;
                case AreaSilhouette.Thorns:
                    AreaBurstPieces.Thorn(world, _profile, order,
                                          throwsClod: i < _profile.GritCount);
                    break;
                case AreaSilhouette.Shock:
                    AreaBurstPieces.BoltFork(world, _profile, order, _eventAngle[i]);
                    break;
                default:
                    AreaBurstPieces.Spark(world, _profile, order);
                    break;
            }
        }

        /// <summary>
        /// Law L3's opaque layer, thrown once at t=0 for every silhouette that has one. Thorns
        /// is the exception in both directions: its clods come out of the holes its thorns open
        /// (so they arrive with the thing that threw them), and its OPAQUE layer is the thorns
        /// themselves rather than the debris.
        /// </summary>
        private void ThrowGrit()
        {
            // The exemption is READ here rather than merely documented, so a silhouette that
            // claims to be purely additive cannot quietly acquire an opaque layer later — the
            // flag and the behaviour are the same fact.
            if (!_profile.PurelyAdditive && _profile.GritCount > 0)
            {
                switch (_profile.Silhouette)
                {
                    case AreaSilhouette.Rime:
                        AreaBurstPieces.IceChips(transform.position, _profile, ORDER_GRIT);
                        break;
                    // Thorns is absent on purpose: its chips come out of the holes its own
                    // thorns open, so they arrive with the thing that threw them instead of
                    // all at once from a centre nothing erupted at.
                    case AreaSilhouette.Shock:
                    case AreaSilhouette.Bloom:
                        AreaBurstPieces.Grit(transform.position, _profile, ORDER_GRIT);
                        break;
                }
            }

            // Additive dust rides on top of whichever opaque layer exists, and for Radiance it
            // is the only scattered layer there is.
            switch (_profile.Silhouette)
            {
                case AreaSilhouette.Rime:
                    AreaBurstPieces.CrystalDust(transform.position, _profile, ORDER_GRIT + 1);
                    break;
                case AreaSilhouette.Radiance:
                    AreaBurstPieces.GoldMotes(transform.position, _profile, ORDER_GRIT + 1);
                    break;
            }
        }

        /// <summary>
        /// Fast attack, slow release — the shape of a detonation. A symmetric envelope reads as
        /// a lamp being turned up and down, which is the note <c>SkyFlash</c> records for the
        /// same reason.
        /// </summary>
        private void DriveLight()
        {
            if (_light == null) return;

            float rise = _profile.LightRise > 0f ? Mathf.Clamp01(_age / _profile.LightRise) : 1f;
            float fall = _profile.LightFall > 0f
                ? Mathf.Clamp01((_age - _profile.LightRise) / _profile.LightFall) : 1f;
            float envelope = rise * Mathf.Pow(1f - fall, 1.6f);

            try
            {
                ElementalProjectileVisual.GetLight2DIntensityProp()?
                    .SetValue(_light, _profile.LightIntensity * envelope);
            }
            catch { }
        }

        /// <summary>
        /// Law L2. On an additive material ALPHA is coverage and COLOUR is brightness, so
        /// fierceness is bought by pushing the RGB past 1 — HDR is on and an authored 2.4 reads
        /// back unchanged — while reaching for the alpha instead widens the layer into fog.
        /// </summary>
        private Color Additive(Color c, float alpha)
            => new Color(c.r * _profile.Gain, c.g * _profile.Gain, c.b * _profile.Gain,
                         Mathf.Clamp01(alpha));
    }
}
