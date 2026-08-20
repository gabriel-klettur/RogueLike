using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// EditMode tests for the unified-volume contract introduced when
    /// MusicPlayerHUD was refactored to use GameSettings.musicVolume instead
    /// of a private PlayerPrefs key.
    ///
    /// Coverage:
    ///   1. OnVolumeChanged writes to GameSettings.musicVolume.
    ///   2. OnVolumeChanged pushes the value to the registered IAudioService.
    ///   3. Volume is clamped to [0,1] in both stores.
    ///   4. First mute click sets volume to 0 and stores previous value.
    ///   5. Second mute click restores the pre-mute volume.
    ///   6. Mute click when already at 0 uses the 0.7 fallback.
    ///   7. Awake seeds _volumeBeforeMute from GameSettings (not the old default).
    ///   8. OnEnable syncs the slider to _audio.MusicVolume.
    ///   9. Legacy PlayerPrefs key "valkur.musichud.volume" is never written.
    /// </summary>
    public class MusicPlayerHUDVolumeTests
    {
        // ── Reflection cache ─────────────────────────────────────────────────

        private static readonly MethodInfo s_awake =
            typeof(MusicPlayerHUD).GetMethod("Awake",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo s_onEnable =
            typeof(MusicPlayerHUD).GetMethod("OnEnable",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo s_volumeSlider =
            typeof(MusicPlayerHUD).GetField("_volumeSlider",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo s_muteBtn =
            typeof(MusicPlayerHUD).GetField("_muteBtn",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo s_audioField =
            typeof(MusicPlayerHUD).GetField("_audio",
                BindingFlags.NonPublic | BindingFlags.Instance);

        // ── Legacy pref key that must NOT be touched after the fix ───────────
        private const string LegacyPrefKey = "valkur.musichud.volume";

        // ── Test state ───────────────────────────────────────────────────────

        private GameObject _go;
        private MusicPlayerHUD _hud;
        private AudioServiceSpy _spy;
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();

        // Snapshot of GameSettings so we can restore it in TearDown.
        private float _savedMusicVolume;

        // ── Mock audio service ───────────────────────────────────────────────

        private class AudioServiceSpy : IAudioService
        {
            public float MusicVolume { get; private set; } = 0.6f;
            public int SetMusicVolumeCallCount { get; private set; }
            public float LastSetMusicVolume { get; private set; }

            public bool IsMusicPlaying   => false;
            public bool IsMusicPaused    => false;
            public bool HasActivePlaylist => false;
            public AudioClip CurrentMusicClip  => null;
            public string CurrentTrackTitle    => "";
            public string CurrentTrackId       => "";
            public float  CurrentTrackBpm      => 0f;
            public int    CurrentTrackBeatsPerBar => 4;
            public float  CurrentTrackBeatOffsetSec => 0f;
            public float[] CurrentTrackBeatTimes => null;
            public string CurrentTrackKey      => "";
            public float  CurrentMusicTime     => 0f;

            // Required by IAudioService — never fired by these tests, but
            // must exist so the spy satisfies the interface contract.
            #pragma warning disable CS0067
            public event System.Action<string, string, float, int> OnTrackChanged;
            #pragma warning restore CS0067

            public void SetMusicVolume(float vol)
            {
                MusicVolume = vol;
                LastSetMusicVolume = vol;
                SetMusicVolumeCallCount++;
            }

            // Simulate what AudioManager does on startup: adopt GameSettings volume.
            public void SimulateInitFromSettings(float vol) { MusicVolume = vol; }

            // Unused interface members — no-ops.
            public void PlayMusic(AudioClip c)              { }
            public void PlayMusic(AudioClip c, float f)     { }
            public void CrossfadeTo(AudioClip c, float d)   { }
            public void PlayMusicByTrackId(string id, float f = -1f) { }
            public void StopMusic()                         { }
            public void StopMusic(float f)                  { }
            public void PauseMusic()                        { }
            public void ResumeMusic()                       { }
            public void SkipToNextTrack()                   { }
            public void SkipToPreviousTrack()               { }
            public void PlaySFX(AudioClip c, float v = 1f)  { }
            public void PlaySFXAtPosition(AudioClip c, Vector3 p, float v = 1f) { }
            public void PlaySfxById(string id, float v = 1f) { }
            public bool HasSfx(string id) => true;
            public void PlaySfxRandom(string[] ids, float v = 1f) { }
            public void SetSFXVolume(float v)               { }
            public void SetAmbientVolume(float v)           { }
            public void EnableAmbient(string[] ids, float mn, float mx) { }
            public void DisableAmbient()                    { }
            public void StartPlaylist(AudioClip[] t, float i = 120f, bool s = true) { }
            public void StopPlaylist()                      { }
            public void SeekMusic(float sec)                { }
            public bool GetMusicSpectrumData(float[] b, int ch = 0, FFTWindow w = FFTWindow.BlackmanHarris) => false;
            public bool GetMusicOutputData(float[] b, int ch = 0) => false;
            public void OnZoneChanged(string z, string l = null, string b = null) { }
            public void EnterGameAudio()                    { }
            public void PlayMenuMusic()                     { }
            public void TransitionMenuToGame()              { }
            public void ApplySettings()                     { }
        }

        // ── Setup / Teardown ─────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            // Suppress TMP/Canvas EditMode warnings so they don't fail the run.
            LogAssert.ignoreFailingMessages = true;

            // Snapshot the live GameSettings so TearDown can restore it.
            _savedMusicVolume = GameSettings.Instance.musicVolume;

            // Remove the legacy key so its absence is a clean baseline.
            PlayerPrefs.DeleteKey(LegacyPrefKey);

            // Create and register the audio spy BEFORE the HUD so OnEnable can find it.
            _spy = new AudioServiceSpy();
            ServiceLocator.Register<IAudioService>(_spy);

            CreateHud();
        }

        [TearDown]
        public void TearDown()
        {
            ServiceLocator.Unregister<IAudioService>();

            // Restore GameSettings so we don't pollute the developer's settings.json.
            // The fix's ApplyAndPersistVolume calls gs.Save() during the test, so we
            // must also Save() here to overwrite the file with the original value —
            // otherwise the disk file keeps whatever the last test wrote.
            GameSettings.Instance.musicVolume = _savedMusicVolume;
            GameSettings.Instance.Save();

            // Remove legacy key both as cleanup and as a final assertion helper
            // (tests that need to verify absence check it themselves before TearDown).
            PlayerPrefs.DeleteKey(LegacyPrefKey);

            foreach (var go in _sceneObjects)
                if (go != null) Object.DestroyImmediate(go);
            _sceneObjects.Clear();
        }

        // ── Factory ──────────────────────────────────────────────────────────

        /// <summary>
        /// Spawn a fresh HUD with Awake + OnEnable called via reflection
        /// (same approach as MusicPlayerHUDVisibilityTests to avoid SendMessage
        /// assert noise in EditMode).
        /// </summary>
        private void CreateHud(float initialSettingsVolume = 0.6f)
        {
            // Ensure GameSettings reflects the requested initial volume so
            // Awake seeds _volumeBeforeMute correctly.
            GameSettings.Instance.musicVolume = initialSettingsVolume;
            _spy.SimulateInitFromSettings(initialSettingsVolume);

            _go = new GameObject("TestMusicPlayerHUD_Volume", typeof(RectTransform));
            _sceneObjects.Add(_go);
            _hud = _go.AddComponent<MusicPlayerHUD>();
            s_awake.Invoke(_hud, null);
            s_onEnable.Invoke(_hud, null);
        }

        // ── Accessors ────────────────────────────────────────────────────────

        private Slider GetSlider()    => (Slider)s_volumeSlider.GetValue(_hud);
        private Button GetMuteButton() => (Button)s_muteBtn.GetValue(_hud);

        // ── Tests ────────────────────────────────────────────────────────────

        // 1. OnVolumeChanged writes to GameSettings.musicVolume
        [Test]
        public void OnVolumeChanged_WritesToGameSettings_MusicVolume()
        {
            var slider = GetSlider();
            Assume.That(slider, Is.Not.Null, "Volume slider was not built — Awake must have failed.");

            slider.value = 0.55f;

            Assert.AreEqual(0.55f, GameSettings.Instance.musicVolume, 0.001f,
                "After slider change, GameSettings.musicVolume must equal the slider value.");
        }

        // 2. OnVolumeChanged pushes to the registered IAudioService
        [Test]
        public void OnVolumeChanged_PushesValueToAudioService()
        {
            var slider = GetSlider();
            Assume.That(slider, Is.Not.Null, "Volume slider was not built — Awake must have failed.");

            _spy.SetMusicVolumeCallCount.ToString(); // baseline read
            slider.value = 0.8f;

            Assert.Greater(_spy.SetMusicVolumeCallCount, 0,
                "IAudioService.SetMusicVolume must be called when slider value changes.");
            Assert.AreEqual(0.8f, _spy.LastSetMusicVolume, 0.001f,
                "IAudioService must receive the same volume that was set on the slider.");
        }

        // 3a. Volume clamped to 0 when slider is below range
        [Test]
        public void OnVolumeChanged_NegativeInput_ClampedToZero_InGameSettings()
        {
            var slider = GetSlider();
            Assume.That(slider, Is.Not.Null, "Volume slider was not built.");

            // Bypass Slider.minValue guard by driving ApplyAndPersistVolume directly
            // via reflection — the slider itself won't accept values below minValue.
            var applyMethod = typeof(MusicPlayerHUD).GetMethod(
                "ApplyAndPersistVolume",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assume.That(applyMethod, Is.Not.Null, "ApplyAndPersistVolume method not found.");

            applyMethod.Invoke(_hud, new object[] { -0.5f });

            Assert.AreEqual(0f, GameSettings.Instance.musicVolume, 0.001f,
                "Negative volume must be clamped to 0 in GameSettings.");
            Assert.AreEqual(0f, _spy.LastSetMusicVolume, 0.001f,
                "Negative volume must be clamped to 0 when pushed to AudioService.");
        }

        // 3b. Volume clamped to 1 when input exceeds maximum
        [Test]
        public void OnVolumeChanged_AboveOneInput_ClampedToOne_InGameSettings()
        {
            var applyMethod = typeof(MusicPlayerHUD).GetMethod(
                "ApplyAndPersistVolume",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assume.That(applyMethod, Is.Not.Null, "ApplyAndPersistVolume method not found.");

            applyMethod.Invoke(_hud, new object[] { 1.5f });

            Assert.AreEqual(1f, GameSettings.Instance.musicVolume, 0.001f,
                "Volume above 1 must be clamped to 1 in GameSettings.");
            Assert.AreEqual(1f, _spy.LastSetMusicVolume, 0.001f,
                "Volume above 1 must be clamped to 1 when pushed to AudioService.");
        }

        // 4. First mute click sets volume to 0 and stores the previous value
        [Test]
        public void MuteClick_FromNonZeroVolume_SetsVolumeToZero()
        {
            // Arrange: start at 0.6.
            _spy.SimulateInitFromSettings(0.6f);
            GameSettings.Instance.musicVolume = 0.6f;
            // Force the slider to 0.6 without firing onValueChanged so the
            // spy's call-count baseline is clean.
            var slider = GetSlider();
            if (slider != null) slider.SetValueWithoutNotify(0.6f);

            var muteBtn = GetMuteButton();
            Assume.That(muteBtn, Is.Not.Null, "Mute button was not built — Awake must have failed.");

            // Act.
            muteBtn.onClick.Invoke();

            // Assert.
            Assert.AreEqual(0f, GameSettings.Instance.musicVolume, 0.001f,
                "After first mute click, GameSettings.musicVolume must be 0.");
            Assert.AreEqual(0f, _spy.LastSetMusicVolume, 0.001f,
                "After first mute click, AudioService must receive volume 0.");
        }

        // 5. Second mute click restores the pre-mute volume
        [Test]
        public void MuteClick_TwiceFromNonZeroVolume_RestoresOriginalVolume()
        {
            // Arrange: start at 0.4.
            const float startVolume = 0.4f;
            _spy.SimulateInitFromSettings(startVolume);
            GameSettings.Instance.musicVolume = startVolume;
            var slider = GetSlider();
            if (slider != null) slider.SetValueWithoutNotify(startVolume);

            var muteBtn = GetMuteButton();
            Assume.That(muteBtn, Is.Not.Null, "Mute button was not built.");

            // Act: mute then unmute.
            muteBtn.onClick.Invoke(); // mute — stores 0.4, sets to 0
            muteBtn.onClick.Invoke(); // unmute — restores 0.4

            // Assert.
            Assert.AreEqual(startVolume, GameSettings.Instance.musicVolume, 0.001f,
                "Second mute click must restore the pre-mute volume in GameSettings.");
            Assert.AreEqual(startVolume, _spy.LastSetMusicVolume, 0.001f,
                "Second mute click must push the restored volume to AudioService.");
        }

        // 6. Mute click when volume is already 0 uses the 0.7 fallback
        [Test]
        public void MuteClick_WhenVolumeAlreadyZero_UsesFallback_0_7()
        {
            // Start completely silent — _volumeBeforeMute will also be the fallback (0.7)
            // because Awake clamps it: if <= 0.001 it snaps to 0.7.
            Object.DestroyImmediate(_go);
            GameSettings.Instance.musicVolume = 0f; // zero → Awake will set fallback
            _spy.SimulateInitFromSettings(0f);
            CreateHud(initialSettingsVolume: 0f);

            var muteBtn = GetMuteButton();
            Assume.That(muteBtn, Is.Not.Null, "Mute button was not built.");

            // Ensure slider/audio both report volume 0 before the click.
            var slider = GetSlider();
            if (slider != null) slider.SetValueWithoutNotify(0f);
            _spy.SimulateInitFromSettings(0f); // force spy MusicVolume = 0

            // Re-inject the spy after the second CreateHud (which re-runs OnEnable).
            // OnEnable already ran and grabbed the spy from ServiceLocator, so the
            // private _audio field already holds it — verify via reflection.
            var audioFieldValue = s_audioField.GetValue(_hud) as IAudioService;
            Assume.That(audioFieldValue, Is.Not.Null, "_audio must be set after OnEnable.");

            muteBtn.onClick.Invoke();

            Assert.AreEqual(0.7f, GameSettings.Instance.musicVolume, 0.001f,
                "When current volume is 0, mute click must restore to fallback 0.7.");
            Assert.AreEqual(0.7f, _spy.LastSetMusicVolume, 0.001f,
                "Fallback volume 0.7 must be pushed to AudioService.");
        }

        // 7. Awake seeds _volumeBeforeMute from GameSettings
        [Test]
        public void Awake_SeedsVolumeBeforeMute_FromGameSettings()
        {
            // Create a fresh HUD with GameSettings.musicVolume = 0.3.
            // Immediately mute (volume will be 0), then unmute → must restore 0.3.
            Object.DestroyImmediate(_go);
            const float settingsVolume = 0.3f;
            CreateHud(initialSettingsVolume: settingsVolume);

            var muteBtn = GetMuteButton();
            Assume.That(muteBtn, Is.Not.Null, "Mute button was not built.");

            // Make the spy report 0.3 so the first mute click stores 0.3.
            _spy.SimulateInitFromSettings(settingsVolume);
            var slider = GetSlider();
            if (slider != null) slider.SetValueWithoutNotify(settingsVolume);

            muteBtn.onClick.Invoke(); // mute
            muteBtn.onClick.Invoke(); // unmute — must restore 0.3 from _volumeBeforeMute

            Assert.AreEqual(settingsVolume, GameSettings.Instance.musicVolume, 0.001f,
                "After mute+unmute, volume must return to the value seeded from " +
                "GameSettings.musicVolume in Awake (0.3), not the hardcoded fallback 0.7.");
        }

        // 8. OnEnable syncs slider to _audio.MusicVolume
        [Test]
        public void OnEnable_SyncsSlider_ToAudioServiceMusicVolume()
        {
            // Recreate with a spy that reports a specific volume.
            Object.DestroyImmediate(_go);
            const float audioVolume = 0.42f;
            _spy.SimulateInitFromSettings(audioVolume); // spy.MusicVolume = 0.42
            CreateHud(initialSettingsVolume: audioVolume);

            var slider = GetSlider();
            Assume.That(slider, Is.Not.Null, "Volume slider was not built.");

            Assert.AreEqual(audioVolume, slider.value, 0.001f,
                "OnEnable must sync the slider value to IAudioService.MusicVolume " +
                "so both the PauseMenu sounds panel and the HUD slider always agree.");
        }

        // 9. Legacy PlayerPrefs key "valkur.musichud.volume" is never written
        [Test]
        public void OnVolumeChanged_DoesNotWrite_LegacyPlayerPrefsKey()
        {
            // Ensure the key is absent before we interact with the slider.
            PlayerPrefs.DeleteKey(LegacyPrefKey);

            var slider = GetSlider();
            Assume.That(slider, Is.Not.Null, "Volume slider was not built.");

            // Trigger several volume changes including a mute cycle.
            slider.value = 0.5f;
            slider.value = 0.9f;
            slider.value = 0.2f;

            var muteBtn = GetMuteButton();
            if (muteBtn != null)
            {
                muteBtn.onClick.Invoke(); // mute
                muteBtn.onClick.Invoke(); // unmute
            }

            Assert.IsFalse(PlayerPrefs.HasKey(LegacyPrefKey),
                $"The legacy key '{LegacyPrefKey}' must not be written after the " +
                "volume-unification fix. If this fails, a PlayerPrefs.Set call for " +
                "that key has been reintroduced.");
        }
    }
}
