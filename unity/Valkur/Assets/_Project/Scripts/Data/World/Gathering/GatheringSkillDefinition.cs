using UnityEngine;

namespace Valkur.Data
{
    /// <summary>How a rhythm tap landed against the beat.</summary>
    public enum RhythmVerdict
    {
        /// <summary>Not a judged tap — the automatic swing, or the tap that STARTS tapping.</summary>
        None = 0,
        Perfect = 1,
        Good = 2,
        /// <summary>Before the window opened. A miss.</summary>
        Early = 3,
        /// <summary>After the window closed. A miss.</summary>
        Late = 4,
        /// <summary>A tap while the axe was still off balance from a miss.</summary>
        Staggered = 5,
    }

    /// <summary>How a node reads against the worker's skill. Drives the prompt and the gain.</summary>
    public enum GatheringEase
    {
        /// <summary>So far below the worker that working it teaches nothing more than the floor.</summary>
        Trivial = 0,
        Easy = 1,
        Suitable = 2,
        Hard = 3,
        VeryHard = 4,
    }

    /// <summary>
    /// One gathering skill — woodcutting today, mining and fishing on the same rules tomorrow —
    /// measured from 0.0 % to 100.0 %.
    ///
    /// <para><b>STORED IN TENTHS, NEVER AS A FLOAT.</b> A skill that climbs by 0.1 a gain would
    /// otherwise accumulate float error across thousands of gains, and a player at "99.99999" who
    /// can never display 100.0 is a bug report written by arithmetic. Every method here takes and
    /// returns tenths (0..1000); percent is a display conversion.</para>
    ///
    /// <para><b>ALL OF THE CURVE IS DATA AND PURE.</b> The gain chance, the efficiency and the
    /// bonus yield are functions of (skill, difficulty) with no Unity state, so the time it takes
    /// to reach 100 % is something a test can SIMULATE and pin, rather than a feeling somebody has
    /// after an afternoon of chopping.</para>
    ///
    /// <para><b>WHY A GAIN DEPENDS ON THE NODE.</b> A skill that rose at the same rate on any tree
    /// would be ground out on the one next to the spawn point. Tying the chance to how hard the
    /// node is against the worker makes progress a reason to go somewhere: past a margin a node
    /// becomes trivial and teaches only a floor, so the player is sent to the swamp, the snow and
    /// the old groves to keep climbing. The floor is not zero on purpose — a world that simply has
    /// no hard trees nearby must slow the player down, not stop them.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "GS_NewSkill", menuName = "Valkur/World/Gathering Skill")]
    public class GatheringSkillDefinition : ScriptableObject
    {
        public const int MaxTenths = 1000;

        [Header("Identity")]
        [Tooltip("Stable key a save names this skill by. Lowercase, no spaces. Never rename.")]
        public string skillKey = "woodcutting";

        [Tooltip("What the player reads: 'Tala', 'Minería'.")]
        public string displayName = "Tala";

        [Tooltip("The item families this skill uncovers as it grows. Shown by the skills panel.")]
        public GatheringYieldTable yieldTable;

        [Tooltip("The kinds of node that train this skill, so the skills panel can tell the player " +
                 "which ones suit them right now. Filled by the content seeder.")]
        public System.Collections.Generic.List<DestructionProfile> trainingNodes =
            new System.Collections.Generic.List<DestructionProfile>();

        [Header("Gain")]
        [Tooltip("Chance of a 0.1 % gain on one productive blow at 0 % skill, against a node of the " +
                 "right difficulty. Everything else multiplies this.")]
        [Range(0f, 1f)] public float gainBaseChance = 0.4f;

        [Tooltip("The chance falls as (1 - skill / divisor) ^ exponent. A divisor above 100 keeps " +
                 "the last percent reachable; the exponent is how steep the late climb is.")]
        [Min(100.5f)] public float gainDecayDivisor = 110f;

        [Min(0.1f)] public float gainDecayExponent = 2f;

        [Tooltip("Tenths granted per successful gain. 1 = 0.1 %.")]
        [Min(1)] public int gainTenths = 1;

        [Tooltip("A node stays fully instructive until the worker exceeds its difficulty by this " +
                 "many points.")]
        [Min(0f)] public float trivialMargin = 15f;

        [Tooltip("Past the margin, the chance fades over this many points down to the floor.")]
        [Min(1f)] public float trivialFade = 25f;

