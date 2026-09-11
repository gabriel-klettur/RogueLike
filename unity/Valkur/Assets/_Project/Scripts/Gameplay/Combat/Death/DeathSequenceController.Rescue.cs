using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Save;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Combat.Death
{
    /// <summary>
    /// The guarantee that a death always has an exit.
    ///
    /// <para><b>Why this is the most important part of the subsystem.</b> Every other axis of the
    /// death flow can be wrong and still leave a playable game. This one cannot: a spirit that
    /// cannot reach an altar has no verb left. The 2026-09-07 audit found the shipped build in
    /// exactly that state — zero altars in the world, the player 64 units from their own corpse,
    /// phase Spirit, with the DevConsole the only way out. Nothing had failed; the altar simply
    /// was not there, and nothing in the flow was able to notice.</para>
    ///
    /// <para><b>Two independent triggers, because "stuck" has two shapes.</b> A world with NO
    /// altar is knowable immediately and is rescued on a short clock — there is nothing to wait
    /// for. A world WITH an altar the player cannot reach (walled off, wrong side of an interior,
    /// or simply lost) is indistinguishable from a player taking their time, so it is rescued on
    /// the long <see cref="DeathTuning.spiritTimeLimitSeconds"/> clock instead.</para>
    ///
    /// <para><b>The no-altar clock does not run while the binder is still looking.</b>
    /// <c>BuildingLoader</c> streams a world in over several frames, so a player who dies in the
    /// first seconds of a session would otherwise be rescued for a world whose altars simply had
    /// not spawned yet — a real answer to a question that was asked too early.</para>
    /// </summary>
    public partial class DeathSequenceController
    {
        private float _noAltarElapsed;
        private ResurrectionZoneAutoBinder _binder;

        /// <summary>Why the last rescue fired, for the status line and the console probe.</summary>
        public string LastRescueReason { get; private set; } = string.Empty;

        /// <summary>
        /// Seconds until the safety net fires, or <see cref="float.PositiveInfinity"/> when it is
        /// not armed. Surfaced so the death banner can COUNT DOWN rather than leaving the player
        /// guessing whether anything is going to happen.
        /// </summary>
        public float RescueEta
        {
            get
            {
                var tuning = Tuning;
                if (CurrentPhase != Phase.Spirit) return float.PositiveInfinity;
                if (tuning.rescueMode == DeathRescueMode.None) return float.PositiveInfinity;

                if (!ResurrectionAltarRegistry.AnyUsable)
                {
                    if (BinderStillSearching) return float.PositiveInfinity;
                    return Mathf.Max(0f, tuning.rescueDelayWithoutAltar - _noAltarElapsed);
                }

                if (tuning.spiritTimeLimitSeconds <= 0f) return float.PositiveInfinity;
                return Mathf.Max(0f, tuning.spiritTimeLimitSeconds - SpiritElapsed);
            }
        }

        /// <summary>True when no altar is registered AND the binder has stopped looking for one.</summary>
        public bool NoAltarInWorld => !ResurrectionAltarRegistry.AnyUsable && !BinderStillSearching;

        private bool BinderStillSearching
        {
            get
            {
                if (_binder == null) _binder = FindObjectOfType<ResurrectionZoneAutoBinder>();
                // No binder at all means nothing is going to bind an altar later, so the answer
                // is "not searching" rather than "unknown". A null here used to read as "wait",
                // which is the one answer that never resolves.
                return _binder != null && _binder.IsSearching;
            }
        }

        private void ResetRescueClock()
        {
            _noAltarElapsed = 0f;
        }

        /// <summary>Runs every frame the player is a spirit. The clock, and nothing else.</summary>
        private void TickSpirit()
        {
            float dt = Time.unscaledDeltaTime;
            SpiritElapsed += dt;

            var tuning = Tuning;
            if (tuning.rescueMode == DeathRescueMode.None) return;
            if (_activeCoroutine != null) return;

            if (!ResurrectionAltarRegistry.AnyUsable)
            {
                if (BinderStillSearching) return;

                _noAltarElapsed += dt;
                if (_noAltarElapsed >= tuning.rescueDelayWithoutAltar)
                    Rescue("no hay ningun altar en el mundo cargado");
                return;
            }

            _noAltarElapsed = 0f;

            if (tuning.spiritTimeLimitSeconds > 0f && SpiritElapsed >= tuning.spiritTimeLimitSeconds)
                Rescue($"limite de {tuning.spiritTimeLimitSeconds:0} s en forma espiritu");
        }

        /// <summary>
        /// Put the player back on their feet somewhere safe. Public so the Death Editor and the
        /// DevConsole can fire it on demand — a player who knows they are stuck should not have to
        /// wait out a clock built for a player who does not.
        /// </summary>
        public bool Rescue(string reason)
        {
            if (CurrentPhase != Phase.Spirit) return false;

            LastRescueReason = reason ?? string.Empty;
            Vector3 point = ResolveRescuePoint(Tuning.rescueMode);

            Debug.Log($"[DeathSequence] Rescate: {LastRescueReason}. Revive en {point} " +
                      $"al {Mathf.RoundToInt(Tuning.rescueHpFraction * 100f)}% de vida.");

            if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
            _activeCoroutine = StartCoroutine(ReviveRoutine(ReviveKind.Rescue, instant: false, teleportTo: point));
            return true;
        }

        /// <summary>
        /// Where a rescue puts the player.
        ///
        /// <para>Every mode falls back to the death position rather than to the world origin,
        /// because the death position is the only point guaranteed to exist, to be inside the
        /// loaded world, and to be somewhere the player was standing a moment ago — i.e. reachable
        /// ground. It also keeps the corpse's loot in reach, which the origin would not.</para>
        /// </summary>
        /// <summary>Whether a stored checkpoint names a zone the loaded world actually has.
        /// The judgement itself lives in <see cref="PlayerPositionPersistence"/>.</summary>
        private bool IsCheckpointInThisWorld(string zone)
        {
            var zones = FindObjectOfType<ZoneManager>();
            System.Func<string, bool> known = zones == null
                ? (System.Func<string, bool>)null
                : (name => zones.TryGetZone(name, out _));

            return PlayerPositionPersistence.IsUsableSpawn(zone, known, out _);
        }

        private Vector3 ResolveRescuePoint(DeathRescueMode mode)
        {
            switch (mode)
            {
                case DeathRescueMode.LastCheckpoint:
                {
                    var checkpoint = SaveFileManager.ReadPositionCheckpoint();
                    // A checkpoint recorded inside an interior is a coordinate in a grid this
                    // world does not have, so rescuing onto it strands the player in the void —
                    // which for a rescue is the one outcome that must be impossible. The death
                    // position is guaranteed to be inside the loaded world.
                    if (checkpoint != null && IsCheckpointInThisWorld(checkpoint.zone))
                        return new Vector3(checkpoint.x, checkpoint.y, 0f);
                    return LastDeathPosition;
                }

                case DeathRescueMode.ZoneSpawn:
                {
                    var zones = FindObjectOfType<ZoneManager>();
                    if (zones != null && !string.IsNullOrEmpty(zones.CurrentZone))
                    {
                        Vector2 centre = zones.GetZoneCenter(zones.CurrentZone);
                        if (centre != Vector2.zero) return new Vector3(centre.x, centre.y, 0f);
                    }
                    return LastDeathPosition;
                }

                case DeathRescueMode.DeathPosition:
                default:
                    return LastDeathPosition;
            }
        }

        /// <summary>
        /// Move the body, and do the two things every teleport in this project has to do:
        /// zero the velocity and WAKE the Rigidbody. A Dynamic body that has come to rest goes to
        /// sleep after half a second, and a sleeping body starts no new contacts — so a player
        /// dropped onto a doorway or a resurrection footprint would never trigger it.
        /// </summary>
        private static void TeleportPlayer(GameObject player, Vector3 destination)
        {
            player.transform.position = new Vector3(destination.x, destination.y, player.transform.position.z);

            var rb = player.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.velocity = Vector2.zero;
                rb.WakeUp();
            }
        }
    }
}
