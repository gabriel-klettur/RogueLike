using UnityEngine;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Turns a window of music samples into N bars, the way an ear hears them: a Hann-windowed
    /// radix-2 FFT, bands spaced LOGARITHMICALLY between <see cref="MinHz"/> and
    /// <see cref="MaxHz"/> (so the bass does not get one bar and the cymbals forty), levels in
    /// decibels over a fixed floor, and an automatic gain that follows the song's loud
    /// passages down slowly — so a quiet ballad fills the bars as well as a battle theme.
    ///
    /// <para><b>Pure.</b> No Unity audio call happens here; the samples come from
    /// <see cref="Valkur.Core.IMusicSignalSource"/>. An EditMode test feeds it a synthetic
    /// sine and asserts the right bar lights.</para>
    ///
    /// <para><b>It does not allocate after construction.</b> Every buffer is sized once.</para>
    /// </summary>
    public sealed class MusicSpectrum
    {
        public const float MinHz = 45f;
        public const float MaxHz = 12000f;

        /// <summary>Decibels below the automatic gain's reference that read as an empty bar.</summary>
        public const float RangeDb = 42f;

        private readonly int _size;
        private readonly float[] _re, _im, _window, _mag;
        private readonly int[] _bandLo, _bandHi;
        private readonly float[] _levels;
        private float _reference = 1e-4f;
        private int _sampleRate;

        /// <summary>Current bar levels, 0..1, attack-fast and decay-slow smoothed.</summary>
        public float[] Levels => _levels;

        /// <summary>Number of bars.</summary>
        public int Bands => _levels.Length;

        /// <summary>The FFT length. A power of two.</summary>
        public int Size => _size;

        public MusicSpectrum(int bands, int fftSize = 1024)
        {
            if (fftSize < 64 || (fftSize & (fftSize - 1)) != 0)
                throw new System.ArgumentException("FFT size must be a power of two >= 64.", nameof(fftSize));
            _size = fftSize;
            _re = new float[fftSize];
            _im = new float[fftSize];
            _mag = new float[fftSize / 2];
            _window = new float[fftSize];
            for (int i = 0; i < fftSize; i++)
                _window[i] = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * i / (fftSize - 1));
            bands = Mathf.Max(1, bands);
            _levels = new float[bands];
            _bandLo = new int[bands];
            _bandHi = new int[bands];
            SetSampleRate(48000);
        }

        /// <summary>Recomputes the band edges for a sample rate. Cheap; call when it changes.</summary>
        public void SetSampleRate(int sampleRate)
        {
            if (sampleRate <= 0 || sampleRate == _sampleRate) return;
            _sampleRate = sampleRate;
            float binHz = (float)sampleRate / _size;
            int maxBin = _size / 2 - 1;
            float top = Mathf.Min(MaxHz, sampleRate * 0.5f);
            int bands = _levels.Length;
            for (int b = 0; b < bands; b++)
            {
                float f0 = MinHz * Mathf.Pow(top / MinHz, (float)b / bands);
                float f1 = MinHz * Mathf.Pow(top / MinHz, (float)(b + 1) / bands);
                int lo = Mathf.Clamp(Mathf.FloorToInt(f0 / binHz), 1, maxBin);
                int hi = Mathf.Clamp(Mathf.CeilToInt(f1 / binHz), lo + 1, maxBin + 1);
                _bandLo[b] = lo;
                _bandHi[b] = hi;
            }
        }

        /// <summary>The centre frequency of a bar, in Hz. For the tests.</summary>
        public float BandCentreHz(int band)
        {
            float binHz = (float)_sampleRate / _size;
            return (_bandLo[band] + _bandHi[band]) * 0.5f * binHz;
        }

        /// <summary>
        /// Analyses the latest <paramref name="count"/> samples of <paramref name="samples"/>
        /// (fewer than <see cref="Size"/> are zero-padded) and moves the bars towards the result
        /// over <paramref name="dt"/> seconds. Pass <paramref name="count"/> 0 to let the bars fall.
        /// </summary>
        public void Analyse(float[] samples, int count, float dt)
        {
            int bands = _levels.Length;
            if (samples == null || count <= 0)
            {
                Decay(dt);
                return;
            }

            count = Mathf.Min(count, Mathf.Min(samples.Length, _size));
            int offset = count - _size;
            for (int i = 0; i < _size; i++)
            {
                int s = i + offset;
                _re[i] = s >= 0 ? samples[s] * _window[i] : 0f;
                _im[i] = 0f;
            }
            Transform(_re, _im);
            float norm = 2f / _size;
            for (int k = 0; k < _mag.Length; k++)
                _mag[k] = Mathf.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]) * norm;

            // The loudest band this frame drives the automatic gain: up at once, down over
            // about four seconds, and never below a floor, so a silent passage does not amplify
            // the hiss between two tracks into a full wall of bars.
            float frameMax = 0f;
            for (int b = 0; b < bands; b++)
            {
                float peak = 0f;
                for (int k = _bandLo[b]; k < _bandHi[b]; k++) if (_mag[k] > peak) peak = _mag[k];
                _re[b] = peak; // reuse as scratch: band peak magnitudes
                if (peak > frameMax) frameMax = peak;
            }
            _reference = Mathf.Max(1e-4f, frameMax > _reference ? frameMax : Mathf.Lerp(_reference, frameMax, Mathf.Clamp01(dt * 0.25f)));

            for (int b = 0; b < bands; b++)
            {
                float db = 20f * Mathf.Log10(Mathf.Max(1e-7f, _re[b]) / _reference);
                float target = Mathf.Clamp01(1f + db / RangeDb);
                float current = _levels[b];
                // Fast up, slow down: a drum hit lands on the frame it happens and lingers just
                // long enough to be seen.
                _levels[b] = target > current
                    ? Mathf.Lerp(current, target, Mathf.Clamp01(dt * 28f))
                    : Mathf.Max(target, current - dt * 1.6f);
            }
        }

        /// <summary>Lets every bar fall towards zero, as when the music stops.</summary>
        public void Decay(float dt)
        {
            for (int b = 0; b < _levels.Length; b++)
                _levels[b] = Mathf.Max(0f, _levels[b] - dt * 1.6f);
        }

        /// <summary>In-place iterative radix-2 FFT.</summary>
        private static void Transform(float[] re, float[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                float ang = -2f * Mathf.PI / len;
                float wr = Mathf.Cos(ang), wi = Mathf.Sin(ang);
                int half = len >> 1;
                for (int i = 0; i < n; i += len)
                {
                    float cr = 1f, ci = 0f;
                    for (int k = 0; k < half; k++)
                    {
                        int a = i + k, b = a + half;
                        float tr = re[b] * cr - im[b] * ci;
                        float ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr;
                        im[b] = im[a] - ti;
                        re[a] += tr;
                        im[a] += ti;
                        float ncr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = ncr;
                    }
                }
            }
        }
    }
}
