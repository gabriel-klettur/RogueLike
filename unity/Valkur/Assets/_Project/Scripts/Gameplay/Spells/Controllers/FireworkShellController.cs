using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// One firework shell, from the mortar to the last report.
    ///
    /// <para>WHY THIS OWNS THE FLIGHT INSTEAD OF <c>Projectile</c>. The spell is cosmetic —
    /// zero damage, no target layers — so nothing in <c>Projectile</c> is wanted, and one
    /// thing in it is actively fatal: it expires on <c>range</c>, which defaults to 20 for a
    /// spell that authors none. Riding a shared prefab and inheriting a component you are
    /// replacing is exactly how the boomerang lost its entire return leg for the life of that
    /// spell, and the answer there was the same as here — own the flight.</para>
    ///
    /// <para>THE TIMELINE IS THE DESIGN. A firework is three beats and the old implementation
    /// had one: it flashed at the caster and the "shell" was an invisible projectile trailing
    /// twelve white dots that were locked to it. Here the shell CLIMBS (so there is something
    /// to look at, and something for the whistle to be about), BURSTS at its apex, and its
    /// report arrives LATE — a detonation seven units up is seen before it is heard, and that
    /// single delay is most of what makes the effect read as happening in the sky rather than
    /// on the lens.</para>
    /// </summary>
    public partial class FireworkShellController : MonoBehaviour
    {
        /// <summary>How far the shell flies along the aim when the spell authors no range.</summary>
        public const float DEFAULT_FLIGHT_DISTANCE = 6.5f;

        /// <summary>Flight speed in world units per second when the spell authors none.</summary>
        public const float DEFAULT_FLIGHT_SPEED = 9f;

        /// <summary>Burst radius when the spell authors none.</summary>
        public const float DEFAULT_BURST_RADIUS = 3.2f;

        /// <summary>
        /// How late the report is, per world unit between the caster and the burst. Small on
        /// purpose: the point is that the eye beats the ear, not that the sound is disconnected
        /// from the picture. At the default distance this is ~0.13 s, about four frames.
        /// </summary>
        public const float REPORT_DELAY_PER_UNIT = 0.020f;

        /// <summary>
        /// Secondary shells. A real firework rarely goes off exactly once — the companions are
        /// what turn a single circle into an event, and they are small, late and off-centre so
        /// they read as fragments of the same shell rather than as three separate spells.
        /// </summary>
        public const int COMPANIONS = 2;

        private const float COMPANION_RADIUS_FRACTION = 0.42f;
        private const float COMPANION_DELAY_MIN = 0.10f;
        private const float COMPANION_DELAY_MAX = 0.28f;

        /// <summary>
        /// How high the shell bows above the straight line to its burst point, as a fraction of
        /// the flight distance.
        ///
        /// <para>This is what keeps a MORTAR from becoming a bullet. The shell goes where the
        /// cursor points — that is the whole ask — but a firework travelling in a straight line
        /// is a projectile, and the arc is most of what says the thing is being lobbed. The bow
        /// is scaled by how HORIZONTAL the aim is, so a shot straight up gets none of it (there
        /// is nothing to bow away from) and a flat shot across the street gets all of it.</para>
        /// </summary>
        private const float ARC_BOW_FRACTION = 0.30f;

        private FireworkPalette _palette;
        private Vector3 _launchPos;
        private Vector3 _burstPos;
        private float _arcBow;
        private float _climbSeconds;
        private float _burstRadius;
        private float _age;

        private bool _burst;
        private bool _reported;
        private float _reportAt;
        private float _endAt;

        /// <summary>
        /// Where this shell will open. Exposed for <c>FireworkVisualContractTests</c>: the aim
        /// is resolved once at launch and then only replayed by <see cref="Climb"/>, so the
        /// honest way to ask "did the cursor bearing reach the burst" without running a frame
        /// is to read the resolved point.
        /// </summary>
        internal Vector3 BurstPosition => _burstPos;

        /// <summary>How far the flight bows above the straight line. Zero for a vertical shot.</summary>
        internal float ArcBow => _arcBow;

        private int _companionsFired;
        private readonly float[] _companionDelays = new float[COMPANIONS];
        private readonly Vector3[] _companionOffsets = new Vector3[COMPANIONS];

        /// <summary>
        /// Launch a shell along <paramref name="direction"/>, which for a player is the cursor
        /// bearing — <c>PlayerFacingResolver</c> derives it from the mouse, so this is the same
        /// aim every other spell is cast with.
        ///
        /// <para>An aim of zero falls back to straight up rather than to nowhere: a monster or a
        /// console command with no bearing should still produce a firework.</para>
        /// </summary>
        internal static FireworkShellController Launch(Vector3 origin, Vector2 direction,
                                                     FireworkPalette palette,
                                                     float flightDistance, float flightSpeed,
                                                     float burstRadius)
        {
            var go = new GameObject("FireworkShell");
            go.transform.position = origin;

            var shell = go.AddComponent<FireworkShellController>();
            shell._palette = palette;
            shell._launchPos = origin;
            shell._burstRadius = Mathf.Max(0.5f, burstRadius);

            float distance = Mathf.Max(1f, flightDistance);
            float speed = Mathf.Max(1f, flightSpeed);
            shell._climbSeconds = distance / speed;

            Vector2 aim = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.up;
            shell._burstPos = origin + new Vector3(aim.x, aim.y, 0f) * distance;

            // Scaled by how horizontal the aim is — see ARC_BOW_FRACTION.
            shell._arcBow = distance * ARC_BOW_FRACTION * (1f - Mathf.Abs(aim.y));

            shell._reportAt = shell._climbSeconds + distance * REPORT_DELAY_PER_UNIT;

            for (int i = 0; i < COMPANIONS; i++)
            {
                shell._companionDelays[i] = Random.Range(COMPANION_DELAY_MIN, COMPANION_DELAY_MAX);
                shell._companionOffsets[i] = new Vector3(
                    Random.Range(-1f, 1f), Random.Range(-0.55f, 0.85f), 0f) * shell._burstRadius * 0.85f;
            }

            // Sorted ascending because TickCompanions walks the array in order: an unsorted
            // pair makes the earlier-scheduled shell wait behind a later one and the two go off
            // together, which is the one thing companions exist to avoid.
            System.Array.Sort(shell._companionDelays);

            // The whole thing has to outlive its own last sound and its last burst's particles.
            float lastCompanion = COMPANIONS > 0 ? shell._companionDelays[COMPANIONS - 1] : 0f;
            shell._endAt = shell._climbSeconds + lastCompanion + FireworkBurstFX.STAR_LIFETIME + 1.6f;

            shell.Build();
            shell.PlayAt(FireworkAudio.Launch(), origin, 0.85f);
            shell.PlayAt(FireworkAudio.Whistle(), origin, 0.45f);
            return shell;
        }

        private void Update()
        {
            _age += Time.deltaTime;

            if (!_burst)
            {
                Climb();
                if (_age >= _climbSeconds) Burst();
                return;
            }

            TickCompanions();

            if (!_reported && _age >= _reportAt)
            {
                _reported = true;
                PlayAt(FireworkAudio.Burst(), _burstPos, 1f);
            }

            if (_age >= _endAt) Destroy(gameObject);
        }

        /// <summary>
        /// The flight. Two independent curves, and they say different things.
        ///
        /// <para>Progress ALONG the line decelerates, because a shell is coasting against
        /// gravity by the time it arrives — a linear rise reads as a lift, not as something
        /// thrown. The BOW above that line is symmetric in raw time, so the shell rises off the
        /// straight path and settles back onto its burst point. Driving the bow off the eased
        /// value instead would skew its peak toward the end and read as a hook.</para>
        /// </summary>
        private void Climb()
        {
            float t = Mathf.Clamp01(_age / _climbSeconds);
            float eased = 1f - Mathf.Pow(1f - t, 1.85f);

            Vector3 previous = transform.position;
            Vector3 along = Vector3.Lerp(_launchPos, _burstPos, eased);
            transform.position = along + Vector3.up * (_arcBow * Mathf.Sin(Mathf.PI * t));
            AnimateClimb(t, transform.position - previous);
        }

        private void Burst()
        {
            _burst = true;
            transform.position = _burstPos;
            HideShell();

            FireworkBurstFX.Spawn(_burstPos, _palette, _burstRadius);
        }

        private void TickCompanions()
        {
            while (_companionsFired < COMPANIONS &&
                   _age >= _climbSeconds + _companionDelays[_companionsFired])
            {
                Vector3 at = _burstPos + _companionOffsets[_companionsFired];
                FireworkBurstFX.Spawn(at, _palette, _burstRadius * COMPANION_RADIUS_FRACTION);
                PlayAt(FireworkAudio.Companion(), at, 0.55f);
                _companionsFired++;
            }
        }

        private void PlayAt(AudioClip clip, Vector3 position, float volume)
        {
            if (clip == null) return;
            ServiceLocator.Get<IAudioService>()?.PlaySFXAtPosition(clip, position, volume);
        }
    }
}
