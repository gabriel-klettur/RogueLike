using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data.Feel
{
    /// <summary>
    /// Every number the camera director reads.
    ///
    /// The system this replaces had its nine amplitudes written as literals at nine
    /// unrelated call sites, so tuning the feel of the game meant finding and editing nine
    /// files and two effects meant to hit equally hard could drift apart silently. Here a
    /// call site names a moment and this asset decides what that moment does.
    /// </summary>
    [CreateAssetMenu(fileName = "CameraFeelProfile", menuName = "Valkur/Camera/Feel Profile")]
    public sealed partial class CameraFeelProfile : ScriptableObject
    {
        // ── Follow ────────────────────────────────────────────────────────────

        [Header("Smooth follow")]
        [SerializeField, Tooltip("Spring frequency (rad/s) with which the camera chases the " +
            "player. Deliberately TIGHT rather than loose: a critically damped spring tracking " +
            "a walking player settles at a constant 2*speed/omega behind them, so softening " +
            "this does not read as smoother — it drags the camera backwards until it cancels " +
            "the forward lead and the frame trails the character. At 16 and 4 units/second the " +
            "anchor sits half a unit back. The softness belongs in the lead. 0 welds it.")]
        private float followOmega = 16f;

        [SerializeField, Tooltip("Maximum distance the smoothing is allowed to fall behind, " +
            "in world units. Past it the camera catches up rigidly, so a dash can never leave " +
            "the frame behind.")]
        private float maxFollowLagWu = 1.0f;

        [SerializeField, Tooltip("Once the anchor is within this many screen pixels of the " +
            "player it lands exactly on them and stops. A spring's tail is an infinite series " +
            "of ever-smaller steps, and CameraPixelSnap turns the last of them into a flicker " +
            "between two pixel rows rather than motion.")]
        private float followSettlePixels = 0.5f;

        // ── Lead ──────────────────────────────────────────────────────────────

        [Header("Lead")]
        [SerializeField, Tooltip("World units the camera leads in the movement direction at " +
            "full stick. The follow anchor sits about half a unit behind while walking, so " +
            "the net offset ahead of the character is roughly this minus that.")]
        private float moveLeadWu = 0.85f;

        [SerializeField, Tooltip("Aim lead while standing still. SHIPPED AT ZERO: the camera " +
            "follows the character, not the cursor. Raising it lets a standing player scan " +
            "the room with the mouse, at the cost of the frame appearing to argue with the " +
            "pointer during combat. Deliberately NOT scaled by movement speed when enabled.")]
        private float aimLeadIdleWu = 0f;

        [SerializeField, Tooltip("Aim lead while moving. SHIPPED AT ZERO, same reason. When " +
            "enabled it should stay below the idle value so movement dominates the frame.")]
        private float aimLeadMovingWu = 0f;

        [SerializeField, Tooltip("Hard clamp on the composed lead.")]
        private float maxLeadWu = 1.80f;

        [SerializeField, Tooltip("Aim lead is dropped entirely when the cursor is closer than " +
            "this. With the cursor on top of the player every direction is a fixed point, and " +
            "the camera would wander on noise.")]
        private float aimDeadzoneWu = 1.20f;

        [SerializeField, Tooltip("Lead spring frequency (rad/s), critically damped. This is " +
            "the knob that decides how gentle the motion feels: lower eases the camera into " +
            "and out of its lead instead of swinging to it. 4.5 takes about half a second.")]
        private float leadOmega = 4.5f;

        [SerializeField, Tooltip("Lead spring frequency during the death flow. The ghost " +
            "camera is heavy.")]
        private float leadOmegaHeavy = 3f;

        [SerializeField, Tooltip("The lead holds instead of creeping while the target is " +
            "within this many screen pixels. CameraPixelSnap rounds the final position, so " +
            "sub-pixel motion is not smooth — it is a flicker between two rows.")]
        private float leadDeadzonePixels = 0.75f;

        [SerializeField, Tooltip("Lead multiplier while the player is a spirit.")]
        private float spiritLeadScale = 0.35f;

        // ── Noise ─────────────────────────────────────────────────────────────

        [Header("Noise")]
        [SerializeField, Tooltip("Peak shake displacement at trauma 1. Amplitude is trauma " +
            "squared times this.")]
        private float maxShakeWu = 0.42f;

        [SerializeField, Tooltip("Scales Perlin's realised range to a peak of 1. MEASURED, " +
            "not chosen — pinned by CameraFeelNoiseCalibrationTests. Changing it silently " +
            "rescales every amplitude in this asset.")]
        private float noiseNormalisation = 1.2370f;

        [SerializeField, Tooltip("Trauma shed per second when no active cue asks for slower.")]
        private float defaultTraumaDecay = 1.8f;

        [SerializeField, Tooltip("Ceiling on trauma added in any rolling second. Beams and " +
            "cones report a hit per tick per victim; without a budget they pin the shake.")]
        private float maxTraumaPerSecond = 3f;

        // ── Classification ────────────────────────────────────────────────────

        [Header("Classification")]
        [SerializeField, Tooltip("A cast with at least this much prepareDuration is heavy.")]
        private float heavyPrepareSeconds = 0.20f;

        [SerializeField, Tooltip("A cast with at least this much cooldown is heavy.")]
        private float heavyCooldownSeconds = 3f;

        [SerializeField, Tooltip("A cast costing at least this much mana is heavy. Inert on " +
            "today's data — every spell costs 0-2 — and present so retuned costs work.")]
        private float heavyManaCost = 25f;

        [SerializeField, Tooltip("A melee swing that lands no hit within this window whiffed.")]
        private float whiffWindowSeconds = 0.22f;

        [SerializeField, Tooltip("Damage that maps to full cue intensity.")]
        private float damageReference = 40f;

        [SerializeField, Tooltip("Combo count at which the escalation saturates.")]
        private int comboCap = 8;

        [SerializeField, Tooltip("Extra trauma and kick at the combo cap.")]
        private float comboGain = 0.45f;

        [SerializeField, Tooltip("Fraction of max HP that maps to a full-severity hurt.")]
        private float severeDamageFraction = 0.25f;

        // ── Global ────────────────────────────────────────────────────────────

        [Header("Global")]
        [SerializeField, Range(0f, 1f), Tooltip("Global scale on all trauma and kick. Zero " +
            "disables the transient layer without a code change.")]
        private float masterIntensity01 = 1f;

        [SerializeField, Tooltip("A single-frame player move larger than this is a warp, not " +
            "movement. Chasing one drags the whole transient layer across the map.")]
        private float teleportThresholdWu = 6f;

        [SerializeField, Tooltip("Solver dt is clamped to this so an editor hitch cannot " +
            "produce a step nothing was designed for.")]
        private float maxStepSeconds = 0.05f;

        // ── Cues ──────────────────────────────────────────────────────────────

        [Header("Cues")]
        [SerializeField] private FeelCue attackConnect;
        [SerializeField] private FeelCue attackWhiff;
        [SerializeField] private FeelCue hurt;
        [SerializeField] private FeelCue castHeavy;
        [SerializeField] private FeelCue castPrepare;
        [SerializeField] private FeelCue dashLaunch;
        [SerializeField] private FeelCue dashLand;
        [SerializeField] private FeelCue impactLight;
        [SerializeField] private FeelCue impactMedium;
        [SerializeField] private FeelCue impactHeavy;
        [SerializeField] private FeelCue impactMassive;
        [SerializeField] private FeelCue death;
        [SerializeField] private FeelCue bossPhase;
        [SerializeField] private FeelCue levelUp;
        [SerializeField] private FeelCue comboPayoff;

        public float FollowOmega => followOmega;
        public float MaxFollowLagWu => maxFollowLagWu;
        public float FollowSettlePixels => followSettlePixels;
        public float MoveLeadWu => moveLeadWu;
        public float AimLeadIdleWu => aimLeadIdleWu;
        public float AimLeadMovingWu => aimLeadMovingWu;
        public float MaxLeadWu => maxLeadWu;
        public float AimDeadzoneWu => aimDeadzoneWu;
        public float LeadOmega => leadOmega;
        public float LeadOmegaHeavy => leadOmegaHeavy;
        public float LeadDeadzonePixels => leadDeadzonePixels;
        public float SpiritLeadScale => spiritLeadScale;
        public float MaxShakeWu => maxShakeWu;
        public float NoiseNormalisation => noiseNormalisation;
        public float DefaultTraumaDecay => defaultTraumaDecay;
        public float MaxTraumaPerSecond => maxTraumaPerSecond;
        public float HeavyPrepareSeconds => heavyPrepareSeconds;
        public float HeavyCooldownSeconds => heavyCooldownSeconds;
        public float HeavyManaCost => heavyManaCost;
        public float WhiffWindowSeconds => whiffWindowSeconds;
        public float DamageReference => damageReference;
        public int ComboCap => comboCap;
        public float ComboGain => comboGain;
        public float SevereDamageFraction => severeDamageFraction;
        public float MasterIntensity01 => masterIntensity01;
        public float TeleportThresholdWu => teleportThresholdWu;
        public float MaxStepSeconds => maxStepSeconds;

        /// <summary>
        /// The tuning for one beat. Never throws: an unknown id returns a zeroed record,
        /// which is a silent no-op rather than an exception in the middle of combat.
        /// </summary>
        public FeelCue GetCue(CameraFeelCue cue)
        {
            switch (cue)
            {
                case CameraFeelCue.AttackConnect: return attackConnect;
                case CameraFeelCue.AttackWhiff:   return attackWhiff;
                case CameraFeelCue.Hurt:          return hurt;
                case CameraFeelCue.CastHeavy:     return castHeavy;
                case CameraFeelCue.CastPrepare:   return castPrepare;
                case CameraFeelCue.DashLaunch:    return dashLaunch;
                case CameraFeelCue.DashLand:      return dashLand;
                case CameraFeelCue.ImpactLight:   return impactLight;
                case CameraFeelCue.ImpactMedium:  return impactMedium;
                case CameraFeelCue.ImpactHeavy:   return impactHeavy;
                case CameraFeelCue.ImpactMassive: return impactMassive;
                case CameraFeelCue.Death:         return death;
                case CameraFeelCue.BossPhase:     return bossPhase;
                case CameraFeelCue.LevelUp:       return levelUp;
                case CameraFeelCue.ComboPayoff:   return comboPayoff;
                default:                          return default;
            }
        }

        /// <summary>
        /// The shipped tuning, in code.
        ///
        /// A missing asset must degrade to a correctly tuned camera rather than to a dead
        /// one — a system whose failure mode is "nothing happens and nothing is logged" is
        /// exactly what this replaces.
        /// </summary>
        public static CameraFeelProfile CreateDefault()
        {
            var p = CreateInstance<CameraFeelProfile>();
            p.name = "CameraFeelProfile (code default)";
            p.ApplyDefaults();
            return p;
        }

        /// <summary>
        /// Amplitude floor: shake is trauma SQUARED times maxShakeWu, and CameraPixelSnap
        /// rounds the camera to the screen-pixel grid. At 0.42 max shake that makes any
        /// trauma below about 0.23 literally invisible — DashLaunch, LevelUp and ComboPayoff
        /// were authored under it and fired into nothing until
        /// CameraFeelProfileDefaultsTests caught them.
        ///
        /// Reading the cue table: damage DEALT is fast and critically damped and kicks toward
        /// the victim. Damage TAKEN is slow, overshoots once, kicks away from the attacker and
        /// freezes the lead so the frame stops anticipating and just absorbs it. Rewards have
        /// no kick at all — a reward that punches reads as damage.
        /// </summary>
        internal void ApplyDefaults()
        {
            followOmega = 16f;
            maxFollowLagWu = 1.0f;
            followSettlePixels = 0.5f;
            moveLeadWu = 0.85f;
            // Zero: the camera leads where the character is going, not where the pointer is.
            aimLeadIdleWu = 0f;
            aimLeadMovingWu = 0f;
            maxLeadWu = 1.80f;
            aimDeadzoneWu = 1.20f;
            leadOmega = 4.5f;
            leadOmegaHeavy = 3f;
            leadDeadzonePixels = 0.75f;
            spiritLeadScale = 0.35f;

            maxShakeWu = 0.42f;
            noiseNormalisation = 1.2370f;
            defaultTraumaDecay = 1.8f;
            maxTraumaPerSecond = 3f;

            heavyPrepareSeconds = 0.20f;
            heavyCooldownSeconds = 3f;
            heavyManaCost = 25f;
            whiffWindowSeconds = 0.22f;
            damageReference = 40f;
            comboCap = 8;
            comboGain = 0.45f;
            severeDamageFraction = 0.25f;

            masterIntensity01 = 1f;
            teleportThresholdWu = 6f;
            maxStepSeconds = 0.05f;

            //                     trauma decay  freq  kick  omega zeta  freeze stop  interval
            attackConnect = Cue(0.30f, 1.8f, 24f, 0.14f, 26f, 1.00f, 0f,    0.045f, 0.08f);
            attackWhiff   = Cue(0f,    1.8f, 0f,  0.05f, 30f, 1.00f, 0f,    0f,     0.15f);
            hurt          = Cue(0.28f, 1.3f, 13f, 0.20f, 15f, 0.65f, 0.20f, 0f,     0.12f);
            castHeavy     = Cue(0.10f, 2.0f, 18f, 0.12f, 18f, 1.00f, 0f,    0f,     0.10f);
            castPrepare   = Cue(0f,    1.8f, 0f,  0f,    0f,  1.00f, 0.60f, 0f,     0.10f);
            dashLaunch    = Cue(0.24f, 2.2f, 20f, 0f,    0f,  1.00f, 0f,    0f,     0.10f);
            dashLand      = Cue(0f,    1.8f, 0f,  0.07f, 24f, 0.90f, 0f,    0f,     0.10f);
            impactLight   = Cue(0.24f, 2.0f, 24f, 0.06f, 26f, 1.00f, 0f,    0f,     0.08f);
            impactMedium  = Cue(0.38f, 1.8f, 22f, 0.10f, 24f, 1.00f, 0f,    0f,     0.08f);
            impactHeavy   = Cue(0.52f, 1.6f, 20f, 0.16f, 22f, 1.00f, 0f,    0f,     0.10f);
            impactMassive = Cue(0.78f, 1.4f, 16f, 0.24f, 18f, 0.85f, 0.15f, 0.05f,  0.15f);
            death         = Cue(0.85f, 0.9f, 18f, 0f,    0f,  1.00f, 3.00f, 0f,     1.00f);
            bossPhase     = Cue(0.65f, 0.55f, 11f, 0f,   0f,  1.00f, 0.60f, 0.10f,  1.00f);
            levelUp       = Cue(0.25f, 1.2f, 9f,  0f,    0f,  1.00f, 0f,    0f,     0.50f);
            comboPayoff   = Cue(0.28f, 1.6f, 20f, 0f,    0f,  1.00f, 0f,    0f,     0.30f);
        }

        private static FeelCue Cue(float trauma, float decay, float freq, float kick,
                                   float omega, float zeta, float freeze, float stop,
                                   float interval) => new FeelCue
        {
            traumaAdd = trauma,
            traumaDecayPerSecond = decay,
            shakeFrequencyHz = freq,
            kickAmplitudeWu = kick,
            kickOmega = omega,
            kickZeta = zeta,
            leadFreezeSeconds = freeze,
            hitStopSeconds = stop,
            minIntervalSeconds = interval,
        };

        /// <summary>Every cue paired with its id, for tests that sweep the whole table.</summary>
        public IEnumerable<KeyValuePair<CameraFeelCue, FeelCue>> AllCues()
        {
            foreach (CameraFeelCue id in System.Enum.GetValues(typeof(CameraFeelCue)))
                yield return new KeyValuePair<CameraFeelCue, FeelCue>(id, GetCue(id));
        }
    }
}
