using UnityEngine;

namespace Valkur.Data
{
    /// <summary>How a node reads against the worker's skill. Drives the prompt and the gain.</summary>
    public enum SkillEase
    {
        /// <summary>So far below the worker that working it teaches nothing more than the floor.</summary>
        Trivial = 0,
        Easy = 1,
        Suitable = 2,
        Hard = 3,
        VeryHard = 4,
    }

    /// <summary>
    /// One skill the player climbs from 0.0 % to 100.0 % by DOING it: woodcutting, mining and
    /// fishing (gathering), cooking, blacksmithing and crafting (making).
    ///
    /// <para><b>ONE MODEL FOR EVERY TRADE.</b> The trades used to be a second system — levels
    /// 1..20 on <c>ProfessionDefinition</c>, raised only by crafting — beside this 0-100 % one.
    /// Two progressions for one idea meant two panels, two save formats and a skills screen that
    /// mixed "12.3 %" with "Nv 4". A trade is now a <see cref="SkillDefinition"/> with
    /// <see cref="category"/> = Crafting, and a profession only POINTS at the skill it trains.
    /// The gathering half (yield table, training nodes, rhythm) is simply unused by a crafting
    /// skill.</para>
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
    /// <para><b>WHY A GAIN DEPENDS ON THE NODE OR THE RECIPE.</b> A skill that rose at the same
    /// rate on any tree would be ground out on the one next to the spawn point. Tying the chance
    /// to how hard the work is against the worker — a node's difficulty, a recipe's required
    /// skill — makes progress a reason to go somewhere or cook something harder: past a margin
    /// the work becomes trivial and teaches only a floor. The floor is not zero on purpose.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "GS_NewSkill", menuName = "Valkur/Skills/Skill")]
    public class SkillDefinition : ScriptableObject
    {
        public const int MaxTenths = 1000;

        [Header("Identity")]
        [Tooltip("Stable key a save names this skill by. Lowercase, no spaces. Never rename.")]
        public string skillKey = "woodcutting";

        [Tooltip("What the player reads: 'Tala', 'Minería', 'Cocina'.")]
        public string displayName = "Tala";

        [Tooltip("Gathering (worked on nodes in the world) or Crafting (trained by recipes). " +
                 "Decides which group of the skills table the row sits in.")]
        public SkillCategory category = SkillCategory.Gathering;

        [Tooltip("One line for the skills panel: what this skill is and what raising it buys.")]
        [TextArea(2, 4)] public string description = "";

        [Tooltip("Colour of this skill's bar in the skills table.")]
        public Color accentColor = new Color(0.86f, 0.66f, 0.25f, 1f);

        [Tooltip("Order inside its group in the skills table. Ties fall back to catalog order.")]
        public int sortOrder;

