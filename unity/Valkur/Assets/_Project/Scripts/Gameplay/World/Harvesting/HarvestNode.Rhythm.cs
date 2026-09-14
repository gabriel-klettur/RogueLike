using System;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Interaction;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Chopping has two gears. Left alone the axe falls on its own clock. Tap the interact key on
    /// the beat and the worker chops faster — up to twice as fast for a master — as long as the
    /// taps land.
    ///
    /// <para><b>WHY IT IS WORTH HAVING.</b> An automatic shift is restful and accessible, and it
    /// stays exactly as it was: nobody is made to tap. What it cannot be is a thing a player gets
    /// GOOD at. The rhythm gear turns the same activity into one: the cross on the trunk already
    /// closes on the beat, so the cue was on screen before the mechanic existed.</para>
    ///
    /// <para><b>THE SKILL SETS THE DIFFICULTY, NOT JUST THE REWARD.</b> A beginner's top tempo is
    /// 1.35x and their window is narrow (±55 ms); a master's is 2x with a wider one. So a beginner
    /// who taps is barely faster and easily thrown off, and a master who taps is much faster and
    /// forgiving — "more skill, fewer and easier clicks" is the efficiency multiplier (fewer blows
    /// per log) plus the wider window (fewer misses). All of it is data on the skill definition.</para>
    ///
    /// <para><b>MASHING NEVER BEATS WAITING.</b> A miss is not a weaker blow, it is NO blow and a
    /// stagger: taps for most of a beat after it are misses too. The tap that starts tapping does not
    /// strike, abandoning the rhythm through misses locks it out until the next automatic blow, and
    /// that blow is never pulled forward — so hammering the key is at best exactly as fast as the
    /// automatic swing, and the only way to be faster is to actually keep time. Three misses — or three beats let pass untouched — hand
    /// the session back to the automatic swing on its own, so stopping tapping never stops the work.
    /// </para>
    ///
    /// <para><b>THE BEAT IS A GRID.</b> A hit advances the next beat from the beat it hit, not from
    /// the moment of the tap, so a player who is consistently 30 ms late stays 30 ms late rather than
    /// drifting further behind. A miss re-anchors the grid on the tap, which is what lets a thrown-off
    /// player find the beat again.</para>
    /// </summary>
    public partial class HarvestNode : IRhythmInteractable
    {
        private bool _rhythmMode;
        private float _nextBeatAt;
        private float _staggerUntil;
        private float _lastBlowAt = -999f;
        private int _missStreak;
        private int _hitStreak;

        /// <summary>
        /// Set when tapping was abandoned through MISSES, cleared by the next automatic blow. While
        /// set, a tap cannot re-enter the rhythm: otherwise mashing enters, misses out and re-enters
        /// every few frames, and the automatic swing it keeps interrupting never lands at all.
        /// </summary>
        private bool _rhythmLockedUntilAutoBlow;

        /// <summary>A tap was judged. Args: the verdict and the current hit streak.</summary>
        public event Action<RhythmVerdict, int> RhythmJudged;

        /// <summary>Test seam: replaces <c>Time.time</c> for the session and rhythm clocks.</summary>
        internal Func<float> ClockForTests;

        private float Now => ClockForTests != null ? ClockForTests() : Time.time;

        /// <summary>Whether the session is being driven by taps rather than by its own clock.</summary>
        public bool InRhythmMode => _sessionActive && _rhythmMode;

        /// <summary>Consecutive landed taps.</summary>
        public int RhythmStreak => _hitStreak;

        /// <summary>The verdict of the most recent tap. What the trunk mark colours its strike by.</summary>
        public RhythmVerdict LastVerdict { get; private set; }

        public bool AcceptsRhythmTaps =>
            _sessionActive && _profile != null && _profile.gatheringSkill != null
            && _profile.harvestMode == HarvestMode.Destroy;

        /// <summary>Seconds between beats for the current worker, or the automatic clock outside rhythm.</summary>
        public float BeatSeconds
        {
            get
            {
                float auto = _profile != null ? Mathf.Max(0.05f, _profile.secondsPerBlow) : 0.6f;
                if (_profile == null || _profile.gatheringSkill == null) return auto;
                return _profile.gatheringSkill.RhythmBeatSeconds(SkillTenthsOf(_worker), auto);
            }
        }

        /// <summary>Half-width of the current hit window in seconds. 0 outside rhythm.</summary>
        public float HitWindowSeconds =>
            _profile != null && _profile.gatheringSkill != null
                ? _profile.gatheringSkill.HitWindowSeconds(SkillTenthsOf(_worker), BeatSeconds)
                : 0f;

        /// <summary>True while a tap right now would strike.</summary>
        public bool InHitWindow => InRhythmMode && Now >= _staggerUntil &&
                                   Mathf.Abs(Now - _nextBeatAt) <= HitWindowSeconds;

        /// <summary>The window as a fraction of a beat, for the bar's target zone. Negative outside rhythm.</summary>
        public float HitWindow01 => InRhythmMode ? Mathf.Clamp01(HitWindowSeconds / Mathf.Max(0.0001f, BeatSeconds)) : -1f;

        public RhythmVerdict Tap(GameObject player)
        {
            if (!AcceptsRhythmTaps || player == null || player != _worker) return RhythmVerdict.None;

            float beat = BeatSeconds;
            float window = HitWindowSeconds;
            var skill = _profile.gatheringSkill;

            if (!_rhythmMode)
            {
                if (_rhythmLockedUntilAutoBlow) return RhythmVerdict.Staggered;

                // The tap that STARTS tapping only starts the metronome — it never strikes. A
                // striking first tap was measured to be an exploit: mashing entered, missed out and
                // re-entered every few frames, striking on each re-entry faster than the automatic
                // swing. The first beat lands one beat after the last blow.
                _rhythmMode = true;
                _missStreak = 0;
                _hitStreak = 0;
                _staggerUntil = 0f;
                _nextBeatAt = Mathf.Max(Now + window * 2f, _lastBlowAt + beat);

                LastVerdict = RhythmVerdict.None;
                RhythmJudged?.Invoke(RhythmVerdict.None, _hitStreak);
                return RhythmVerdict.None;
            }

            RhythmVerdict verdict;
            if (Now < _staggerUntil)
            {
                verdict = RhythmVerdict.Staggered;
            }
            else
            {
                verdict = skill.Judge(Now - _nextBeatAt, window);
            }

            if (verdict == RhythmVerdict.Perfect || verdict == RhythmVerdict.Good)
            {
                _missStreak = 0;
                _hitStreak++;
                LastVerdict = verdict;
                // Advance from the BEAT, not the tap: a steady offset stays steady.
                _nextBeatAt += beat;
                if (_nextBeatAt < Now) _nextBeatAt = Now + beat;
                LandBlow(verdict);
            }
            else
            {
                RegisterMiss(verdict, beat);
                // Re-anchor on the tap so a thrown-off player can find the beat again.
                _nextBeatAt = Now + beat;
            }

            RhythmJudged?.Invoke(verdict, _hitStreak);
            return verdict;
        }

        private void RegisterMiss(RhythmVerdict verdict, float beat)
        {
            LastVerdict = verdict;
            _hitStreak = 0;
            _missStreak++;
            _staggerUntil = Now + beat * _profile.gatheringSkill.missStaggerBeats;

            HarvestWorkMark.For(_worker)?.Miss(this);

            if (_missStreak >= Mathf.Max(1, _profile.gatheringSkill.rhythmFallbackBeats))
            {
                ExitRhythm();
                _rhythmLockedUntilAutoBlow = true;
            }
        }

        /// <summary>
        /// Called every session tick while tapping: a beat whose window closed untouched counts
        /// toward handing back to the automatic swing, silently — letting a beat go is not a mistake
        /// worth a red flash, it is how a player says "I have stopped tapping".
        /// </summary>
        private void TickRhythm()
        {
            float beat = BeatSeconds;
            float window = HitWindowSeconds;

            while (Now > _nextBeatAt + window)
            {
                _nextBeatAt += beat;
                _hitStreak = 0;
                _missStreak++;

                if (_missStreak < Mathf.Max(1, _profile.gatheringSkill.rhythmFallbackBeats)) continue;
                ExitRhythm();
                return;
            }
        }

        private void ExitRhythm()
        {
            _rhythmMode = false;
            _hitStreak = 0;
            _missStreak = 0;
            // The automatic swing picks up promptly rather than a full blow later, so handing back
            // reads as the axe carrying on — but NEVER earlier than it was already due. Pulling it
            // forward let mashing (enter, miss out) drag the automatic blow in ahead of its own
            // clock; with the MAX, abandoning a rhythm can only ever cost time, never buy it.
            float half = Mathf.Max(0.05f, _profile != null ? _profile.secondsPerBlow : 0.6f) * 0.5f;
            _nextBlowAt = Mathf.Max(_nextBlowAt, Now + half);
        }

        private void ResetRhythm()
        {
            _rhythmMode = false;
            _hitStreak = 0;
            _missStreak = 0;
            _staggerUntil = 0f;
            _rhythmLockedUntilAutoBlow = false;
            LastVerdict = RhythmVerdict.None;
        }

        /// <summary>The automatic swing landed: tapping may be tried again.</summary>
        private void NoteAutoBlow() => _rhythmLockedUntilAutoBlow = false;

        /// <summary>Test seam: one session frame, as Update would run it.</summary>
        internal void StepSessionForTests() => TickSession();
    }
}
