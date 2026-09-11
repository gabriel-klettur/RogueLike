using UnityEngine;

namespace Valkur.Infrastructure
{
    /// <summary>
    /// Copies a music source's signal off the audio thread into a ring buffer, so the music
    /// panel's visualiser can read the song at the level it was mastered at, whatever the
    /// player's volume.
    ///
    /// <para><b>Why a filter.</b> <c>AudioSource.GetOutputData</c> and <c>GetSpectrumData</c>
    /// read AFTER the source volume: with the music slider at 0 both return silence (measured:
    /// peak 0.0000, spectrum sum 0.000013), so the old expanded panel was a black slab for any
    /// player who plays with the music off. <c>OnAudioFilterRead</c> sees the samples on their
    /// way through the source's DSP chain; the volume that has been applied to them by then is
    /// divided back out here, using the value the main thread last saw on the source.</para>
    ///
    /// <para><b>Measured, not assumed:</b> at a source volume of 0.02 the filter's peak was
    /// 8.2e-3 against 9.1e-3 from <c>GetOutputData</c> — the filter sees the signal AFTER the
    /// volume, hence the division.</para>
    ///
    /// <para><b>Muted is not zero.</b> Dividing by a volume of 0 recovers nothing, so
    /// <see cref="AudioManager"/> never drives a music source below
    /// <see cref="AudioManager.MusicSilenceFloor"/> (1e-4, -80 dBFS: silent to the player, still
    /// a signal to divide back up). And a voice STARTED that quietly is started virtual and fed
    /// zeros, so the tap reports <see cref="Starved"/> and the manager wakes it for three frames
    /// at -54 dBFS; once real it stays real at the floor.</para>
    ///
    /// <para>The audio thread only ever writes the ring and the main thread only ever reads it,
    /// under one lock held for a copy of at most <see cref="Capacity"/> floats.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class MusicSignalTap : MonoBehaviour
    {
        /// <summary>Ring length in mono samples. A power of two, ~46 ms at 44.1 kHz.</summary>
        public const int Capacity = 2048;

        private readonly float[] _ring = new float[Capacity];
        private readonly object _gate = new object();
        private int _head;
        private int _filled;

        private AudioSource _source;
        // Written on the main thread, read on the audio thread. A float write is atomic.
        private volatile float _appliedGain = 1f;
        private volatile bool _live;

        /// <summary>
        /// True when the samples reaching the filter have already been multiplied by the source
        /// volume, so <see cref="Read"/> must divide it back out. Measured live on this Unity
        /// version; see the class summary.
        /// </summary>
        public const bool FilterSeesVolume = true;

        /// <summary>True while the source is playing and the ring holds fresh samples.</summary>
        public bool IsLive => _live && _filled > 0;

        // Set on the audio thread when a buffer carried any non-zero sample; consumed by Update.
        private volatile bool _sawSignal;
        private float _lastSignalTime;

        /// <summary>
        /// True when the source has been playing for a while and every sample the filter received
        /// was an exact zero. At a quiet enough volume Unity starts a voice VIRTUAL and never
        /// feeds its filter; <see cref="AudioManager"/> answers by waking the voice.
        /// </summary>
        public bool Starved => _live && Time.unscaledTime - _lastSignalTime > StarvedSeconds;

        private const float StarvedSeconds = 0.3f;

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
        }

        private void Update()
        {
            if (_source == null) return;
            _appliedGain = _source.volume;
            bool wasLive = _live;
            _live = _source.isPlaying;
            if (!_live) Clear();
            // A voice that just started playing gets the grace period before it can be starved.
            if (_sawSignal || (_live && !wasLive))
            {
                _sawSignal = false;
                _lastSignalTime = Time.unscaledTime;
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (channels <= 0) return;
            int frames = data.Length / channels;
            bool any = false;
            lock (_gate)
            {
                for (int f = 0; f < frames; f++)
                {
                    float sum = 0f;
                    int b = f * channels;
                    for (int c = 0; c < channels; c++) sum += data[b + c];
                    float v = sum / channels;
                    if (v != 0f) any = true;
                    _ring[_head] = v;
                    _head = (_head + 1) & (Capacity - 1);
                }
                _filled = Mathf.Min(Capacity, _filled + frames);
            }
            if (any) _sawSignal = true;
        }

        /// <summary>
        /// Copies the latest <c>dest.Length</c> samples (or fewer), oldest first, at mastered
        /// level. Returns the count written.
        /// </summary>
        public int Read(float[] dest)
        {
            if (dest == null || dest.Length == 0 || !_live) return 0;
#pragma warning disable CS0162 // one branch is unreachable by construction: the constant records a measurement
            float gain = FilterSeesVolume ? _appliedGain : 1f;
#pragma warning restore CS0162
            if (gain <= 0f) return 0;
            float inv = 1f / gain;
            lock (_gate)
            {
                int n = Mathf.Min(dest.Length, _filled);
                int start = (_head - n) & (Capacity - 1);
                for (int i = 0; i < n; i++) dest[i] = _ring[(start + i) & (Capacity - 1)] * inv;
                return n;
            }
        }

        /// <summary>The raw ring as received, without any gain correction. For the probe.</summary>
        public float RawRms()
        {
            lock (_gate)
            {
                if (_filled == 0) return 0f;
                double acc = 0;
                for (int i = 0; i < _filled; i++) acc += _ring[i] * _ring[i];
                return Mathf.Sqrt((float)(acc / _filled));
            }
        }

        private void Clear()
        {
            lock (_gate)
            {
                _filled = 0;
                _head = 0;
            }
        }
    }
}
