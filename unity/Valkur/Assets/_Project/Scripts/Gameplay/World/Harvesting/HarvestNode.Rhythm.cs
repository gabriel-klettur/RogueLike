using System;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Interaction;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Chopping has two gears. Left alone the axe falls on its own clock. Tap the interact key on
    /// the beat and the worker chops faster — up to twice as fast for a master — and every tap is
    /// GRADED by how close to the centre of the beat it landed.
    ///
    /// <para><b>WHY IT IS WORTH HAVING.</b> An automatic shift is restful and accessible, and it
    /// stays exactly as it was: nobody is made to tap. What it cannot be is a thing a player gets
    /// GOOD at. The rhythm gear turns the same activity into one: the cross on the trunk already
    /// closes on the beat, so the cue was on screen before the mechanic existed.</para>
    ///
    /// <para><b>SIX GRADES, NOT HIT-OR-MISS.</b> The distance from the beat, in half-widths of the
    /// skill's hit window, picks one band of a target (<see cref="SkillDefinition.GradeCut"/>):
    /// Perfect at the centre (a blow worth more than the automatic one), Good, Ok (the combo
    /// survives), Bad and Awful (a weak blow, combo broken) and, off the target, Soquete — no blow,
    /// beat lost, and a line telling the player what they are. What a cut is worth is DATA
    /// (<c>cutWorth*</c>), and a Bad cut at a master's tempo pays less than letting the axe fall, so
    /// tapping sloppily is never the fast way to fell a tree.</para>
    ///
    /// <para><b>CHANCES PER BLOW, EARNED BY SKILL.</b> An EARLY Bad or Awful cut with chances left
    /// is held back rather than landed: the axe does not fall, try again. A beginner has one chance
    /// (the early weak cut lands as it is); more skill gives more (1 at 0 %, 2 at 50 %, 3 at 100 %
    /// with the defaults). A late weak cut always lands — the beat has already passed.</para>
    ///
    /// <para><b>MASHING NEVER BEATS WAITING, AND THAT IS ARITHMETIC.</b> A beat resolves when its
    /// late grace ends, and the next beat's target opens only after a gap
    /// (<c>(1 + nearMissWindows) x hitWindowMaxOfBeat &lt; 1</c>). A masher's first tap after a beat
    /// resolves therefore lands OFF the target — a soquete — loses that beat, and every tap until
    /// the next resolution is ignored. Hammering the key yields no blows at all, three lost beats
    /// hand the session back to the automatic swing, and re-entry is locked until that swing lands.
    /// A debounce (<c>tapCooldownSeconds</c>) makes sure one press never spends two chances. The tap
    /// that starts tapping does not strike.</para>
    ///
    /// <para><b>THE BEAT IS A GRID.</b> A landed cut advances the next beat from the beat it cut,
    /// not from the moment of the tap, so a player who is consistently 30 ms late stays 30 ms late
    /// rather than drifting further behind. A retry or a soquete does not move the grid either.</para>
    /// </summary>
    public partial class HarvestNode : IRhythmInteractable
    {
        private bool _rhythmMode;
        private float _nextBeatAt;
        private float _lastBlowAt = -999f;
        private int _missStreak;
        private int _hitStreak;

        /// <summary>Taps held back on the CURRENT beat.</summary>
        private int _beatTriesUsed;

        /// <summary>The current beat is lost: taps are ignored until it resolves.</summary>
        private bool _beatLost;

        /// <summary>No tap counts before this time: the debounce.</summary>
        private float _tapReadyAt;

        /// <summary>
        /// Set when tapping was abandoned through soquetes, cleared by the next automatic blow. While
        /// set, a tap cannot re-enter the rhythm: otherwise mashing enters, misses out and re-enters
        /// every few frames, and the automatic swing it keeps interrupting never lands at all.
        /// </summary>
        private bool _rhythmLockedUntilAutoBlow;

        /// <summary>A tap was judged (anything but ignored).</summary>
        public event Action<RhythmTap> RhythmJudged;

        /// <summary>Test seam: replaces <c>Time.time</c> for the session and rhythm clocks.</summary>
        internal Func<float> ClockForTests;

        private float Now => ClockForTests != null ? ClockForTests() : Time.time;

        /// <summary>Whether the session is being driven by taps rather than by its own clock.</summary>
        public bool InRhythmMode => _sessionActive && _rhythmMode;

        /// <summary>Consecutive cuts graded Ok or better.</summary>
        public int RhythmStreak => _hitStreak;

        /// <summary>The most recent judged tap. What the trunk mark colours its strike by.</summary>
        public RhythmTap LastTap { get; private set; }

        /// <summary>The skill whose rhythm this node plays. Null for a node without one.</summary>
        public SkillDefinition RhythmSkill => _profile != null ? _profile.gatheringSkill : null;

        /// <summary>Chances this worker gets per beat at their skill.</summary>
        public int RhythmTriesPerBeat =>
            RhythmSkill != null ? RhythmSkill.RhythmTries(SkillTenthsOf(_worker)) : 1;

        /// <summary>Chances still left on the current beat. 0 once it is lost.</summary>
        public int RhythmTriesLeft => !InRhythmMode ? 0 : _beatLost ? 0 : Mathf.Max(0, RhythmTriesPerBeat - _beatTriesUsed);

        /// <summary>The current beat is lost and taps are being ignored. What the trunk mark dims on.</summary>
        public bool RhythmBeatLost => InRhythmMode && _beatLost;

        public bool AcceptsRhythmTaps =>
            _sessionActive && _profile != null && _profile.gatheringSkill != null
            && _profile.harvestMode == HarvestMode.Destroy;

        /// <summary>Seconds between beats for the current worker, or the automatic clock outside rhythm.</summary>
        public float BeatSeconds
        {
            get
            {
                float auto = _profile != null ? Mathf.Max(0.05f, _profile.secondsPerBlow) : 0.6f;
                if (RhythmSkill == null) return auto;
                return RhythmSkill.RhythmBeatSeconds(SkillTenthsOf(_worker), auto);
            }
        }

        /// <summary>Half-width of the current hit window (the Ok band) in seconds. 0 without a skill.</summary>
        public float HitWindowSeconds =>
            RhythmSkill != null ? RhythmSkill.HitWindowSeconds(SkillTenthsOf(_worker), BeatSeconds) : 0f;

        /// <summary>Seconds after the beat in which a tap is still graded rather than taken as the next beat.</summary>
        private float LateGraceSeconds =>
            HitWindowSeconds * (RhythmSkill != null ? RhythmSkill.nearMissWindows : 1f);

        /// <summary>Signed seconds from the current beat, negative = before it. 0 outside rhythm.</summary>
        public float SecondsFromBeat => InRhythmMode ? Now - _nextBeatAt : 0f;

        /// <summary>True while a tap right now would land Ok or better.</summary>
        public bool InHitWindow => InRhythmMode && !_beatLost && Mathf.Abs(Now - _nextBeatAt) <= HitWindowSeconds;

        public CutGrade GradeIfTappedNow =>
            InRhythmMode && !_beatLost && RhythmSkill != null
                ? RhythmSkill.GradeCut(Now - _nextBeatAt, HitWindowSeconds)
                : CutGrade.None;

        public float CutBandReach01(CutGrade grade)
        {
            if (!InRhythmMode || RhythmSkill == null) return -1f;
            float window01 = HitWindowSeconds / Mathf.Max(0.0001f, BeatSeconds);
            return Mathf.Clamp01(window01 * RhythmSkill.CutReachOfWindow(grade));
        }

        public RhythmTap Tap(GameObject player)
        {
            if (!AcceptsRhythmTaps || player == null || player != _worker) return RhythmTap.Ignored;

            // The debounce: one press, or a bouncing key, never spends two chances.
            if (Now < _tapReadyAt) return RhythmTap.Ignored;

            var skill = _profile.gatheringSkill;
            int streakBefore = _hitStreak;

            if (!_rhythmMode)
            {
                if (_rhythmLockedUntilAutoBlow) return RhythmTap.Ignored;
                _tapReadyAt = Now + skill.tapCooldownSeconds;

                // The tap that STARTS tapping only starts the metronome — it never strikes. A
                // striking first tap was measured to be an exploit: mashing entered, missed out and
                // re-entered every few frames, striking on each re-entry faster than the automatic
                // swing. The first beat lands one beat after the last blow.
                _rhythmMode = true;
                _missStreak = 0;
                _hitStreak = 0;
                StartBeat();
                _nextBeatAt = Mathf.Max(Now + HitWindowSeconds * (1f + skill.nearMissWindows) + 0.05f,
                                        _lastBlowAt + BeatSeconds);

                return Report(new RhythmTap(CutGrade.None, RhythmTapOutcome.Started, 0f, HitWindowSeconds,
                    RhythmTriesPerBeat, streakBefore, 0), player);
            }

            // A beat whose late grace already ended belongs to the past, whatever the frame order
            // between this tap and the session tick.
            ResolvePastBeats();
            if (!_rhythmMode || _beatLost) return RhythmTap.Ignored;

            _tapReadyAt = Now + skill.tapCooldownSeconds;

            float beat = BeatSeconds;
            float window = HitWindowSeconds;
            float offset = Now - _nextBeatAt;
            var grade = skill.GradeCut(offset, window);

            if (grade == CutGrade.Soquete)
            {
                LoseBeat();
                return Report(new RhythmTap(grade, RhythmTapOutcome.Lost, offset, window, 0, streakBefore, 0), player);
            }

            // An EARLY weak cut with chances left is held back: the axe does not fall, try again.
            // The streak survives, the trunk flinches.
            bool weak = !SkillDefinition.CutKeepsStreak(grade);
            if (weak && offset < 0f)
            {
                int triesLeft = RhythmTriesPerBeat - (_beatTriesUsed + 1);
                if (triesLeft > 0)
                {
                    _beatTriesUsed++;
                    HarvestWorkMark.For(_worker)?.Miss(this);
                    return Report(new RhythmTap(grade, RhythmTapOutcome.Retry, offset, window, triesLeft,
                        streakBefore, _hitStreak), player);
                }
            }

            // The cut lands, worth what its grade is worth.
            _missStreak = 0;
            _hitStreak = weak ? 0 : _hitStreak + 1;
            // Advance from the BEAT, not the tap: a steady offset stays steady.
            _nextBeatAt += beat;
            if (_nextBeatAt < Now) _nextBeatAt = Now + beat;
            StartBeat();

            // Read BEFORE the blow lands: the felling blow ends the session and resets the streak,
            // and the last cut of a tree is exactly the one whose combo should show.
            int streakAfter = _hitStreak;
            var tap = new RhythmTap(grade, RhythmTapOutcome.Landed, offset, window, 0, streakBefore, streakAfter);
            LastTap = tap;
            LandBlow(grade);
            return Report(tap, player);
        }

        private RhythmTap Report(RhythmTap tap, GameObject player)
        {
            LastTap = tap;
            RhythmJudged?.Invoke(tap);
            HarvestRhythmCallout.For(player)?.Judge(this, tap);
            HarvestWorkMark.For(player)?.Judged(this, tap);
            return tap;
        }

        private void StartBeat()
        {
            _beatTriesUsed = 0;
            _beatLost = false;
        }

        /// <summary>
        /// A soquete: no blow, streak broken, the trunk ignores taps until the beat resolves. Three
        /// lost beats in a row hand back to the automatic swing and lock re-entry until it lands.
        /// </summary>
        private void LoseBeat()
        {
            _beatLost = true;
            _hitStreak = 0;
            _missStreak++;

            HarvestWorkMark.For(_worker)?.Miss(this);

            if (_missStreak >= Mathf.Max(1, _profile.gatheringSkill.rhythmFallbackBeats))
            {
                ExitRhythm();
                _rhythmLockedUntilAutoBlow = true;
            }
        }

        /// <summary>
        /// Called every session tick while tapping: advance past every beat whose late grace ended.
        /// A beat let pass untouched (or held back and never landed) counts toward handing back to
        /// the automatic swing, silently — letting a beat go is not a mistake worth a red flash, it
        /// is how a player says "I have stopped tapping". A beat already LOST was counted when it was.
        /// </summary>
        private void TickRhythm() => ResolvePastBeats();

        private void ResolvePastBeats()
        {
            float beat = BeatSeconds;
            float grace = LateGraceSeconds;

            while (_rhythmMode && Now > _nextBeatAt + grace)
            {
                if (!_beatLost)
                {
                    _hitStreak = 0;
                    _missStreak++;
                }
                _nextBeatAt += beat;
                StartBeat();

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
            StartBeat();
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
            _tapReadyAt = 0f;
            StartBeat();
            _rhythmLockedUntilAutoBlow = false;
            LastTap = default;
        }

        /// <summary>The automatic swing landed: tapping may be tried again.</summary>
        private void NoteAutoBlow() => _rhythmLockedUntilAutoBlow = false;

        /// <summary>Test seam: one session frame, as Update would run it.</summary>
        internal void StepSessionForTests() => TickSession();
    }
}
