using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Plays a phase-specific ambient bed (wind / crickets / birds) and
    /// crossfades between them whenever <see cref="DayNightCycle.OnPhaseChanged"/>
    /// fires. Uses two private <see cref="AudioSource"/>s — A and B — that swap
    /// roles each transition so the previous bed fades out while the new one
    /// fades in over the same window. Independent of the music track and the
    /// catalog-driven <c>EnableAmbient(...)</c> SFX picker — those continue to
    /// run in parallel.
    ///
    /// All four clips are optional. When a phase has no clip the active source
    /// simply fades to silence; gives designers a way to opt out of (e.g.) day
    /// ambience without breaking the rest of the cycle.
    /// </summary>
    public sealed class DayNightAmbientAudio : MonoBehaviour
    {
        [Header("Phase clips (any may be null)")]
        [SerializeField, Tooltip("Looping ambient bed for the Dawn phase (e.g. distant birdsong, soft wind).")]
        private AudioClip _dawnClip;
        [SerializeField, Tooltip("Looping ambient bed for the Day phase (e.g. light breeze).")]
        private AudioClip _dayClip;
        [SerializeField, Tooltip("Looping ambient bed for the Dusk phase (e.g. evening insects, distant howl).")]
        private AudioClip _duskClip;
        [SerializeField, Tooltip("Looping ambient bed for the Night phase (e.g. crickets, owl, soft wind).")]
        private AudioClip _nightClip;

        [Header("Mix")]
        [SerializeField, Range(0f, 1f), Tooltip("Master volume for the crossfading ambient bed. " +
                                                "Multiplied with whatever is playing on the active source.")]
        private float _masterVolume = 0.45f;

        [SerializeField, Tooltip("Crossfade length in seconds when the cycle phase changes.")]
        private float _crossfadeSeconds = 4f;

        // ── Internal state ───────────────────────────────────────────────────
        private AudioSource _srcA;
        private AudioSource _srcB;
        private bool _aIsActive = true;          // which source currently holds the live clip

        // Crossfade animation state (single coroutine-free Update timer).
        private float     _fadeT;
        private float     _fadeDuration;
        private bool      _fading;
        private AudioClip _fadingInClip;
        private float     _fadeInStartVol, _fadeOutStartVol;

        private DayNightCycle.DayPhase _appliedPhase = (DayNightCycle.DayPhase)(-1);

        private void Awake()
        {
            _srcA = CreateLoopingSource("AmbientA");
            _srcB = CreateLoopingSource("AmbientB");
        }

        private void OnEnable()  => DayNightCycle.OnPhaseChanged += HandlePhaseChanged;
        private void OnDisable() => DayNightCycle.OnPhaseChanged -= HandlePhaseChanged;

        private void Start()
        {
            // Snap to the current phase rather than waiting for the next event.
            if (DayNightCycle.Instance != null)
                ApplyPhaseImmediate(DayNightCycle.Instance.CurrentPhase);
        }

        private void Update()
        {
            if (!_fading) return;
            _fadeT += Time.deltaTime;
            float t = Mathf.Clamp01(_fadeT / Mathf.Max(0.01f, _fadeDuration));

            var fadeIn  = _aIsActive ? _srcA : _srcB;
            var fadeOut = _aIsActive ? _srcB : _srcA;
            fadeIn.volume  = Mathf.Lerp(_fadeInStartVol,  _masterVolume, t);
            fadeOut.volume = Mathf.Lerp(_fadeOutStartVol, 0f,            t);

            if (t >= 1f)
            {
                _fading = false;
                fadeOut.Stop();
                fadeOut.clip = null;
            }
        }

        // ── Phase handling ───────────────────────────────────────────────────

        private void HandlePhaseChanged(DayNightCycle.DayPhase phase) => StartCrossfadeTo(phase);

        private void ApplyPhaseImmediate(DayNightCycle.DayPhase phase)
        {
            _appliedPhase = phase;
            var clip = ClipFor(phase);
            var active = _aIsActive ? _srcA : _srcB;
            if (clip == null) { active.Stop(); active.volume = 0f; return; }
            active.clip   = clip;
            active.volume = _masterVolume;
            active.Play();
        }

        private void StartCrossfadeTo(DayNightCycle.DayPhase phase)
        {
            if (phase == _appliedPhase) return;
            _appliedPhase = phase;

            var newClip = ClipFor(phase);
            // Swap which source is active so the new clip plays on the other one.
            _aIsActive = !_aIsActive;
            var fadeIn  = _aIsActive ? _srcA : _srcB;
            var fadeOut = _aIsActive ? _srcB : _srcA;

            _fadeOutStartVol = fadeOut.volume;
            if (newClip != null)
            {
                fadeIn.clip   = newClip;
                fadeIn.volume = 0f;
                fadeIn.Play();
                _fadeInStartVol = 0f;
            }
            else
            {
                _fadeInStartVol = 0f;
                fadeIn.Stop();
            }

            _fadeT        = 0f;
            _fadeDuration = _crossfadeSeconds;
            _fading       = true;
            _fadingInClip = newClip;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private AudioSource CreateLoopingSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.loop                  = true;
            src.spatialBlend          = 0f;        // 2D — ambient beds always heard
            src.playOnAwake           = false;
            src.volume                = 0f;
            src.priority              = 200;       // de-prioritise vs combat SFX
            src.bypassEffects         = true;
            src.bypassListenerEffects = true;
            src.bypassReverbZones     = true;
            return src;
        }

        private AudioClip ClipFor(DayNightCycle.DayPhase phase) => phase switch
        {
            DayNightCycle.DayPhase.Dawn  => _dawnClip,
            DayNightCycle.DayPhase.Dusk  => _duskClip,
            DayNightCycle.DayPhase.Night => _nightClip,
            _                             => _dayClip,
        };
    }
}
