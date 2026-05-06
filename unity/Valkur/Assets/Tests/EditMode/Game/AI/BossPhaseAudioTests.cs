// EditMode test for BossPhaseAudio — see CLAUDE.md for namespace conventions.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.AI
{
    /// <summary>
    /// Pins <see cref="BossPhaseAudio"/>: when the sibling
    /// <see cref="BossPhaseController"/> announces a phase transition, the
    /// configured catalog track id for the new phase is forwarded to
    /// <c>IAudioService.PlayMusicByTrackId</c>; empty entries are skipped;
    /// out-of-range indices are tolerated; unsubscription happens cleanly
    /// on disable so a destroyed boss does not keep firing music.
    /// </summary>
    [TestFixture]
    public class BossPhaseAudioTests
    {
        private GameObject _go;
        private Health _health;
        private BossPhaseController _controller;
        private BossPhaseAudio _audio;
        private SpyAudioService _spy;

        [SetUp]
        public void SetUp()
        {
            _spy = new SpyAudioService();
            ServiceLocator.Register<IAudioService>(_spy);

            _go = new GameObject("Boss");
            _health = _go.AddComponent<Health>();
            _health.Initialize(100);
            _controller = _go.AddComponent<BossPhaseController>();
            _controller.InitForTest(_health);
            _audio = _go.AddComponent<BossPhaseAudio>();
            _audio.InitForTest(_controller,
                new[] { "phase_1_theme", "phase_2_theme", "phase_3_theme" });
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            ServiceLocator.Unregister<IAudioService>();
        }

        [Test]
        public void OnPhaseChanged_FiresPlayMusicByTrackId_WithMappedId()
        {
            _controller.EvaluateAt(0.4f);   // crosses 0.50 → phase 1
            Assert.AreEqual(1, _spy.Calls.Count, "Expected exactly one music change.");
            Assert.AreEqual("phase_2_theme", _spy.Calls[0].trackId);
        }

        [Test]
        public void EmptyTrackId_SkipsTheCall()
        {
            _audio.InitForTest(_controller,
                new[] { "phase_1_theme", "", "phase_3_theme" });
            _controller.EvaluateAt(0.4f);   // phase 1, but mapping is empty
            Assert.AreEqual(0, _spy.Calls.Count);
        }

        [Test]
        public void IndexOutOfRange_DoesNotThrow()
        {
            _audio.InitForTest(_controller, new[] { "phase_1_theme" });   // only 1 entry, 3 phases
            Assert.DoesNotThrow(() => _controller.EvaluateAt(0.1f));      // crosses 0.20 → phase 2
            Assert.AreEqual(0, _spy.Calls.Count);
        }

        [Test]
        public void DisabledComponent_DoesNotForwardEvents()
        {
            _audio.enabled = false;          // triggers OnDisable → unsubscribes
            _controller.EvaluateAt(0.4f);
            Assert.AreEqual(0, _spy.Calls.Count);
        }

        // ── Spy ─────────────────────────────────────────────────────────────────
        private sealed class SpyAudioService : IAudioService
        {
            public readonly List<(string trackId, float fadeSec)> Calls
                = new List<(string trackId, float fadeSec)>();

            public void PlayMusicByTrackId(string trackId, float fadeSec = -1f)
                => Calls.Add((trackId, fadeSec));

            // ── Unused interface members ──
            public void PlayMusic(AudioClip clip) {}
            public void PlayMusic(AudioClip clip, float fadeInSec) {}
            public void CrossfadeTo(AudioClip clip, float durationSec = 0.6f) {}
            public void StopMusic() {}
            public void StopMusic(float fadeOutSec) {}
            public void SetMusicVolume(float vol) {}
            public void PauseMusic() {}
            public void ResumeMusic() {}
            public bool IsMusicPaused => false;
            public float MusicVolume => 1f;
            public void SkipToNextTrack() {}
            public void SkipToPreviousTrack() {}
            public bool HasActivePlaylist => false;
            public void PlaySFX(AudioClip clip, float volumeScale = 1f) {}
            public void PlaySFXAtPosition(AudioClip clip, Vector3 position, float volumeScale = 1f) {}
            public void PlaySfxById(string sfxId, float volumeScale = 1f) {}
            public void PlaySfxRandom(string[] sfxIds, float volumeScale = 1f) {}
            public void SetSFXVolume(float vol) {}
            public void SetAmbientVolume(float vol) {}
            public void EnableAmbient(string[] choiceIds, float minInterval, float maxInterval) {}
            public void DisableAmbient() {}
            public void StartPlaylist(AudioClip[] tracks, float intervalSec = 120f, bool shuffle = true) {}
            public void StopPlaylist() {}
            public bool IsMusicPlaying => false;
            public AudioClip CurrentMusicClip => null;
            public string CurrentTrackTitle => string.Empty;
            public string CurrentTrackId => string.Empty;
            public float CurrentTrackBpm => 0f;
            public int CurrentTrackBeatsPerBar => 4;
            public float CurrentTrackBeatOffsetSec => 0f;
            public float[] CurrentTrackBeatTimes => null;
            public string CurrentTrackKey => string.Empty;
            public float CurrentMusicTime => 0f;
            public void SeekMusic(float seconds) {}
            public bool GetMusicSpectrumData(float[] buffer, int channel = 0, FFTWindow window = FFTWindow.BlackmanHarris) => false;
            public bool GetMusicOutputData(float[] buffer, int channel = 0) => false;
            public event System.Action<string, string, float, int> OnTrackChanged { add {} remove {} }
            public void OnZoneChanged(string zoneName, string levelName = null, string biomeName = null) {}
            public void EnterGameAudio() {}
            public void PlayMenuMusic() {}
            public void TransitionMenuToGame() {}
            public void ApplySettings() {}
        }
    }
}
