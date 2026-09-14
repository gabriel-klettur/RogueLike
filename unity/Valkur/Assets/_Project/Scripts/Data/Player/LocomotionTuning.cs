using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Every number of walking and running, at 0 % and at 100 % of the <c>athletics</c> skill
    /// ("Carrera"). The skill does not own a curve of its own: it only says how far along each
    /// pair the character is.
    ///
    /// <para><b>WHY UNDER <c>Resources/Skills/</c>.</b> Every reader is <c>AddComponent</c>-ed —
    /// <c>PlayerController</c>'s locomotion partial, the energy bar driver, the ground mark, the
    /// footstep emitter — so a <c>[SerializeField]</c> on any of them could never be filled. That
    /// is the <c>ChatSystem._catalog</c> defect, and <see cref="DeathTuning"/> answers it the same
    /// way. It sits in the Skills folder because it is the other half of the skill it tunes.</para>
    ///
    /// <para><b>THE PAIRS ARE THE DESIGN.</b> A beginner walks about three steps (0.9 s) before breaking
    /// into a run at the class's full speed and is winded after eleven seconds; a master breaks into
    /// a run on the first stride, runs 30 % faster and lasts twenty-five. Walking is 60 % of the
    /// class speed, so the art's short steps can land. Everything between is a lerp on the
    /// skill, so a retune is two numbers and a test re-run, never code.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "LocomotionTuning", menuName = "Valkur/Player/Locomotion Tuning")]
    public sealed class LocomotionTuning : ScriptableObject
    {
        public const string ResourcePath = "Skills/LocomotionTuning";

        /// <summary>The skill this tuning is the other half of.</summary>
        public const string SkillKey = "athletics";

        [Header("Impulso (caminar hasta correr)")]
        [Tooltip("Seconds of sustained walking before breaking into a run, at 0 % skill.")]
        [Min(0.05f)] public float startSecondsAtZero = 0.9f;

        [Tooltip("...and at 100 %.")]
        [Min(0.05f)] public float startSecondsAtMax = 0.3f;

        [Tooltip("Momentum only builds while the body REALLY moves: measured speed at least this " +
                 "fraction of the speed asked for. Walking into a wall builds nothing.")]
        [Range(0.1f, 1f)] public float minRealSpeedFraction = 0.65f;

        [Tooltip("Letting go of the keys for less than this does not break momentum — a tap between " +
                 "two directions is not a stop.")]
        [Range(0f, 0.5f)] public float stopGraceSeconds = 0.12f;

        [Tooltip("A change of heading up to this many degrees costs nothing, at 0 % skill.")]
        [Range(0f, 180f)] public float turnToleranceAtZero = 25f;

        [Tooltip("...and at 100 %.")]
        [Range(0f, 180f)] public float turnToleranceAtMax = 70f;

        [Tooltip("A turn this sharp or sharper throws all momentum away. Between the tolerance and " +
                 "this, the loss is proportional.")]
        [Range(1f, 180f)] public float turnBreakDegrees = 150f;

        [Tooltip("While running, momentum falling under this drops the body back to a walk.")]
        [Range(0f, 1f)] public float runExitMomentum = 0.55f;

        [Header("Velocidades (fracciones de la velocidad de clase)")]
        [Tooltip("Walking speed as a fraction of the class's MoveSpeed stat. The shipped art draws " +
                 "steps of 0.5-1 world units, and at the full stat speed a walk skated 3-10x " +
                 "(.github/LOCOMOTION_FOOT_SYNC_AUDIT_2026-09-14.md). Walking slower and RUNNING at " +
                 "the old speed is what lets the feet land where the body goes.")]
        [Range(0.2f, 1f)] public float walkSpeedFraction = 0.6f;

        [Tooltip("Run speed as a fraction of the class's MoveSpeed stat, at 0 % skill. 1 = the speed " +
                 "the character used to walk at.")]
        [Min(0.2f)] public float runMultiplierAtZero = 1.0f;

        [Tooltip("...and at 100 %.")]
        [Min(0.2f)] public float runMultiplierAtMax = 1.3f;

        [Tooltip("Backpedalling (moving away from where the character faces) as a fraction of the " +
                 "walking speed. Plays the walk cycle backwards and never builds momentum.")]
        [Range(0.2f, 1f)] public float backpedalSpeedFraction = 0.7f;

        [Tooltip("Degrees between the movement and the facing beyond which the body backpedals.")]
        [Range(90f, 180f)] public float backpedalAngle = 100f;

        [Tooltip("Seconds the speed takes to ease between walking and running, either way. No step.")]
        [Range(0.01f, 1f)] public float blendSeconds = 0.25f;

        [Header("Resistencia")]
        [Tooltip("Energy per second spent running, at 0 % skill.")]
        [Min(0f)] public float drainPerSecondAtZero = 9f;

        [Tooltip("...and at 100 %.")]
        [Min(0f)] public float drainPerSecondAtMax = 4f;

        [Tooltip("Energy per second regained while walking, at 0 % skill.")]
        [Min(0f)] public float regenWalkingAtZero = 6f;

        [Tooltip("...and at 100 %.")]
        [Min(0f)] public float regenWalkingAtMax = 10f;

        [Tooltip("Energy per second regained while standing still, at 0 % skill.")]
        [Min(0f)] public float regenIdleAtZero = 14f;

        [Tooltip("...and at 100 %.")]
        [Min(0f)] public float regenIdleAtMax = 22f;

        [Tooltip("Seconds after a run ends before energy starts to come back, at 0 % skill.")]
        [Min(0f)] public float regenDelayAtZero = 0.8f;

        [Tooltip("...and at 100 %.")]
        [Min(0f)] public float regenDelayAtMax = 0.4f;

        [Tooltip("Once winded, the body may not build momentum again until energy is back to this " +
                 "fraction. Hysteresis: without it an empty bar flickers between run and walk.")]
        [Range(0.05f, 1f)] public float windedRecoverFraction = 0.35f;

        [Tooltip("Max energy = offset + perDexterity x the class's maxDexterity (the stat the class " +
                 "selector has always called 'Resistencia' and nothing read).")]
        [Min(1f)] public float energyBaseOffset = 60f;

        [Min(0f)] public float energyPerDexterity = 0.4f;

        [Header("Animación")]
        [Tooltip("The speed the run cycle was drawn for, as a multiple of the class speed, when the " +
                 "sheet has no measured cycle (LocomotionCycleCatalog) and no authored reference.")]
        [Min(0.2f)] public float runReferenceFactor = 1.0f;

        [Tooltip("Playback rate is clamped to this range so a nudge against a wall does not freeze the " +
                 "cycle.")]
        [Range(0.1f, 1f)] public float locomotionRateMin = 0.5f;

        [Tooltip("Upper clamp for unmeasured cycles. Measured ones are capped in frames per second instead.")]
        [Range(1f, 4f)] public float locomotionRateMax = 1.5f;

        [Tooltip("The fastest a measured WALK cycle may play, frames per second. Past it the legs " +
                 "blur; what the cap does not absorb is residual skate.")]
        [Range(4f, 30f)] public float walkMaxFps = 14f;

        [Tooltip("The fastest a measured RUN cycle may play, frames per second.")]
        [Range(4f, 30f)] public float runMaxFps = 18f;

        [Tooltip("How much longer a FOOT CONTACT frame holds than the cycle's average, the rest of " +
                 "the loop sharing out the difference so the cycle keeps its length. The animator's " +
                 "oldest trick for weight: the foot lands and the body settles on it, the passing " +
                 "pose flies. 1 = every frame equal.")]
        [Range(1f, 2f)] public float contactHold = 1.3f;

        [Tooltip("Seconds the body takes to reach walking speed from a stand. Short enough to keep " +
                 "the controls instant, long enough that the first step is not a teleport.")]
        [Range(0f, 0.3f)] public float startEaseSeconds = 0.06f;

        [Tooltip("Seconds the body takes to stop when the keys are released. Matches the legs " +
                 "finishing their step: a body frozen while the legs still move reads as sliding.")]
        [Range(0f, 0.3f)] public float stopEaseSeconds = 0.1f;

        [Tooltip("When the keys are released mid-stride, the legs finish to the next foot contact " +
                 "(at most this many frames) before standing.")]
        [Range(0, 4)] public int settleMaxFrames = 2;

        [Header("Skill")]
        [Tooltip("One gain roll per this many world units REALLY covered while running.")]
        [Min(0.5f)] public float gainEveryRunDistance = 4f;

        [Tooltip("At most this many gain rolls in any rolling minute: a macro holding the keys in a " +
                 "field must not be the fastest way to a master's legs.")]
        [Min(1)] public int maxGainRollsPerMinute = 20;

        [Header("Mundo")]
        [Tooltip("A running footstep is this many times as loud as a walking one (NoiseEvents).")]
        [Min(1f)] public float runLoudnessMultiplier = 1.8f;

        [Tooltip("Speed lines appear only after this many seconds at full run.")]
        [Min(0f)] public float speedLinesAfterSeconds = 1.5f;

        // ── Pure maths ───────────────────────────────────────────────────────

        private static float L(float a, float b, float skill01) => Mathf.Lerp(a, b, Mathf.Clamp01(skill01));

        public float StartSeconds(float skill01)      => L(startSecondsAtZero, startSecondsAtMax, skill01);
        public float TurnTolerance(float skill01)     => L(turnToleranceAtZero, turnToleranceAtMax, skill01);
        public float RunMultiplier(float skill01)     => L(runMultiplierAtZero, runMultiplierAtMax, skill01);
        public float DrainPerSecond(float skill01)    => L(drainPerSecondAtZero, drainPerSecondAtMax, skill01);
        public float RegenWalking(float skill01)      => L(regenWalkingAtZero, regenWalkingAtMax, skill01);
        public float RegenIdle(float skill01)         => L(regenIdleAtZero, regenIdleAtMax, skill01);
        public float RegenDelay(float skill01)        => L(regenDelayAtZero, regenDelayAtMax, skill01);

        /// <summary>
        /// Steps of walking before a run, rounded for the ground mark's segments. The steps are the
        /// ones the DRAWING takes: at the walking speed (the class speed times
        /// <see cref="walkSpeedFraction"/>) a step every <paramref name="strideLength"/>, but never
        /// faster than a capped walk cycle can plant a foot (two steps per eight frames at
        /// <see cref="walkMaxFps"/>). With the defaults a beginner takes about three, a master one.
        /// </summary>
        public int StartSteps(float skill01, float classSpeed, float strideLength)
        {
            float walk = Mathf.Max(0.1f, classSpeed) * walkSpeedFraction;
            float stepsPerSecond = Mathf.Min(walk / Mathf.Max(0.05f, strideLength), walkMaxFps * 2f / 8f);
            return Mathf.Clamp(Mathf.RoundToInt(StartSeconds(skill01) * stepsPerSecond), 1, 8);
        }

        /// <summary>Seconds of running from full to empty, for the skills panel.</summary>
        public float RunEndurance(float skill01, float maxEnergy)
        {
            float drain = DrainPerSecond(skill01);
            return drain <= 0f ? float.PositiveInfinity : maxEnergy / drain;
        }

        /// <summary>Base max energy for a class, from its authored <c>maxDexterity</c>.</summary>
        public float MaxEnergyFor(float maxDexterity) =>
            energyBaseOffset + energyPerDexterity * Mathf.Max(0f, maxDexterity);

        // ── Resolution ───────────────────────────────────────────────────────

        private static LocomotionTuning s_cached;
        private static bool s_looked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_cached = null;
            s_looked = false;
        }

        /// <summary>The shipped tuning, or a throwaway instance carrying the defaults. Never null.</summary>
        public static LocomotionTuning Active
        {
            get
            {
                if (!s_looked)
                {
                    s_cached = Resources.Load<LocomotionTuning>(ResourcePath);
                    s_looked = true;
                }
                if (s_cached == null)
                {
                    s_cached = CreateInstance<LocomotionTuning>();
                    s_cached.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_cached;
            }
        }

        public static void InvalidateCache()
        {
            s_cached = null;
            s_looked = false;
        }
    }
}
