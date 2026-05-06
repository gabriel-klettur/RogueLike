using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Optional sibling component for a boss: subscribes to
    /// <see cref="BossPhaseController.OnPhaseChanged"/> and crossfades the
    /// game music to a phase-specific track id (resolved through the
    /// <see cref="IAudioService"/>'s catalog).
    ///
    /// Designers author one entry per phase. An empty entry is treated as
    /// "no music change for this phase" (the previous track keeps playing).
    /// The component does nothing if no <c>BossPhaseController</c> sits on
    /// the same GameObject.
    ///
    /// This is decoupled from <see cref="BossPhaseController"/> on purpose:
    /// the controller stays a pure HP-driven phase tracker; phase listeners
    /// (audio here, boss-cue dispatcher elsewhere, animation hooks
    /// elsewhere) plug into its <c>OnPhaseChanged</c> event.
    /// </summary>
    [RequireComponent(typeof(BossPhaseController))]
    public sealed class BossPhaseAudio : MonoBehaviour
    {
        [Tooltip("One catalog track id per phase, in BossPhaseController order. " +
                 "Leave an entry empty to skip the music swap for that phase. " +
                 "Track ids are resolved through AudioCatalogSO.")]
        [SerializeField] private string[] phaseMusicTrackIds = System.Array.Empty<string>();

        [Tooltip("Optional crossfade override in seconds. -1 (default) uses the " +
                 "catalog's configured CrossfadeSec.")]
        [SerializeField] private float crossfadeOverrideSec = -1f;

        private BossPhaseController _phases;
        private bool _subscribed;

        private void Awake() => _phases = GetComponent<BossPhaseController>();

        private void OnEnable()
        {
            if (_phases == null) _phases = GetComponent<BossPhaseController>();
            if (_phases == null || _subscribed) return;
            _phases.OnPhaseChanged += HandlePhaseChanged;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (_phases == null || !_subscribed) return;
            _phases.OnPhaseChanged -= HandlePhaseChanged;
            _subscribed = false;
        }

        // Test seam — lets EditMode tests drive transitions without a live
        // Health/Awake/OnEnable sequence. EditMode's AddComponent does not
        // call OnEnable so we subscribe explicitly here. OnDisable still
        // fires on enabled=false / DestroyImmediate, which keeps the
        // subscription state consistent.
        public void InitForTest(BossPhaseController phases, string[] trackIds)
        {
            if (_subscribed && _phases != null)
            {
                _phases.OnPhaseChanged -= HandlePhaseChanged;
                _subscribed = false;
            }
            _phases = phases;
            phaseMusicTrackIds = trackIds ?? System.Array.Empty<string>();
            if (_phases != null)
            {
                _phases.OnPhaseChanged += HandlePhaseChanged;
                _subscribed = true;
            }
        }

        private void HandlePhaseChanged(int oldPhase, int newPhase)
        {
            // Belt-and-suspenders: OnEnable/OnDisable own the subscription
            // lifecycle, but a disabled component should never push audio
            // changes. This also keeps EditMode tests deterministic — they
            // can flip `enabled` directly without depending on Unity's
            // lifecycle hooks firing.
            if (!isActiveAndEnabled) return;
            if (phaseMusicTrackIds == null) return;
            if (newPhase < 0 || newPhase >= phaseMusicTrackIds.Length) return;
            string trackId = phaseMusicTrackIds[newPhase];
            if (string.IsNullOrEmpty(trackId)) return;

            var audio = ServiceLocator.Get<IAudioService>();
            audio?.PlayMusicByTrackId(trackId, crossfadeOverrideSec);
        }
    }
}
