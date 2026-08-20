using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data.Feel
{
    /// <summary>
    /// Addressable access to the profile's global numbers, so the Camera Editor can build
    /// its whole UI from a table instead of from twenty-five hand-written rows.
    ///
    /// The ranges live here rather than in the editor because they are a property of the
    /// data — a slider whose maximum is chosen in the UI layer is a limit nobody reading the
    /// profile can see.
    /// </summary>
    public sealed partial class CameraFeelProfile
    {
        /// <summary>
        /// Every tunable with its label, range and help text. Ordered as the editor should
        /// present them.
        /// </summary>
        public static IReadOnlyList<CameraFeelTunableInfo> Tunables => _tunables;

        [Valkur.Core.SelfHealingStatic("Immutable table built once from string and float " +
            "literals. Holds no Unity objects and is never written to, so it cannot go stale " +
            "across a Play session.")]
        private static readonly CameraFeelTunableInfo[] _tunables =
        {
            // ── Follow ────────────────────────────────────────────────────────
            new CameraFeelTunableInfo(CameraFeelTunable.FollowOmega, CameraFeelGroup.Follow,
                "Follow spring", 0f, 30f, "",
                "How tightly the camera chases the player. A spring tracking a walker settles " +
                "2*speed/omega behind them, so LOWERING this does not read as smoother — it " +
                "drags the frame backwards until it cancels the lead. 0 welds the camera."),

            new CameraFeelTunableInfo(CameraFeelTunable.MaxFollowLagWu, CameraFeelGroup.Follow,
                "Max lag", 0.1f, 4f, " wu",
                "Hard leash. Past this the camera catches up rigidly, so a dash cannot leave " +
                "the frame behind."),

            new CameraFeelTunableInfo(CameraFeelTunable.FollowSettlePixels, CameraFeelGroup.Follow,
                "Settle", 0f, 3f, " px",
                "Within this many screen pixels the anchor lands exactly and stops. A spring's " +
                "tail plus the pixel snap is a flicker between two rows, not slow arrival."),

            // ── Lead ──────────────────────────────────────────────────────────
            new CameraFeelTunableInfo(CameraFeelTunable.MoveLeadWu, CameraFeelGroup.Lead,
                "Move lead", 0f, 4f, " wu",
                "How far ahead of the character the camera looks at full stick."),

            new CameraFeelTunableInfo(CameraFeelTunable.LeadOmega, CameraFeelGroup.Lead,
                "Lead spring", 1f, 15f, "",
                "The knob that decides how gentle the motion feels. Lower eases the camera " +
                "into and out of its lead instead of swinging to it."),

            new CameraFeelTunableInfo(CameraFeelTunable.AimLeadIdleWu, CameraFeelGroup.Lead,
                "Aim lead (idle)", 0f, 3f, " wu",
                "Lead toward the cursor while standing still. SHIPPED AT ZERO: the camera " +
                "follows the character, not the pointer."),

            new CameraFeelTunableInfo(CameraFeelTunable.AimLeadMovingWu, CameraFeelGroup.Lead,
                "Aim lead (moving)", 0f, 3f, " wu",
                "Lead toward the cursor while moving. Shipped at zero, same reason."),

            new CameraFeelTunableInfo(CameraFeelTunable.MaxLeadWu, CameraFeelGroup.Lead,
                "Max lead", 0.1f, 6f, " wu",
                "Hard clamp on the composed lead."),

            new CameraFeelTunableInfo(CameraFeelTunable.AimDeadzoneWu, CameraFeelGroup.Lead,
                "Aim deadzone", 0f, 4f, " wu",
                "Aim lead switches off inside this radius. With the cursor on the player every " +
                "direction is a fixed point and the camera would wander on noise."),

            new CameraFeelTunableInfo(CameraFeelTunable.LeadDeadzonePixels, CameraFeelGroup.Lead,
                "Lead deadzone", 0f, 4f, " px",
                "The lead holds rather than creeping inside this many screen pixels."),

            new CameraFeelTunableInfo(CameraFeelTunable.LeadOmegaHeavy, CameraFeelGroup.Lead,
                "Lead spring (death)", 0.5f, 10f, "",
                "Lead spring while the death flow is running. The ghost camera is heavy."),

            new CameraFeelTunableInfo(CameraFeelTunable.SpiritLeadScale, CameraFeelGroup.Lead,
                "Spirit lead", 0f, 1f, "x",
                "Lead multiplier while the player is a spirit."),

            // ── Shake ─────────────────────────────────────────────────────────
            new CameraFeelTunableInfo(CameraFeelTunable.MaxShakeWu, CameraFeelGroup.Shake,
                "Max shake", 0f, 1.5f, " wu",
                "Peak displacement at trauma 1. Amplitude is trauma SQUARED times this, so " +
                "halving the trauma quarters the shake."),

            new CameraFeelTunableInfo(CameraFeelTunable.DefaultTraumaDecay, CameraFeelGroup.Shake,
                "Trauma decay", 0.1f, 6f, "/s",
                "How fast the shake dies when no cue asks for slower."),

            new CameraFeelTunableInfo(CameraFeelTunable.MaxTraumaPerSecond, CameraFeelGroup.Shake,
                "Trauma budget", 0.5f, 10f, "/s",
                "Ceiling on trauma added per rolling second. Beams and cones report a hit per " +
                "tick per victim; without this they pin the screen at full shake."),

            // ── Global ────────────────────────────────────────────────────────
            new CameraFeelTunableInfo(CameraFeelTunable.MasterIntensity01, CameraFeelGroup.Global,
                "Master", 0f, 1f, "x",
                "Scales all trauma and kick. Zero disables the whole transient layer, leaving " +
                "follow and lead — the fastest way to judge the movement on its own."),

            new CameraFeelTunableInfo(CameraFeelTunable.TeleportThresholdWu, CameraFeelGroup.Global,
                "Warp threshold", 1f, 20f, " wu",
                "A single-frame move larger than this is treated as a teleport and clears every " +
                "transient rather than being chased."),

            new CameraFeelTunableInfo(CameraFeelTunable.MaxStepSeconds, CameraFeelGroup.Global,
                "Max step", 0.01f, 0.2f, " s",
                "Solver dt clamp, so an editor hitch cannot produce a step nothing was designed for."),

            // ── Classification ────────────────────────────────────────────────
            new CameraFeelTunableInfo(CameraFeelTunable.HeavyPrepareSeconds, CameraFeelGroup.Classification,
                "Heavy wind-up", 0f, 1f, " s",
                "A cast with at least this much prepare time counts as heavy and moves the camera."),

            new CameraFeelTunableInfo(CameraFeelTunable.HeavyCooldownSeconds, CameraFeelGroup.Classification,
                "Heavy cooldown", 0.5f, 10f, " s",
                "A cast with at least this much cooldown counts as heavy."),

            new CameraFeelTunableInfo(CameraFeelTunable.WhiffWindowSeconds, CameraFeelGroup.Classification,
                "Whiff window", 0.05f, 1f, " s",
                "A melee swing that lands nothing inside this window is a whiff."),

            new CameraFeelTunableInfo(CameraFeelTunable.DamageReference, CameraFeelGroup.Classification,
                "Damage ref", 5f, 200f, "",
                "Damage that maps to a full-intensity hit cue."),

            new CameraFeelTunableInfo(CameraFeelTunable.ComboGain, CameraFeelGroup.Classification,
                "Combo gain", 0f, 2f, "x",
                "Extra trauma and kick at the combo cap."),

            new CameraFeelTunableInfo(CameraFeelTunable.SevereDamageFraction, CameraFeelGroup.Classification,
                "Severe damage", 0.05f, 1f, " of HP",
                "Fraction of the health bar that maps to a full-severity hurt. Expressed as a " +
                "fraction so a hit reads the same at 40 max HP and at 400."),
        };

        public static CameraFeelTunableInfo GetInfo(CameraFeelTunable id)
        {
            for (int i = 0; i < _tunables.Length; i++)
                if (_tunables[i].Id == id) return _tunables[i];
            return default;
        }

        public float GetTunable(CameraFeelTunable id)
        {
            switch (id)
            {
                case CameraFeelTunable.FollowOmega:          return followOmega;
                case CameraFeelTunable.MaxFollowLagWu:       return maxFollowLagWu;
                case CameraFeelTunable.FollowSettlePixels:   return followSettlePixels;
                case CameraFeelTunable.MoveLeadWu:           return moveLeadWu;
                case CameraFeelTunable.AimLeadIdleWu:        return aimLeadIdleWu;
                case CameraFeelTunable.AimLeadMovingWu:      return aimLeadMovingWu;
                case CameraFeelTunable.MaxLeadWu:            return maxLeadWu;
                case CameraFeelTunable.AimDeadzoneWu:        return aimDeadzoneWu;
                case CameraFeelTunable.LeadOmega:            return leadOmega;
                case CameraFeelTunable.LeadOmegaHeavy:       return leadOmegaHeavy;
                case CameraFeelTunable.LeadDeadzonePixels:   return leadDeadzonePixels;
                case CameraFeelTunable.SpiritLeadScale:      return spiritLeadScale;
                case CameraFeelTunable.MaxShakeWu:           return maxShakeWu;
                case CameraFeelTunable.DefaultTraumaDecay:   return defaultTraumaDecay;
                case CameraFeelTunable.MaxTraumaPerSecond:   return maxTraumaPerSecond;
                case CameraFeelTunable.MasterIntensity01:    return masterIntensity01;
                case CameraFeelTunable.TeleportThresholdWu:  return teleportThresholdWu;
                case CameraFeelTunable.MaxStepSeconds:       return maxStepSeconds;
                case CameraFeelTunable.HeavyPrepareSeconds:  return heavyPrepareSeconds;
                case CameraFeelTunable.HeavyCooldownSeconds: return heavyCooldownSeconds;
                case CameraFeelTunable.WhiffWindowSeconds:   return whiffWindowSeconds;
                case CameraFeelTunable.DamageReference:      return damageReference;
                case CameraFeelTunable.ComboGain:            return comboGain;
                case CameraFeelTunable.SevereDamageFraction: return severeDamageFraction;
                default:                                     return 0f;
            }
        }

        /// <summary>Writes a tunable, clamped to its declared range.</summary>
        public void SetTunable(CameraFeelTunable id, float value)
        {
            CameraFeelTunableInfo info = GetInfo(id);
            float v = Mathf.Clamp(value, info.Min, info.Max);

            switch (id)
            {
                case CameraFeelTunable.FollowOmega:          followOmega = v; break;
                case CameraFeelTunable.MaxFollowLagWu:       maxFollowLagWu = v; break;
                case CameraFeelTunable.FollowSettlePixels:   followSettlePixels = v; break;
                case CameraFeelTunable.MoveLeadWu:           moveLeadWu = v; break;
                case CameraFeelTunable.AimLeadIdleWu:        aimLeadIdleWu = v; break;
                case CameraFeelTunable.AimLeadMovingWu:      aimLeadMovingWu = v; break;
                case CameraFeelTunable.MaxLeadWu:            maxLeadWu = v; break;
                case CameraFeelTunable.AimDeadzoneWu:        aimDeadzoneWu = v; break;
                case CameraFeelTunable.LeadOmega:            leadOmega = v; break;
                case CameraFeelTunable.LeadOmegaHeavy:       leadOmegaHeavy = v; break;
                case CameraFeelTunable.LeadDeadzonePixels:   leadDeadzonePixels = v; break;
                case CameraFeelTunable.SpiritLeadScale:      spiritLeadScale = v; break;
                case CameraFeelTunable.MaxShakeWu:           maxShakeWu = v; break;
                case CameraFeelTunable.DefaultTraumaDecay:   defaultTraumaDecay = v; break;
                case CameraFeelTunable.MaxTraumaPerSecond:   maxTraumaPerSecond = v; break;
                case CameraFeelTunable.MasterIntensity01:    masterIntensity01 = v; break;
                case CameraFeelTunable.TeleportThresholdWu:  teleportThresholdWu = v; break;
                case CameraFeelTunable.MaxStepSeconds:       maxStepSeconds = v; break;
                case CameraFeelTunable.HeavyPrepareSeconds:  heavyPrepareSeconds = v; break;
                case CameraFeelTunable.HeavyCooldownSeconds: heavyCooldownSeconds = v; break;
                case CameraFeelTunable.WhiffWindowSeconds:   whiffWindowSeconds = v; break;
                case CameraFeelTunable.DamageReference:      damageReference = v; break;
                case CameraFeelTunable.ComboGain:            comboGain = v; break;
                case CameraFeelTunable.SevereDamageFraction: severeDamageFraction = v; break;
            }
        }

        /// <summary>Overwrites one cue's tuning. Used by the Camera Editor's cue panel.</summary>
        public void SetCue(CameraFeelCue cue, FeelCue value)
        {
            switch (cue)
            {
                case CameraFeelCue.AttackConnect: attackConnect = value; break;
                case CameraFeelCue.AttackWhiff:   attackWhiff = value; break;
                case CameraFeelCue.Hurt:          hurt = value; break;
                case CameraFeelCue.CastHeavy:     castHeavy = value; break;
                case CameraFeelCue.CastPrepare:   castPrepare = value; break;
                case CameraFeelCue.DashLaunch:    dashLaunch = value; break;
                case CameraFeelCue.DashLand:      dashLand = value; break;
                case CameraFeelCue.ImpactLight:   impactLight = value; break;
                case CameraFeelCue.ImpactMedium:  impactMedium = value; break;
                case CameraFeelCue.ImpactHeavy:   impactHeavy = value; break;
                case CameraFeelCue.ImpactMassive: impactMassive = value; break;
                case CameraFeelCue.Death:         death = value; break;
                case CameraFeelCue.BossPhase:     bossPhase = value; break;
                case CameraFeelCue.LevelUp:       levelUp = value; break;
                case CameraFeelCue.ComboPayoff:   comboPayoff = value; break;
            }
        }

        /// <summary>Restores every value to the shipped tuning.</summary>
        public void ResetToDefaults() => ApplyDefaults();

        /// <summary>
        /// Applies a whole-camera starting point. Always begins from the shipped tuning, so a
        /// preset is a destination rather than a delta on whatever was there before — two
        /// clicks of the same preset land in the same place.
        /// </summary>
        public void ApplyPreset(CameraFeelPreset preset)
        {
            ApplyDefaults();

            switch (preset)
            {
                case CameraFeelPreset.Rigid:
                    // The pre-existing behaviour, kept as the honest comparison baseline.
                    followOmega = 0f;
                    moveLeadWu = 0f;
                    aimLeadIdleWu = 0f;
                    aimLeadMovingWu = 0f;
                    break;

                case CameraFeelPreset.Cinematic:
                    followOmega = 11f;      // 0.73 wu of lag at walking speed
                    moveLeadWu = 1.60f;     // net +0.87 wu ahead
                    leadOmega = 3.2f;
                    maxFollowLagWu = 1.6f;
                    maxLeadWu = 2.4f;
                    break;

                case CameraFeelPreset.Subtle:
                    followOmega = 20f;      // 0.40 wu of lag
                    moveLeadWu = 0.55f;     // net +0.15 wu ahead
                    leadOmega = 5.5f;
                    break;

                case CameraFeelPreset.MovementOnly:
                    masterIntensity01 = 0f;
                    break;
            }
        }
    }
}
