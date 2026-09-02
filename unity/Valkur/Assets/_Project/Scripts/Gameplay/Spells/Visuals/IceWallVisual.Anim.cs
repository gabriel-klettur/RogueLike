using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The timeline: crystals erupt from the middle outwards, live under a travelling
    /// highlight, and either melt back into the ground or are shattered.
    /// </summary>
    internal sealed partial class IceWallVisual
    {
        /// <summary>How long one crystal takes to reach full height.</summary>
        private const float RiseSeconds = 0.30f;

        /// <summary>Seconds for the highlight to travel the length of the wall once.</summary>
        private const float SweepPeriod = 2.6f;

        private bool _melting;
        private float _meltDuration;
        private float _meltTime;

        /// <summary>True once a melt has run its course and the object can be destroyed.</summary>
        public bool MeltComplete => _melting && _meltTime >= _meltDuration;

        /// <summary>How long the last crystal keeps rising. Callers size the cast window on it.</summary>
        public float EruptionSeconds => 0.34f + RiseSeconds;

        public void Tick(float deltaTime)
        {
            _age += deltaTime;
            if (_melting) _meltTime += deltaTime;

            float meltProgress = _melting ? Mathf.Clamp01(_meltTime / Mathf.Max(0.01f, _meltDuration)) : 0f;
            float globalAlpha = 1f - meltProgress;

            // The highlight starts off one end and runs past the other, so it enters and
            // leaves rather than appearing in the middle of the wall.
            float sweep = Mathf.Repeat(_age / SweepPeriod, 1f) * 1.4f - 0.2f;

            float riseSum = 0f;
            float flashSum = 0f;

            for (int i = 0; i < _shards.Count; i++)
            {
                var shard = _shards[i];
                if (shard.Broken || shard.Root == null) continue;

                AdvanceShard(shard, deltaTime, sweep, globalAlpha, meltProgress);
                riseSum += shard.Rise;
                flashSum += shard.Flash;
            }

            float riseAverage = _shards.Count > 0 ? Mathf.Clamp01(riseSum / _shards.Count) : 0f;
            AdvanceAmbient(riseAverage, flashSum, globalAlpha, meltProgress);
        }

        private void AdvanceShard(Shard shard, float deltaTime, float sweep,
            float globalAlpha, float meltProgress)
        {
            float local = _age - shard.BirthDelay;
            float previousRise = shard.Rise;
            shard.Rise = local <= 0f ? 0f : Mathf.Clamp01(local / RiseSeconds);
            shard.Flash = Mathf.Max(0f, shard.Flash - deltaTime * 3.2f);

            // A settled crystal's geometry never changes, and a wall is 18 of them with four
            // renderers each. Writing 72 transforms a frame to re-assert the same numbers is
            // the shape of cost YSortEntity's own no-move guard exists to avoid.
            bool geometryMoves = shard.Rise < 1f || previousRise < 1f || meltProgress > 0f;
            if (geometryMoves)
            {
                // easeOutBack: the crystal overshoots its height and settles. A linear rise
                // reads as a sprite being scaled; the overshoot reads as something being
                // forced out of the ground.
                float eased = EaseOutBack(shard.Rise);
                float heightScale = Mathf.Max(0f, eased) * (1f - meltProgress * 0.92f);
                for (int c = 0; c < shard.Root.childCount; c++)
                    IceSprites.ScaleShard(shard.Root.GetChild(c), shard.Width, shard.Height * heightScale);

                // Sinking back into the ground is what makes a melt read as a melt rather
                // than as a fade-out. Only during the melt, and only a fraction of the height.
                if (meltProgress > 0f)
                    shard.Root.localPosition = shard.BaseLocal +
                        new Vector3(0f, -shard.Height * 0.18f * meltProgress, 0f);
            }

            float appear = Mathf.Clamp01(local / 0.12f);
            float visible = appear * globalAlpha;

            float highlight = Gaussian(shard.T - sweep, 0.16f);
            float shimmer = 0.55f + 0.25f * Mathf.Sin(_age * 1.7f + shard.Phase);

            // The opaque layers only move when the wall is being born, damaged or ending.
            if (visible != shard.LastVisible || shard.CrackAlpha != shard.LastCrack)
            {
                SetAlpha(shard.Body, shard.BaseAlpha * visible);
                SetAlpha(shard.Crack, shard.CrackAlpha * visible);
                shard.LastVisible = visible;
                shard.LastCrack = shard.CrackAlpha;
            }

            // The additive layers are the shimmer and the travelling highlight: those DO
            // change every frame, and they are the whole reason the wall looks alive.
            // Measured live, the previous weights summed past 1.0 at the sweep's peak, so the
            // highlight plateaued at full white across several crystals instead of passing
            // over them. These leave headroom: only a hit flash is allowed to clip.
            SetAlpha(shard.Facet, (0.16f + 0.22f * shimmer + 0.58f * highlight + shard.Flash) * visible);
            SetAlpha(shard.Rim, (0.32f + 0.18f * shimmer + 0.42f * highlight + 1.4f * shard.Flash) * visible);
        }

        private void AdvanceAmbient(float riseAverage, float flashSum, float globalAlpha, float meltProgress)
        {
            float pulse = 0.86f + 0.14f * Mathf.Sin(_age * 2.2f);

            if (_rime != null)
            {
                // The frost patch appears BEFORE the crystals — the ground freezes, then it
                // splits. It also outlives them: the last thing to go is the mark on the floor.
                float appear = Mathf.Clamp01(_age / 0.20f);
                float linger = 1f - Mathf.Pow(meltProgress, 2.2f);
                SetAlpha(_rime, 0.58f * appear * linger);
            }

            for (int i = 0; i < _auras.Count; i++)
                SetAlpha(_auras[i], (0.16f + 0.07f * pulse) * riseAverage * globalAlpha);

            SetLightIntensity((_lightBaseIntensity * pulse * riseAverage + 0.7f * flashSum) * globalAlpha);
        }

        private static void SetAlpha(SpriteRenderer renderer, float alpha)
        {
            if (renderer == null) return;
            var color = renderer.color;
            color.a = Mathf.Clamp01(alpha);
            renderer.color = color;
        }

        /// <summary>
        /// Fade out over <paramref name="seconds"/> and take the crystals down with it.
        /// Emission stops immediately while the particles already in the air finish their
        /// lives — destroying the systems outright kills them on a frame boundary.
        /// </summary>
        public void BeginMelt(float seconds)
        {
            if (_melting) return;
            _melting = true;
            _meltDuration = Mathf.Max(0.05f, seconds);
            _meltTime = 0f;

            if (_mist != null) _mist.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (_sparkle != null) _sparkle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float t = x - 1f;
            return 1f + c3 * t * t * t + c1 * t * t;
        }

        private static float Gaussian(float d, float sigma)
        {
            float k = d / sigma;
            return Mathf.Exp(-k * k);
        }
    }
}
