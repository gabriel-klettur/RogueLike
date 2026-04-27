using NUnit.Framework;
using UnityEngine;
using Valkur.Infrastructure;
using Valkur.Data;
using Valkur.Gameplay.Enemies;

namespace Valkur.Tests.EditMode.Game
{
    /// <summary>
    /// Tests for MusicBeatClock + BossBeatChoreographer + BossBeatPattern.
    /// Uses DebugTick / DebugSetTrack helpers to avoid spinning up an AudioManager
    /// in EditMode (no real audio playback).
    /// </summary>
    public class MusicBeatTests
    {
        private GameObject _clockGo;
        private MusicBeatClock _clock;

        [SetUp]
        public void SetUp()
        {
            _clockGo = new GameObject("MusicBeatClockTest");
            _clock = _clockGo.AddComponent<MusicBeatClock>();
            // Awake already ran on AddComponent, but it has nothing fatal in EditMode
        }

        [TearDown]
        public void TearDown()
        {
            if (_clockGo != null) Object.DestroyImmediate(_clockGo);
        }

        [Test]
        public void BeatClock_120bpm_4_4_emits_one_beat_per_500ms()
        {
            _clock.DebugSetTrack("test", "Test Track", bpm: 120f, beatsPerBar: 4);

            int beatCount = 0;
            int lastBeatInBar = -1;
            int lastBar = -1;
            _clock.OnBeat += (idx, bib, bar) =>
            {
                beatCount++;
                lastBeatInBar = bib;
                lastBar = bar;
            };

            // 0s → no beats yet (we are AT beat 0 boundary; floor(0) = 0 → emits beat 0)
            _clock.DebugTick(0f);
            Assert.AreEqual(1, beatCount, "Beat 0 should fire at t=0");

            // 0.5s → beat 1
            _clock.DebugTick(0.5f);
            Assert.AreEqual(2, beatCount);
            Assert.AreEqual(1, lastBeatInBar);
            Assert.AreEqual(0, lastBar);

            // 2.0s → beats 2,3,4 catch up (0.5,0.5,0.5) → 5 total. beat 4 = bar 1 / beat 0
            _clock.DebugTick(2.0f);
            Assert.AreEqual(5, beatCount);
            Assert.AreEqual(0, lastBeatInBar);
            Assert.AreEqual(1, lastBar);
        }

        [Test]
        public void BeatClock_offset_skips_silent_intro()
        {
            _clock.DebugSetTrack("intro", "Intro", bpm: 60f, beatsPerBar: 4, offsetSec: 2f);

            int hits = 0;
            _clock.OnBeat += (_, _2, _3) => hits++;

            _clock.DebugTick(1.5f); // before offset
            Assert.AreEqual(0, hits);

            _clock.DebugTick(2.0f); // exactly first beat
            Assert.AreEqual(1, hits);

            _clock.DebugTick(3.0f); // +1 beat
            Assert.AreEqual(2, hits);
        }

        [Test]
        public void BeatClock_OnBar_fires_only_on_downbeat()
        {
            _clock.DebugSetTrack("test", "Test", bpm: 60f, beatsPerBar: 4);

            int bars = 0;
            _clock.OnBar += _ => bars++;

            _clock.DebugTick(0f);   // bar 0 downbeat
            _clock.DebugTick(1f);   // beat 1 (not a bar)
            _clock.DebugTick(2f);
            _clock.DebugTick(3f);
            Assert.AreEqual(1, bars);

            _clock.DebugTick(4f);   // bar 1 downbeat
            Assert.AreEqual(2, bars);
        }

        [Test]
        public void BeatClock_zero_bpm_emits_nothing()
        {
            _clock.DebugSetTrack("none", "None", bpm: 0f, beatsPerBar: 4);

            int hits = 0;
            _clock.OnBeat += (_, _2, _3) => hits++;
            _clock.DebugTick(10f);
            Assert.AreEqual(0, hits);
        }

        [Test]
        public void Choreographer_only_fires_cue_at_matching_bar_and_beat()
        {
            var pattern = ScriptableObject.CreateInstance<BossBeatPattern>();
            pattern.barsPerLoop = 2;
            pattern.cues.Add(new BossBeatPattern.Cue { bar = 0, beat = 0, action = "telegraph" });
            pattern.cues.Add(new BossBeatPattern.Cue { bar = 1, beat = 2, action = "smash", payload = 25f });

            var go = new GameObject("Boss");
            var chor = go.AddComponent<BossBeatChoreographer>();
            chor.Pattern = pattern;

            string lastAction = null;
            float lastPayload = 0f;
            int lastBar = -1, lastBeatInBar = -1;
            chor.OnCue.AddListener((act, pay, bib, bar) =>
            {
                lastAction = act; lastPayload = pay; lastBeatInBar = bib; lastBar = bar;
            });

            // Bar 0 / Beat 0 → telegraph
            chor.DebugForceBeat(0, 0, 0);
            Assert.AreEqual("telegraph", lastAction);
            Assert.AreEqual(0, lastBar);

            // Bar 1 / Beat 2 → smash
            chor.DebugForceBeat(6, 2, 1);
            Assert.AreEqual("smash", lastAction);
            Assert.AreEqual(25f, lastPayload, 0.001f);
            Assert.AreEqual(1, lastBar);

            // Bar 2 / Beat 0 → wraps to bar 0 (loop = 2) → telegraph
            lastAction = null;
            chor.DebugForceBeat(8, 0, 2);
            Assert.AreEqual("telegraph", lastAction);

            // Bar 0 / Beat 1 → no cue
            lastAction = null;
            chor.DebugForceBeat(1, 1, 0);
            Assert.IsNull(lastAction);

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(pattern);
        }

        [Test]
        public void Choreographer_inactive_emits_nothing()
        {
            var pattern = ScriptableObject.CreateInstance<BossBeatPattern>();
            pattern.cues.Add(new BossBeatPattern.Cue { bar = 0, beat = 0, action = "x" });

            var go = new GameObject("Boss");
            var chor = go.AddComponent<BossBeatChoreographer>();
            chor.Pattern = pattern;
            chor.Active = false;

            int hits = 0;
            chor.OnCue.AddListener((_, _2, _3, _4) => hits++);
            chor.DebugForceBeat(0, 0, 0);
            Assert.AreEqual(0, hits);

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(pattern);
        }
    }
}
