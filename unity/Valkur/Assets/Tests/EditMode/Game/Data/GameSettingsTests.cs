using NUnit.Framework;
using Valkur.Core;

namespace Valkur.Tests.EditMode.Game.Data
{
    public class GameSettingsTests
    {
        private GameSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = new GameSettings();
        }

        // ── Default values match Python audio_config.py ──────────────────

        [Test]
        public void DefaultMusicVolume_Matches_Python()
        {
            Assert.AreEqual(0.6f, _settings.musicVolume, 0.001f);
        }

        [Test]
        public void DefaultAmbientVolume_Matches_Python()
        {
            Assert.AreEqual(0.6f, _settings.ambientVolume, 0.001f);
        }

        [Test]
        public void DefaultSfxVolume_Matches_Python()
        {
            Assert.AreEqual(0.7f, _settings.sfxVolume, 0.001f);
        }

        [Test]
        public void DefaultDuckingAttenuation_Matches_Python()
        {
            Assert.AreEqual(-4.0f, _settings.duckingAttenuation, 0.001f);
        }

        [Test]
        public void DefaultDuckingHoldMs_Matches_Python()
        {
            Assert.AreEqual(250f, _settings.duckingHoldMs, 0.001f);
        }

        [Test]
        public void DefaultDuckingReleaseMs_Matches_Python()
        {
            Assert.AreEqual(200f, _settings.duckingReleaseMs, 0.001f);
        }

        [Test]
        public void DefaultAmbientMinInterval_Matches_Python()
        {
            Assert.AreEqual(6.0f, _settings.ambientMinInterval, 0.001f);
        }

        [Test]
        public void DefaultAmbientMaxInterval_Matches_Python()
        {
            Assert.AreEqual(18.0f, _settings.ambientMaxInterval, 0.001f);
        }

        // ── The keybinding default tests are GONE with the fields ────────
        //
        // Nine fixtures asserted that GameSettings.moveUpKeyA read "w", dashKeyA read
        // "RightCtrl", spell1..4KeyA read "1".."4", and so on. Every one of them passed for
        // the whole life of the project while NOTHING IN PRODUCTION READ THOSE FIELDS —
        // verified by grep: the only consumers were these tests. A green suite over an inert
        // model is worse than no suite, because it reports the controls as covered.
        //
        // Bindings live in Resources/Input/ValkurInputActions now, and what covers them is
        // ControlsBindingLayerTests (round trips, the legacy half moving with a rebind, the
        // Peace whitelist) plus InputServiceTests (asset/catalog coverage, unique ids).

        // ── ResetToDefaults ──────────────────────────────────────────────

        [Test]
        public void ResetToDefaults_RestoresAudio()
        {
            _settings.musicVolume = 0.1f;
            _settings.sfxVolume = 0.2f;
            _settings.ambientVolume = 0.3f;

            _settings.ResetToDefaults();

            Assert.AreEqual(0.6f, _settings.musicVolume, 0.001f);
            Assert.AreEqual(0.7f, _settings.sfxVolume, 0.001f);
            Assert.AreEqual(0.6f, _settings.ambientVolume, 0.001f);
        }

        [Test]
        public void ResetToDefaults_RestoresDucking()
        {
            _settings.duckingAttenuation = -10f;
            _settings.duckingHoldMs = 999f;
            _settings.duckingReleaseMs = 888f;

            _settings.ResetToDefaults();

            Assert.AreEqual(-4.0f, _settings.duckingAttenuation, 0.001f);
            Assert.AreEqual(250f, _settings.duckingHoldMs, 0.001f);
            Assert.AreEqual(200f, _settings.duckingReleaseMs, 0.001f);
        }

        // ── JSON roundtrip ───────────────────────────────────────────────

        [Test]
        public void JsonRoundtrip_PreservesModifiedValues()
        {
            _settings.musicVolume = 0.42f;
            _settings.resolutionWidth = 1280;

            string json = UnityEngine.JsonUtility.ToJson(_settings);
            var loaded = UnityEngine.JsonUtility.FromJson<GameSettings>(json);

            Assert.AreEqual(0.42f, loaded.musicVolume, 0.001f);
            Assert.AreEqual(1280, loaded.resolutionWidth);
        }

        [Test]
        public void JsonRoundtrip_PreservesAllAudioDefaults()
        {
            string json = UnityEngine.JsonUtility.ToJson(_settings);
            var loaded = UnityEngine.JsonUtility.FromJson<GameSettings>(json);

            Assert.AreEqual(_settings.musicVolume, loaded.musicVolume, 0.001f);
            Assert.AreEqual(_settings.ambientVolume, loaded.ambientVolume, 0.001f);
            Assert.AreEqual(_settings.sfxVolume, loaded.sfxVolume, 0.001f);
            Assert.AreEqual(_settings.ambientMinInterval, loaded.ambientMinInterval, 0.001f);
            Assert.AreEqual(_settings.ambientMaxInterval, loaded.ambientMaxInterval, 0.001f);
            Assert.AreEqual(_settings.duckingAttenuation, loaded.duckingAttenuation, 0.001f);
            Assert.AreEqual(_settings.duckingHoldMs, loaded.duckingHoldMs, 0.001f);
            Assert.AreEqual(_settings.duckingReleaseMs, loaded.duckingReleaseMs, 0.001f);
        }
    }
}
