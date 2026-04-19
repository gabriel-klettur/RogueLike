using NUnit.Framework;
using Valkur.Core;

namespace Valkur.Tests.EditMode
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

        // ── Default keybinding values match Python input_config.py ──────

        [Test]
        public void DefaultPauseKey_IsEscape()
        {
            Assert.AreEqual("Escape", _settings.pauseKeyA);
        }

        [Test]
        public void DefaultMoveUpKeys_AreCorrect()
        {
            Assert.AreEqual("w", _settings.moveUpKeyA);
            Assert.AreEqual("UpArrow", _settings.moveUpKeyB);
        }

        [Test]
        public void DefaultMoveDownKeys_AreCorrect()
        {
            Assert.AreEqual("s", _settings.moveDownKeyA);
            Assert.AreEqual("DownArrow", _settings.moveDownKeyB);
        }

        [Test]
        public void DefaultMoveLeftKeys_AreCorrect()
        {
            Assert.AreEqual("a", _settings.moveLeftKeyA);
            Assert.AreEqual("LeftArrow", _settings.moveLeftKeyB);
        }

        [Test]
        public void DefaultMoveRightKeys_AreCorrect()
        {
            Assert.AreEqual("d", _settings.moveRightKeyA);
            Assert.AreEqual("RightArrow", _settings.moveRightKeyB);
        }

        [Test]
        public void DefaultDashKeys_AreCorrect()
        {
            Assert.AreEqual("RightCtrl", _settings.dashKeyA);
            Assert.AreEqual("RightShift", _settings.dashKeyB);
        }

        [Test]
        public void DefaultSpellKeys_Are1234()
        {
            Assert.AreEqual("1", _settings.spell1KeyA);
            Assert.AreEqual("2", _settings.spell2KeyA);
            Assert.AreEqual("3", _settings.spell3KeyA);
            Assert.AreEqual("4", _settings.spell4KeyA);
        }

        [Test]
        public void DefaultAttackMouse_LeftAndRight()
        {
            Assert.AreEqual("LeftButton", _settings.primaryAttackMouse);
            Assert.AreEqual("RightButton", _settings.secondaryAttackMouse);
        }

        [Test]
        public void DefaultEditorKeys_AreCorrect()
        {
            Assert.AreEqual("F8", _settings.toggleTileEditorKeyA);
            Assert.AreEqual("F11", _settings.toggleMapEditorKeyA);
        }

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
        public void ResetToDefaults_RestoresInput()
        {
            _settings.moveUpKeyA = "z";
            _settings.dashKeyA = "space";

            _settings.ResetToDefaults();

            Assert.AreEqual("w", _settings.moveUpKeyA);
            Assert.AreEqual("RightCtrl", _settings.dashKeyA);
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
            _settings.moveUpKeyA = "z";
            _settings.spell3KeyA = "q";

            string json = UnityEngine.JsonUtility.ToJson(_settings);
            var loaded = UnityEngine.JsonUtility.FromJson<GameSettings>(json);

            Assert.AreEqual(0.42f, loaded.musicVolume, 0.001f);
            Assert.AreEqual("z", loaded.moveUpKeyA);
            Assert.AreEqual("q", loaded.spell3KeyA);
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