        [Header("Gathering")]
        [Tooltip("The item families this skill uncovers as it grows. Shown by the skills panel. " +
                 "Empty for a crafting skill.")]
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
                 "the beat stops being a beat and tapping becomes mashing. Together with " +
                 "nearMissWindows it must leave a gap between one beat's late grace and the next " +
                 "beat's near zone: (1 + nearMissWindows) x this < 1.")]
        [Range(0.1f, 0.5f)] public float hitWindowMaxOfBeat = 0.25f;

        [Tooltip("Within this fraction of the window a cut is PERFECT — the centre of the target.")]
        [Range(0.05f, 1f)] public float perfectFractionOfWindow = 0.35f;

        [Tooltip("Within this fraction of the window a cut is GOOD. Between perfect and 1; the rest " +
                 "of the window is OK.")]
        [Range(0.1f, 1f)] public float goodFractionOfWindow = 0.65f;

        [Tooltip("Out to this many windows a cut is BAD; beyond it, out to nearMissWindows, AWFUL; " +
                 "past that, SOQUETE. Must sit between 1 and nearMissWindows.")]
        [Range(1f, 3f)] public float badWindows = 1.35f;

        [Header("Rhythm (what each cut is worth)")]
        [Tooltip("Blow multiplier of a PERFECT cut. Above 1: the centre of the target is the one " +
                 "thing that beats simply keeping time.")]
        [Range(0f, 2f)] public float cutWorthPerfect = 1.2f;

        [Tooltip("Blow multiplier of a GOOD cut.")]
        [Range(0f, 2f)] public float cutWorthGood = 1f;

        [Tooltip("Blow multiplier of an OK cut.")]
        [Range(0f, 2f)] public float cutWorthOk = 0.75f;

        [Tooltip("Blow multiplier of a BAD cut. Times the master's tempo it must stay under 1, or " +
                 "sloppy tapping out-chops letting the axe fall.")]
        [Range(0f, 2f)] public float cutWorthBad = 0.45f;

        [Tooltip("Blow multiplier of an AWFUL cut. A soquete is worth nothing: it never lands.")]
        [Range(0f, 2f)] public float cutWorthAwful = 0.2f;

        [Tooltip("Chances per beat for a beginner. 1 = one tap per blow: miss it and the blow is lost.")]
        [Range(1, 5)] public int rhythmTriesAtZero = 1;

        [Tooltip("Chances per beat for a master. A chance is only given back by a NEAR miss — a tap " +
                 "just before the window — so extra chances forgive a nervous finger and never a " +
                 "mashing one.")]
        [Range(1, 5)] public int rhythmTriesAtMax = 3;

        [Tooltip("The edge of the target, in multiples of the window's half-width: out to here a cut " +
                 "still lands (BAD, then AWFUL), and an EARLY one with chances left may be retried " +
                 "instead. Past it the cut is a SOQUETE and the beat is lost. Also the late grace " +
                 "after the beat in which a tap is graded rather than taken as the next beat.")]
        [Range(1f, 3f)] public float nearMissWindows = 1.8f;

        [Tooltip("Seconds after any tap during which further taps are ignored — the key-bounce / " +
                 "double-click guard, so one press can never spend two chances.")]
        [Range(0f, 0.3f)] public float tapCooldownSeconds = 0.08f;

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

        /// <summary>Chances per beat at this skill: 1 at 0 %, 2 at 50 %, 3 at 100 % with the defaults.</summary>
        public int RhythmTries(int tenths) =>
            Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(rhythmTriesAtZero, rhythmTriesAtMax, ToPercent(tenths) / 100f)));

        /// <summary>
        /// Whether a missed tap <paramref name="offsetSeconds"/> from the beat (negative = early)
        /// was NEAR enough to be retried: before the beat, and within
        /// <see cref="nearMissWindows"/> windows of it.
        /// </summary>
        public bool IsNearMiss(float offsetSeconds, float windowSeconds) =>
            offsetSeconds < 0f && -offsetSeconds <= windowSeconds * nearMissWindows;

        /// <summary>
        /// Grade a cut that landed <paramref name="offsetSeconds"/> from the beat (negative =
        /// early) against a window of half-width <paramref name="windowSeconds"/>. Symmetric: early
        /// and late are the same distance from the centre; which side is carried by the offset.
        /// </summary>
        public CutGrade GradeCut(float offsetSeconds, float windowSeconds)
        {
            if (windowSeconds <= 0f) return CutGrade.Soquete;
            float d = Mathf.Abs(offsetSeconds) / windowSeconds;
            for (var g = CutGrade.Perfect; g <= CutGrade.Awful; g++)
                if (d <= CutReachOfWindow(g)) return g;
            return CutGrade.Soquete;
        }

        /// <summary>
        /// How far out a grade's band reaches, in half-widths of the hit window. The target on the
        /// trunk and the bands on the bar are drawn from these same numbers, so what the player
        /// SEES as the centre is exactly what is graded as the centre. Soquete reaches past Awful
        /// without bound and answers Awful's reach.
        /// </summary>
        public float CutReachOfWindow(CutGrade grade)
        {
            float near = Mathf.Max(1f, nearMissWindows);
            float bad = Mathf.Clamp(badWindows, 1f, near);
            float perfect = Mathf.Clamp(perfectFractionOfWindow, 0.01f, 1f);
            float good = Mathf.Clamp(goodFractionOfWindow, perfect, 1f);
            switch (grade)
            {
                case CutGrade.Perfect: return perfect;
                case CutGrade.Good:    return good;
                case CutGrade.Ok:      return 1f;
                case CutGrade.Bad:     return bad;
                default:               return near;
            }
        }

        /// <summary>What a cut of this grade does to the blow. 1 for the automatic swing; 0 for a soquete.</summary>
        public float CutWorth(CutGrade grade)
        {
            switch (grade)
            {
                case CutGrade.None:    return 1f;
                case CutGrade.Perfect: return cutWorthPerfect;
                case CutGrade.Good:    return cutWorthGood;
                case CutGrade.Ok:      return cutWorthOk;
                case CutGrade.Bad:     return cutWorthBad;
                case CutGrade.Awful:   return cutWorthAwful;
                default:               return 0f;
            }
        }

        /// <summary>Whether a cut of this grade lands a blow at all.</summary>
        public static bool CutLands(CutGrade grade) => grade >= CutGrade.Perfect && grade <= CutGrade.Awful;

        /// <summary>Whether a cut of this grade keeps (and grows) the combo. Bad or worse breaks it.</summary>
        public static bool CutKeepsStreak(CutGrade grade) => grade >= CutGrade.Perfect && grade <= CutGrade.Ok;

        /// <summary>The word the player reads for a grade, without decoration.</summary>
        public static string CutLabel(CutGrade grade)
        {
            switch (grade)
            {
                case CutGrade.Perfect: return "CORTE PERFECTO";
                case CutGrade.Good:    return "CORTE BUENO";
                case CutGrade.Ok:      return "CORTE OK";
                case CutGrade.Bad:     return "CORTE MALO";
                case CutGrade.Awful:   return "CORTE PÉSIMO";
                case CutGrade.Soquete: return "CORTE SOQUETE";
                default:               return string.Empty;
            }
        }

        /// <summary>Extra yields when a node is finished. 0 at 0 %, 2 at 68 %.</summary>
        public int BonusYields(int tenths) =>
            Mathf.FloorToInt(ToPercent(tenths) / bonusYieldEverySkill);

        public SkillEase Ease(int tenths, int difficulty)
        {
            float gap = ToPercent(tenths) - difficulty;
            if (gap > trivialMargin + trivialFade) return SkillEase.Trivial;
            if (gap > trivialMargin) return SkillEase.Easy;
            if (gap >= -15f) return SkillEase.Suitable;
            if (gap >= -hardMargin) return SkillEase.Hard;
            return SkillEase.VeryHard;
        }

        public static string EaseLabel(SkillEase ease)
        {
            switch (ease)
            {
                case SkillEase.Trivial:  return "trivial, apenas enseña";
                case SkillEase.Easy:     return "fácil";
                case SkillEase.Suitable: return "adecuado";
                case SkillEase.Hard:     return "difícil";
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
