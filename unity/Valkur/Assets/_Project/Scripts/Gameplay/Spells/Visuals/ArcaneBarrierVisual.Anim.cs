using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The timeline. Anchors are driven into the floor, the weave knits outward from them and
    /// meets in the middle of each bay, the surface lives under a travelling highlight, and it
    /// either UNRAVELS back into the anchors or is torn apart all at once.
    ///
    /// <para>THE MELT RUNS THE KNIT BACKWARDS. A plain fade is what ice does — it sublimates,
    /// every crystal at once — and it is the wrong idea here: what ends a ward is the weave
    /// failing between its anchors, so the cells that were woven LAST are the ones that let go
    /// FIRST and the posts gutter out on their own at the end. It costs one stored fraction per
    /// panel and it is the difference between a barrier expiring and a barrier being switched
    /// off.</para>
    /// </summary>
    internal sealed partial class ArcaneBarrierVisual
    {
        /// <summary>How long one anchor takes to reach full height.</summary>
        private const float PostRiseSeconds = 0.16f;

        /// <summary>How long one cell takes to weave in once its delay has elapsed.</summary>
        private const float KnitSeconds = 0.22f;

        /// <summary>Seconds for the highlight to travel the barrier once.</summary>
        private const float SweepPeriod = 3.1f;

        /// <summary>
        /// How much of the melt is spent unravelling the bays before the anchors start to go.
        /// At 0 the whole barrier fades together, which is the ice behaviour this rig exists
        /// not to copy.
        /// </summary>
        private const float MeltStagger = 0.45f;

        private bool _melting;
        private bool _shattered;
        private float _meltDuration;
        private float _meltTime;

        /// <summary>True once a melt has run its course and the object can be destroyed.</summary>
        public bool MeltComplete => _melting && _meltTime >= _meltDuration;

        /// <summary>How long the last cell keeps weaving. Callers size the cast window on it.</summary>
        public float EruptionSeconds => 0.50f + KnitSeconds;

        public void Tick(float deltaTime)
        {
            _age += deltaTime;
            if (_melting) _meltTime += deltaTime;

            float meltProgress = _melting ? Mathf.Clamp01(_meltTime / Mathf.Max(0.01f, _meltDuration)) : 0f;
            float globalAlpha = 1f - meltProgress;

            // The highlight starts off one end and runs past the other, so it enters and leaves
            // rather than appearing in the middle of the barrier.
            float sweep = Mathf.Repeat(_age / SweepPeriod, 1f) * 1.4f - 0.2f;

            float riseSum = AdvancePosts(meltProgress, globalAlpha);
            float riseAverage = _posts.Count > 0 ? Mathf.Clamp01(riseSum / _posts.Count) : 0f;

            float knitSum = 0f, flashSum = 0f;
            for (int i = 0; i < _panels.Count; i++)
            {
                var panel = _panels[i];
                if (panel.Broken || panel.Root == null) continue;
                AdvancePanel(panel, deltaTime, sweep, meltProgress, globalAlpha);
                knitSum += panel.Knit;
                flashSum += panel.Flash;
            }

            float knitAverage = _panels.Count > 0 ? Mathf.Clamp01(knitSum / _panels.Count) : 0f;
            AdvanceRunes(deltaTime, knitAverage * globalAlpha);
            AdvanceAmbient(riseAverage, knitAverage, flashSum, globalAlpha, meltProgress);
        }

        private float AdvancePosts(float meltProgress, float globalAlpha)
        {
            // Anchors hold while the bays unravel and only then gutter out. Below MeltStagger
            // they are the last thing standing, which is what makes the melt read as the weave
            // failing rather than as the whole effect dimming.
            float postAlpha = _shattered
                ? Mathf.Pow(globalAlpha, 0.5f)
                : 1f - Mathf.Clamp01((meltProgress - MeltStagger) / (1f - MeltStagger));

            float sum = 0f;
            for (int i = 0; i < _posts.Count; i++)
            {
                var post = _posts[i];
                if (post.Root == null) continue;

                float local = _age - post.Delay;
                post.Rise = local <= 0f ? 0f : Mathf.Clamp01(local / PostRiseSeconds);
                sum += post.Rise;

                float eased = EaseOutBack(post.Rise);
                if (post.Shaft != null)
                {
                    ArcaneSprites.ScalePost(post.Shaft.transform, PostWidth,
                        _config.Height * 1.06f * Mathf.Max(0.001f, eased));

                    float pulse = 0.86f + 0.14f * Mathf.Sin(_age * 3.4f + i);
                    SetAlpha(post.Shaft, 0.92f * eased * pulse * postAlpha);
                }

                if (post.Sigil != null)
                {
                    // Slow, and in alternating directions along the barrier: two discs turning
                    // the same way read as one animation played twice.
                    post.Sigil.transform.localRotation = Quaternion.Euler(0f, 0f,
                        _age * ((i & 1) == 0 ? 26f : -26f));
                    SetAlpha(post.Sigil, 0.55f * eased * postAlpha);
                }
            }
            return sum;
        }

        private void AdvancePanel(Panel panel, float deltaTime, float sweep,
            float meltProgress, float globalAlpha)
        {
            float local = _age - panel.KnitDelay;
            float previousKnit = panel.Knit;
            panel.Knit = local <= 0f ? 0f : Mathf.Clamp01(local / KnitSeconds);

            // Cells woven last let go first. See the class doc.
            float unravel = _shattered
                ? globalAlpha
                : 1f - Mathf.Clamp01(
                    (meltProgress - (1f - panel.KnitRank) * MeltStagger) / (1f - MeltStagger));

            bool geometryMoves = panel.Knit < 1f || previousKnit < 1f || meltProgress > 0f;
            if (geometryMoves)
            {
                // easeOutBack: the cell overshoots and settles, which is what a thing snapping
                // into a lattice looks like. A linear scale reads as a fade-in with a size.
                float eased = EaseOutBack(panel.Knit);
                float shrink = Mathf.Lerp(0.42f, 1f, eased) * Mathf.Lerp(0.55f, 1f, unravel);
                panel.Root.localScale = Vector3.one * (panel.Size * Mathf.Max(0.001f, shrink));
            }

            if (panel.Flash > 0f)
                panel.Flash = Mathf.Max(0f, panel.Flash - deltaTime * 3.4f);

            float shimmer = 0.62f + 0.38f * Mathf.Sin(_age * 2.1f + panel.Phase);
            // A soft band travelling the barrier, so the surface has a direction of grain even
            // when nothing is happening to it.
            float alongUnit = _config.Length > 1e-4f ? panel.Along / _config.Length + 0.5f : 0.5f;
            float highlight = Gaussian(alongUnit - sweep, 0.13f);
            // The knit itself flares: the cell is brightest at the instant it locks in.
            float lock01 = Mathf.Clamp01(1f - Mathf.Abs(panel.Knit - 0.85f) / 0.4f);

            float visible = (0.30f + 0.16f * shimmer + 0.34f * highlight + 0.55f * lock01
                             + 1.5f * panel.Flash) * panel.Knit * unravel * globalAlpha;

            if (!Mathf.Approximately(visible, panel.LastBody))
            {
                SetAlpha(panel.Body, visible);
                panel.LastBody = visible;
            }

            if (panel.Crack == null) return;
            float crack = panel.CrackAlpha * panel.Knit * unravel * globalAlpha
                          * (0.72f + 0.28f * shimmer);
            if (Mathf.Approximately(crack, panel.LastCrack)) return;
            SetAlpha(panel.Crack, crack);
            panel.LastCrack = crack;
        }

        private void AdvanceRunes(float deltaTime, float surfaceAlpha)
        {
            for (int i = 0; i < _runes.Count; i++)
            {
                var rune = _runes[i];
                if (rune.Root == null) continue;

                rune.Age += deltaTime;
                if (rune.Age >= rune.Period) { rune.Age = 0f; PlaceRune(rune); }

                rune.Along = Mathf.Clamp(rune.Along + rune.Drift * deltaTime,
                    -0.48f * _config.Length, 0.48f * _config.Length);
                rune.Root.localPosition = AlongAxis(rune.Along) + new Vector3(0f, rune.Up, 0f);

                // In fast, hold, out slow: a glyph is an event, and an event needs an attack
                // sharper than its decay or it reads as a slow pulse.
                float t = rune.Age / rune.Period;
                float envelope = Mathf.Clamp01(t / 0.16f) * Mathf.Clamp01((1f - t) / 0.34f);
                SetAlpha(rune.Sr, envelope * 0.90f * surfaceAlpha);
            }
        }

        private void AdvanceAmbient(float riseAverage, float knitAverage, float flashSum,
            float globalAlpha, float meltProgress)
        {
            float pulse = 0.90f + 0.10f * Mathf.Sin(_age * 1.9f);

            SetAlpha(_seal, 0.78f * riseAverage * (1f - meltProgress * 0.85f));

            for (int i = 0; i < _edges.Count; i++)
                SetAlpha(_edges[i], (0.66f + 0.20f * pulse) * knitAverage * globalAlpha);

            for (int i = 0; i < _haze.Count; i++)
                SetAlpha(_haze[i], (0.085f + 0.03f * pulse) * knitAverage * globalAlpha);

            SetLightIntensity(
                (_lightBaseIntensity * pulse * Mathf.Max(riseAverage, knitAverage) + 0.6f * flashSum)
                * globalAlpha);
        }

        private static void SetAlpha(SpriteRenderer renderer, float alpha)
        {
            if (renderer == null) return;
            var color = renderer.color;
            color.a = Mathf.Clamp01(alpha);
            renderer.color = color;
        }

        /// <summary>
        /// Unravel over <paramref name="seconds"/>. Emission stops immediately while the motes
        /// already in the air finish their lives — destroying the system outright kills them on
        /// a frame boundary, which is a hard cut inside a soft ending.
        /// </summary>
        public void BeginMelt(float seconds)
        {
            if (_melting) return;
            _melting = true;
            _meltDuration = Mathf.Max(0.05f, seconds);
            _meltTime = 0f;

            if (_motes != null) _motes.Stop(true, ParticleSystemStopBehavior.StopEmitting);
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
