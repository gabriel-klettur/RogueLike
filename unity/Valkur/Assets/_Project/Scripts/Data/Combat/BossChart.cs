using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// What kind of action a <see cref="BossCue"/> performs when it fires.
    /// </summary>
    public enum BossCueType
    {
        CastSpell,
        PlaySfx,
        SwitchPhase,
        SpawnAdd,
        Taunt,
        PlayAnim,
    }

    /// <summary>
    /// How the dispatcher chooses the firing direction for a CastSpell cue.
    /// </summary>
    public enum BossCueTargeting
    {
        ToPlayer,
        Forward,
        Random8,
        LastDir,
    }

    /// <summary>
    /// A single beat-anchored event in a boss chart. A cue locates itself
    /// inside the chart's loop window via (bar, beat, beatFraction):
    ///   bar          ∈ [0, barsPerLoop)
    ///   beat         ∈ [0, beatsPerBar)
    ///   beatFraction ∈ [0, 1)   sub-beat for 1/8, 1/16 etc.
    ///
    /// The dispatcher resolves <see cref="targetKey"/> based on
    /// <see cref="type"/>:
    ///   CastSpell   → spellKey  (looked up in SpellCatalog)
    ///   PlaySfx     → sfxId
    ///   SwitchPhase → phase label (matched against BossPhaseController)
    ///   SpawnAdd    → monsterKey
    ///   Taunt/PlayAnim → animator trigger name
    /// </summary>
    [Serializable]
    public struct BossCue
    {
        public int bar;
        public int beat;
        [Range(0f, 1f)] public float beatFraction;
        public BossCueType type;
        public string targetKey;
        public BossCueTargeting targeting;
        public float payload;
        public string note;

        public float TotalBeats(int beatsPerBar)
        {
            return bar * Mathf.Max(1, beatsPerBar) + beat + Mathf.Clamp01(beatFraction);
        }
    }

    /// <summary>
    /// Authored rhythmic chart for a boss phase. Pairs a music track id with
    /// a list of <see cref="BossCue"/>s; the runtime dispatcher walks the
    /// list in lock-step with <c>MusicBeatClock</c> and fires actions when
    /// the song's (bar % barsPerLoop, beat) matches a cue.
    ///
    /// Charts are song-bound (one per <c>MusicTrackEntry.id</c>). A boss
    /// phase can hold multiple charts so the same boss attacks differently
    /// depending on which song is playing.
    ///
    /// Replaces <c>BossBeatPattern</c> (kept for legacy compat). Authoring
    /// happens in the in-game Boss Editor (button on General Editor).
    /// </summary>
    [CreateAssetMenu(fileName = "NewBossChart", menuName = "Valkur/Bosses/Boss Chart")]
    public sealed class BossChart : ScriptableObject
    {
        [Header("Music binding")]
        [Tooltip("MusicTrackEntry.id this chart targets. The chart only fires " +
                 "when the matching track is currently playing.")]
        public string musicTrackId;

        [Tooltip("Length of the looping window in bars. Cues are matched modulo " +
                 "this window so a 4-bar chart repeats every 4 bars of the song.")]
        [Min(1)] public int barsPerLoop = 4;

        [Header("Calibration")]
        [Tooltip("Extra lead-time (seconds) added before every cue fires. Use to " +
                 "compensate for audio output latency on a particular setup. " +
                 "Stacks with the per-spell prepareDuration auto-offset.")]
        public float globalLeadOffsetSec;

        [Header("Cues")]
        [Tooltip("Beat-anchored events. Order is irrelevant; the dispatcher " +
                 "scans the full list each beat.")]
        public List<BossCue> cues = new List<BossCue>();
    }
}