        [Tooltip("What a trivial node still teaches, as a fraction of a suitable one. Never 0: a " +
                 "world with no harder nodes nearby must slow the climb, not end it.")]
        [Range(0f, 1f)] public float trivialFloor = 0.25f;

        [Tooltip("A node harder than the worker by more than this many points is beyond them.")]
        [Min(0f)] public float hardMargin = 40f;

        [Tooltip("What a node far beyond the worker teaches, as a fraction. Less than a suitable " +
                 "one: flailing at an ancient trunk is not how anyone learns to fell a tree.")]
        [Range(0f, 1f)] public float hardFactor = 0.6f;

        [Tooltip("Multiplier on the chance while the blow is bare-handed or with the wrong tool.")]
        [Range(0f, 1f)] public float wrongToolGainFactor = 0.3f;

        [Header("Efficiency")]
        [Tooltip("Blow multiplier for a worker hopelessly below the node.")]
        [Min(0.05f)] public float efficiencyMin = 0.5f;

        [Tooltip("Blow multiplier for a worker far above the node.")]
        [Min(0.05f)] public float efficiencyMax = 1.6f;

        [Tooltip("Efficiency reaches its minimum when the worker is this many points below the node.")]
        [Min(0f)] public float efficiencyLowGap = 40f;

        [Tooltip("...and its maximum this many points above it.")]
        [Min(0f)] public float efficiencyHighGap = 40f;

        [Header("Rhythm (tapping)")]
        [Tooltip("Top tempo a beginner can tap, as a multiple of the automatic blow rate. Tapping on " +
                 "the beat is faster than letting the axe fall on its own, and how much faster is a " +
                 "thing the skill earns.")]
        [Min(1f)] public float rhythmTempoAtZero = 1.35f;

        [Tooltip("Top tempo a master can tap. 2 = twice the automatic rate.")]
        [Min(1f)] public float rhythmTempoAtMax = 2f;

        [Tooltip("Half-width of the hit window in seconds for a beginner — how far off the beat a tap " +
                 "may land and still strike.")]
        [Range(0.01f, 0.5f)] public float hitWindowAtZero = 0.055f;

        [Tooltip("Half-width of the hit window for a master.")]
        [Range(0.01f, 0.5f)] public float hitWindowAtMax = 0.15f;

        [Tooltip("The window never covers more than this fraction of a beat, whatever the skill, or " +
                 "the beat stops being a beat and tapping becomes mashing.")]
        [Range(0.1f, 0.5f)] public float hitWindowMaxOfBeat = 0.38f;

        [Tooltip("Within this fraction of the window a hit is PERFECT.")]
        [Range(0.05f, 1f)] public float perfectFractionOfWindow = 0.35f;

        [Tooltip("After a miss the axe is off balance for this fraction of a beat; taps inside it are " +
                 "misses too, which is what makes mashing strictly worse than waiting.")]
        [Range(0f, 2f)] public float missStaggerBeats = 0.7f;

        [Tooltip("Consecutive misses, or beats let pass untouched, before the session hands back to " +
                 "the automatic swing.")]
        [Min(1)] public int rhythmFallbackBeats = 3;

        [Header("Yield")]
        [Tooltip("One extra yield when a node is finished, for every this-many skill points.")]
        [Min(1f)] public float bonusYieldEverySkill = 34f;

        [Tooltip("A toast is shown each time the skill crosses a multiple of this many percent.")]
        [Min(1)] public int milestonePercent = 10;

        // ── Pure math ────────────────────────────────────────────────────────────

        public static float ToPercent(int tenths) => Mathf.Clamp(tenths, 0, MaxTenths) / 10f;

        public static string FormatPercent(int tenths) =>
            ToPercent(tenths).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%";

        /// <summary>Chance, 0..1, that one productive blow raises the skill.</summary>
        public float GainChance(int tenths, int difficulty, bool wrongTool)
        {
            if (tenths >= MaxTenths) return 0f;

            float skill = ToPercent(tenths);
            float decay = Mathf.Pow(Mathf.Max(0f, 1f - skill / gainDecayDivisor), gainDecayExponent);

            float band = 1f;
            float excess = skill - difficulty - trivialMargin;
            if (excess > 0f) band = Mathf.Max(trivialFloor, 1f - excess / trivialFade);
            if (difficulty - skill > hardMargin) band *= hardFactor;

            float chance = gainBaseChance * decay * band;
            if (wrongTool) chance *= wrongToolGainFactor;
            return Mathf.Clamp01(chance);
        }

