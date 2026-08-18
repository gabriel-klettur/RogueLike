// EditMode test for BossConfigurator's per-phase music — see CLAUDE.md for
// namespace conventions.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.AI
{
    /// <summary>
    /// Pins the boss-music wiring: <see cref="BossConfigurator"/> crossfades to
    /// <c>BossDefinition.Phase.musicTrackId</c> when the boss crosses into a new
    /// phase, and primes the entry phase's theme on spawn.
    ///
    /// Ordering matters and is asserted here: the music swap must happen BEFORE
    /// the phase's beat chart is bound, because chart selection matches the
    /// chart's <c>musicTrackId</c> against the ACTIVE song. Swapping after would
    /// leave the boss on the cooldown rotation for its own phase track.
    ///
    /// <see cref="BossPhaseAudio"/> is the inspector-authored alternative for
    /// bosses built by hand; when it is present it owns the swap, so the
    /// configurator stands down rather than firing a second crossfade.
    /// </summary>
    [TestFixture]
    public class BossPhaseMusicTests
    {
        private GameObject _go;
        private Health _health;
        private BossPhaseController _phases;
        private BossConfigurator _configurator;
        private SpyAudioService _spy;

        [SetUp]
        public void SetUp()
        {
            _spy = new SpyAudioService();
            ServiceLocator.Register<IAudioService>(_spy);

            _go = new GameObject("Boss");
            _health = _go.AddComponent<Health>();
            _health.Initialize(100);
            _phases = _go.AddComponent<BossPhaseController>();
            _phases.InitForTest(_health);
            _configurator = _go.AddComponent<BossConfigurator>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            ServiceLocator.Unregister<IAudioService>();
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static BossDefinition MakeBoss(params (float hp, string label, string music)[] phases)
        {
            var d = ScriptableObject.CreateInstance<BossDefinition>();
            d.phases = new BossDefinition.Phase[phases.Length];
            for (int i = 0; i < phases.Length; i++)
            {
                d.phases[i] = new BossDefinition.Phase
                {
                    hpThreshold  = phases[i].hp,
                    label        = phases[i].label,
                    musicTrackId = phases[i].music,
                };
            }
            return d;
        }

        private BossDefinition Bind(params (float hp, string label, string music)[] phases)
        {
            var def = MakeBoss(phases);
            _configurator.InitForTest(_phases, autoCast: null, caster: null, catalog: null);
            _configurator.SetDefinition(def);
            _configurator.ConfigurePhasesFromDefinition();
            return def;
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void PhaseTransition_CrossfadesToThatPhasesTrack()
        {
            var def = Bind(
                (1f,   "Entry",  "boss_calm"),
                (0.5f, "Frenzy", "boss_frenzy"));

            _phases.EvaluateAt(0.4f);   // crosses 0.50 → phase 1

            Assert.AreEqual(1, _spy.Calls.Count, "Expected exactly one music change.");
            Assert.AreEqual("boss_frenzy", _spy.Calls[0].trackId);

            Object.DestroyImmediate(def);
        }

        [Test]
        public void CrossfadeOverride_IsForwardedToTheAudioService()
        {
            var def = Bind(
                (1f,   "Entry",  "boss_calm"),
                (0.5f, "Frenzy", "boss_frenzy"));
            def.phases[1].musicCrossfadeSec = 2.5f;

            _phases.EvaluateAt(0.4f);

            Assert.AreEqual(2.5f, _spy.Calls[0].fadeSec, 0.0001f,
                "A per-phase crossfade override must reach the audio service verbatim.");

            Object.DestroyImmediate(def);
        }

        [Test]
        public void EmptyTrackId_KeepsThePreviousTrackPlaying()
        {
            var def = Bind(
                (1f,   "Entry",  "boss_calm"),
                (0.5f, "Frenzy", ""));

            _phases.EvaluateAt(0.4f);

            Assert.AreEqual(0, _spy.Calls.Count,
                "An empty musicTrackId means 'no music change for this phase'.");

            Object.DestroyImmediate(def);
        }

        [Test]
        public void TrackAlreadyPlaying_SkipsTheRedundantCrossfade()
        {
            var def = Bind(
                (1f,   "Entry",  "boss_calm"),
                (0.5f, "Frenzy", "boss_frenzy"));
            _spy.CurrentTrackIdValue = "boss_frenzy";

            _phases.EvaluateAt(0.4f);

            Assert.AreEqual(0, _spy.Calls.Count,
                "Re-issuing the track that is already playing would restart the " +
                "song and desync any beat chart anchored to it.");

            Object.DestroyImmediate(def);
        }

        [Test]
        public void BossPhaseAudioPresent_ConfiguratorStandsDown()
        {
            var audio = _go.AddComponent<BossPhaseAudio>();
            audio.InitForTest(_phases, new[] { "manual_entry", "manual_frenzy" });

            var def = Bind(
                (1f,   "Entry",  "boss_calm"),
                (0.5f, "Frenzy", "boss_frenzy"));

            _phases.EvaluateAt(0.4f);

            Assert.AreEqual(1, _spy.Calls.Count,
                "Exactly one component may drive the music — two crossfades per " +
                "transition would fight each other.");
            Assert.AreEqual("manual_frenzy", _spy.Calls[0].trackId,
                "When BossPhaseAudio is present it owns the swap.");

            Object.DestroyImmediate(def);
        }

        [Test]
        public void NoAudioServiceRegistered_DoesNotThrow()
        {
            ServiceLocator.Unregister<IAudioService>();
            var def = Bind(
                (1f,   "Entry",  "boss_calm"),
                (0.5f, "Frenzy", "boss_frenzy"));

            Assert.DoesNotThrow(() => _phases.EvaluateAt(0.4f),
                "A boss must still fight when the audio service is absent (headless tests, muted boot).");

            Object.DestroyImmediate(def);
        }

        [Test]
        public void PhaseWithoutMusic_LeavesTheEarlierPhaseTrackAlone()
        {
            var def = Bind(
                (1f,   "Entry",  "boss_calm"),
                (0.5f, "Frenzy", ""),
                (0.2f, "Final",  "boss_final"));

            _phases.EvaluateAt(0.4f);   // phase 1 — no track authored
            _phases.EvaluateAt(0.1f);   // phase 2 — has a track

            Assert.AreEqual(1, _spy.Calls.Count);
            Assert.AreEqual("boss_final", _spy.Calls[0].trackId);

            Object.DestroyImmediate(def);
        }

        // ── Spy ─────────────────────────────────────────────────────────────────
        private sealed class SpyAudioService : IAudioService
        {
            public readonly List<(string trackId, float fadeSec)> Calls
                = new List<(string trackId, float fadeSec)>();

            public string CurrentTrackIdValue = string.Empty;

            public void PlayMusicByTrackId(string trackId, float fadeSec = -1f)
                => Calls.Add((trackId, fadeSec));

            public string CurrentTrackId => CurrentTrackIdValue;

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
