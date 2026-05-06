using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Pins the Wave B audio coverage: <see cref="CombatAudioSystem"/>
    /// listens for OnEntityDied / OnPlayerDied / OnLevelUp / OnItemPickedUp
    /// (in addition to the pre-existing OnEntityDamaged / OnHitDealt) and
    /// dispatches the right entry from <see cref="CombatSfxConfigSO"/>
    /// to <see cref="IAudioService"/>. Without these tests, future config
    /// edits could silently nuke the lifecycle SFX wiring.
    /// </summary>
    [TestFixture]
    public class CombatAudioCoverageTests
    {
        // ── Fake IAudioService that records each call ──────────────────────────

        private sealed class FakeAudioService : IAudioService
        {
            public readonly List<string> ById = new List<string>();
            public readonly List<string[]> Random = new List<string[]>();

            public void PlaySfxById(string sfxId, float volumeScale = 1f) { ById.Add(sfxId); }
            public void PlaySfxRandom(string[] sfxIds, float volumeScale = 1f) { Random.Add(sfxIds); }

            // ── Unused interface members (keep IAudioService happy) ────────────
            public void PlayMusic(AudioClip clip) {}
            public void PlayMusic(AudioClip clip, float fadeInSec) {}
            public void CrossfadeTo(AudioClip clip, float durationSec = 0.6f) {}
            public void PlayMusicByTrackId(string trackId, float fadeSec = -1f) {}
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
            public event System.Action<string, string, float, int> OnTrackChanged;
            public void OnZoneChanged(string zoneName, string levelName = null, string biomeName = null) {}
            public void EnterGameAudio() {}
            public void PlayMenuMusic() {}
            public void TransitionMenuToGame() {}
            public void ApplySettings() {}

            // Suppress "event never used" warning.
            public void RaiseTrackChanged() => OnTrackChanged?.Invoke(null, null, 0f, 0);
        }

        private GameObject _go;
        private CombatAudioSystem _system;
        private CombatSfxConfigSO _config;
        private FakeAudioService _audio;

        [SetUp]
        public void SetUp()
        {
            // Reset events between tests so listeners from earlier fixtures
            // don't leak into ours.
            GameEvents.Clear();

            _audio = new FakeAudioService();
            ServiceLocator.Register<IAudioService>(_audio);

            _config = ScriptableObject.CreateInstance<CombatSfxConfigSO>();
            _config.EditorSetNpcDeath(new[] { "npc_death_1", "npc_death_2" });
            _config.EditorSetPlayerDeath(new[] { "player_death" });
            _config.EditorSetLevelUp("level_up");
            _config.EditorSetItemPickup("item_pickup");

            _go = new GameObject("CombatAudioSystem");
            _system = _go.AddComponent<CombatAudioSystem>();
            _system.Initialize(_config);

            // EditMode AddComponent does not reliably fire OnEnable on the
            // newly-added MonoBehaviour, so the GameEvents subscriptions never
            // happen. Force the event-subscription side effect by invoking
            // OnEnable via reflection.
            var onEnable = typeof(CombatAudioSystem).GetMethod("OnEnable",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            onEnable.Invoke(_system, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            if (_config != null) Object.DestroyImmediate(_config);
            ServiceLocator.Unregister<IAudioService>();
            GameEvents.Clear();
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void NpcDeath_PlaysOneOfTheConfiguredDeathSfx()
        {
            var npcGo = new GameObject("Npc");
            try
            {
                GameEvents.FireEntityDied(npcGo, killer: null);
                Assert.AreEqual(1, _audio.Random.Count,
                    "NPC death must trigger exactly one PlaySfxRandom call.");
                CollectionAssert.AreEqual(_config.NpcDeathSfxIds, _audio.Random[0]);
            }
            finally { Object.DestroyImmediate(npcGo); }
        }

        [Test]
        public void PlayerDeath_DoesNotTriggerNpcDeathPool()
        {
            // The player has the "Player" tag; OnEntityDied must skip the NPC
            // pool so the player-specific sting isn't double-played.
            var playerGo = new GameObject("Player") { tag = "Player" };
            try
            {
                GameEvents.FireEntityDied(playerGo, killer: null);
                Assert.AreEqual(0, _audio.Random.Count,
                    "OnEntityDied must skip the NPC pool when the victim is the " +
                    "player to prevent double-playing the death sound.");
            }
            finally { Object.DestroyImmediate(playerGo); }
        }

        [Test]
        public void PlayerDeath_PlaysConfiguredPlayerDeathSfx()
        {
            GameEvents.FirePlayerDied();
            Assert.AreEqual(1, _audio.Random.Count);
            CollectionAssert.AreEqual(_config.PlayerDeathSfxIds, _audio.Random[0]);
        }

        [Test]
        public void LevelUp_PlaysSingleSfxId()
        {
            var entity = new GameObject("Player");
            try
            {
                GameEvents.FireLevelUp(entity, newLevel: 5);
                Assert.AreEqual(1, _audio.ById.Count,
                    "Level-up plays a single fanfare via PlaySfxById, not the random pool.");
                Assert.AreEqual("level_up", _audio.ById[0]);
            }
            finally { Object.DestroyImmediate(entity); }
        }

        [Test]
        public void ItemPickup_PlaysSingleSfxId()
        {
            var collector = new GameObject("Player");
            try
            {
                GameEvents.FireItemPickedUp(collector, "potion", 1);
                Assert.AreEqual(1, _audio.ById.Count);
                Assert.AreEqual("item_pickup", _audio.ById[0]);
            }
            finally { Object.DestroyImmediate(collector); }
        }

        [Test]
        public void EmptyConfigEntries_AreSilentNoOps()
        {
            // Wipe each lifecycle SFX so the config has nothing to play.
            _config.EditorSetNpcDeath(System.Array.Empty<string>());
            _config.EditorSetPlayerDeath(System.Array.Empty<string>());
            _config.EditorSetLevelUp(string.Empty);
            _config.EditorSetItemPickup(string.Empty);

            var npc = new GameObject("Npc");
            var player = new GameObject("Player") { tag = "Player" };
            try
            {
                GameEvents.FireEntityDied(npc, null);
                GameEvents.FirePlayerDied();
                GameEvents.FireLevelUp(player, 2);
                GameEvents.FireItemPickedUp(player, "coin", 1);

                Assert.AreEqual(0, _audio.ById.Count);
                Assert.AreEqual(0, _audio.Random.Count,
                    "Empty config entries must produce zero audio calls — never " +
                    "log warnings, never play empty arrays. Catalogs populate over " +
                    "time and unwired entries should stay silent.");
            }
            finally
            {
                Object.DestroyImmediate(npc);
                Object.DestroyImmediate(player);
            }
        }
    }
}