        /// <summary>
        /// What the worker's skill does to the worth of a blow against a node of this difficulty.
        /// A beginner at an ancient trunk hits for half; a master at a sapling for well over full.
        /// </summary>
        public float EfficiencyMultiplier(int tenths, int difficulty)
        {
            float gap = ToPercent(tenths) - difficulty;
            float span = efficiencyLowGap + efficiencyHighGap;
            float t = span <= 0f ? 1f : Mathf.Clamp01((gap + efficiencyLowGap) / span);
            return Mathf.Lerp(efficiencyMin, efficiencyMax, t);
        }

        // ── Rhythm ───────────────────────────────────────────────────────────────

        /// <summary>How many times faster than the automatic swing a perfect tapper can chop.</summary>
        public float RhythmTempo(int tenths) =>
            Mathf.Lerp(rhythmTempoAtZero, rhythmTempoAtMax, ToPercent(tenths) / 100f);

        /// <summary>Seconds between beats while tapping, for a node whose automatic blow takes <paramref name="secondsPerBlow"/>.</summary>
        public float RhythmBeatSeconds(int tenths, float secondsPerBlow) =>
            Mathf.Max(0.05f, secondsPerBlow) / Mathf.Max(1f, RhythmTempo(tenths));

        /// <summary>Half-width of the hit window in seconds, capped to a fraction of the beat.</summary>
        public float HitWindowSeconds(int tenths, float beatSeconds)
        {
            float w = Mathf.Lerp(hitWindowAtZero, hitWindowAtMax, ToPercent(tenths) / 100f);
            return Mathf.Min(w, beatSeconds * hitWindowMaxOfBeat);
        }

        /// <summary>
        /// Judge a tap that landed <paramref name="offsetSeconds"/> from the beat (negative =
        /// early) against a window of half-width <paramref name="windowSeconds"/>.
        /// </summary>
        public RhythmVerdict Judge(float offsetSeconds, float windowSeconds)
        {
            float d = Mathf.Abs(offsetSeconds);
            if (d <= windowSeconds * perfectFractionOfWindow) return RhythmVerdict.Perfect;
            if (d <= windowSeconds) return RhythmVerdict.Good;
            return offsetSeconds < 0f ? RhythmVerdict.Early : RhythmVerdict.Late;
        }

        /// <summary>Extra yields when a node is finished. 0 at 0 %, 2 at 68 %.</summary>
        public int BonusYields(int tenths) =>
            Mathf.FloorToInt(ToPercent(tenths) / bonusYieldEverySkill);

        public GatheringEase Ease(int tenths, int difficulty)
        {
            float gap = ToPercent(tenths) - difficulty;
            if (gap > trivialMargin + trivialFade) return GatheringEase.Trivial;
            if (gap > trivialMargin) return GatheringEase.Easy;
            if (gap >= -15f) return GatheringEase.Suitable;
            if (gap >= -hardMargin) return GatheringEase.Hard;
            return GatheringEase.VeryHard;
        }

        public static string EaseLabel(GatheringEase ease)
        {
            switch (ease)
            {
                case GatheringEase.Trivial:  return "trivial, apenas enseña";
                case GatheringEase.Easy:     return "fácil";
                case GatheringEase.Suitable: return "adecuado";
                case GatheringEase.Hard:     return "difícil";
                default:                     return "muy difícil";
            }
        }

        /// <summary>
        /// Expected productive blows to go from <paramref name="fromTenths"/> to 100 % when every
        /// blow is against a node of <paramref name="difficultyForSkill"/>(skill). What the time
        /// test simulates; also what a designer can call from the console to see a retune.
        /// </summary>
        public double ExpectedBlowsToMax(int fromTenths, System.Func<float, int> difficultyForSkill)
        {
            double blows = 0d;
            for (int t = Mathf.Clamp(fromTenths, 0, MaxTenths); t < MaxTenths; t += Mathf.Max(1, gainTenths))
            {
                float chance = GainChance(t, difficultyForSkill(ToPercent(t)), false);
                if (chance <= 0f) return double.PositiveInfinity;
                blows += 1d / chance;
            }
            return blows;
        }
    }
}
