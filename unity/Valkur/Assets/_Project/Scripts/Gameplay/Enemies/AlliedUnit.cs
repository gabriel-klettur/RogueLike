using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Marks an entity as fighting FOR the player: summons, thralls, anything charmed.
    ///
    /// <para>WHY A COMPONENT AND NOT <c>EntityStats.faction</c>. The faction string is
    /// authored on every shipped entity and read by exactly one line in the whole project
    /// (<c>FSMMonsterBrain</c> pushes it into the FSM context, where the shipped sets never
    /// test it). No state class reads it — grep Idle/Patrol/Chase/Attack and the count is
    /// zero — so making allegiance depend on it would mean either growing every authored FSM
    /// set a guard nobody has, or adding a second meaning to a field that already has an
    /// inert one. This project has recorded eleven authored-and-inert layers; the fix is not
    /// a twelfth.</para>
    ///
    /// <para>A live registry rather than a <c>GetComponent</c> per query, because
    /// <see cref="FactionTargeting"/> runs inside <c>ChaseState.Tick</c> for every monster
    /// every frame. The list is normally EMPTY, which makes the hostile path a count check.</para>
    /// </summary>
    public sealed class AlliedUnit : MonoBehaviour
    {
        // Domain Reload is OFF, so a static collection that survives a Play-mode restart
        // holds destroyed GameObjects and hands them out as targets. Cleared through
        // field.Clear(), which is one of the two forms DomainReloadStaticResetTests
        // recognises when it reads this hook's raw IL -- System.Array.Clear or a helper call
        // would pass the field as an ARGUMENT and count as no reset at all.
        private static readonly List<AlliedUnit> _live = new List<AlliedUnit>(4);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _live.Clear();

        /// <summary>
        /// Every registered ally, as a SNAPSHOT. Entries may be inactive — callers filter on
        /// eligibility, which is a different question from membership.
        ///
        /// <para>WHY A COPY. Three things mutate the backing list: <see cref="Prune"/> (called
        /// by this property and by <see cref="AnyLive"/>), <see cref="Register"/>, and a
        /// destroyed ally leaving. Handing out the list itself makes
        /// <c>for (i = 0; i &lt; allies.Count; i++)</c> re-read <c>Count</c> every iteration
        /// against something the loop body could shrink — and the failure is the quiet kind:
        /// indices shift, an entry is SKIPPED, no exception is thrown, and a monster simply
        /// ignores an ally standing in front of it. Today's two call sites happen not to
        /// mutate, but that is a property of those call sites and not of this API, and
        /// <c>FactionTargeting</c> is now the seam every monster passes through.</para>
        ///
        /// <para>THE COPY IS NOT FREE, AND HERE IS WHAT IT COSTS. With no allies out this
        /// returns a shared empty array and allocates nothing, which is the case virtually
        /// always. With an ally out, <c>FactionTargeting.NearestPlayerSideTo</c> is reached by
        /// every monster and several of the states that call it run in <c>Update</c> — twenty
        /// monsters at 60 fps is on the order of 1200 small arrays a second. Two things bound
        /// it: both summon spells author <c>maxInstances: 1</c>, so the arrays hold one or two
        /// elements, and an ally is on the field for seconds at a time rather than always.</para>
        ///
        /// <para>It is left allocating on purpose. Correct-and-allocating beats
        /// fast-and-silently-wrong, and there is no measurement saying this matters — this
        /// project has a Profiler/Recorder workflow precisely so that question gets a number
        /// instead of an argument. If one ever shows it does, the cheap fix is a
        /// <c>CopyTo(AlliedUnit[] buffer)</c> against a per-purpose buffer in
        /// <c>PhysicsScratch</c>: same snapshot semantics, no per-call allocation.</para>
        /// </summary>
        public static IReadOnlyList<AlliedUnit> Live
        {
            get
            {
                Prune();
                if (_live.Count == 0) return System.Array.Empty<AlliedUnit>();
                return _live.ToArray();
            }
        }

        /// <summary>True when anything at all is fighting for the player right now.</summary>
        public static bool AnyLive
        {
            get
            {
                Prune();
                for (int i = 0; i < _live.Count; i++)
                    if (_live[i] != null && _live[i].isActiveAndEnabled) return true;
                return false;
            }
        }

        /// <summary>
        /// Drop DESTROYED entries only. A deactivated ally stays registered and is simply not
        /// eligible — see the two-questions note below.
        ///
        /// <para>MEMBERSHIP IS MAINTAINED ON READ RATHER THAN BY <c>OnDisable</c> ALONE.
        /// Unity calls <c>Awake</c>/<c>OnEnable</c> only on a component added in PLAY MODE
        /// (or one marked <c>[ExecuteAlways]</c>), and skips the matching teardown for the
        /// same reason — so in Edit Mode a component added to a live GameObject never
        /// registers and a deactivated one never leaves. CLAUDE.md records the trap: a test
        /// that adds a component and asserts on what its lifecycle callbacks did is measuring
        /// nothing. It is also the safer runtime shape, because Domain Reload is OFF and a
        /// destroyed ally left in a static list would be handed out as a target.</para>
        ///
        /// <para>MEMBERSHIP AND ELIGIBILITY ARE TWO QUESTIONS, and the first version of this
        /// conflated them by pruning inactive entries as well. That made re-enabling an ally
        /// impossible outside Play Mode: nothing calls <c>OnEnable</c> there, so once dropped
        /// it never came back. Destroyed leaves the registry; deactivated stays in it and is
        /// filtered by whoever asks, which re-reads correctly the moment it is switched on
        /// again.</para>
        ///
        /// <para>The cost is bounded by the list being all but always EMPTY, which is the same
        /// property that makes <see cref="AnyLive"/> a viable fast path for every monster in
        /// the game.</para>
        /// </summary>
        private static void Prune()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
                if (_live[i] == null) _live.RemoveAt(i);
        }

        /// <summary>Join the registry, idempotently. Called from every path that can bring an
        /// ally into existence, because no single one of them is guaranteed to run.</summary>
        private void Register()
        {
            if (this == null) return;
            if (!_live.Contains(this)) _live.Add(this);
        }

        /// <summary>Seconds this ally has left, or a negative value when it does not expire.</summary>
        public float RemainingSeconds => _expiresAt < 0f ? -1f : Mathf.Max(0f, _expiresAt - Time.time);

        /// <summary>Raised when the ally's time runs out, before it is torn down. Distinct
        /// from dying: the two must not look the same on screen, or the player cannot tell a
        /// summon that was killed from one that simply ended.</summary>
        public event System.Action OnExpired;

        private float _expiresAt = -1f;
        private bool _expiring;

        /// <summary>
        /// Give this ally a lifetime. A non-positive duration means "no timer", which is for
        /// a permanent companion rather than a spell.
        /// </summary>
        public void SetLifetime(float seconds)
        {
            _expiresAt = seconds > 0f ? Time.time + seconds : -1f;
            // Registering here as well as in OnEnable is what makes the component work when
            // it is added outside Play Mode, where OnEnable never runs. Every path that
            // creates an ally passes through this method.
            Register();
        }

        /// <summary>True when <paramref name="go"/> is fighting for the player.</summary>
        public static bool IsAllied(GameObject go)
            => go != null && go.GetComponent<AlliedUnit>() != null;

        private void Awake() => Register();

        private void OnEnable() => Register();

        // Deliberately does NOT unregister. A disabled ally is still one of the player's --
        // it is filtered by eligibility, not by membership -- and removing it here would make
        // the Play-mode path disagree with the Edit-mode one, where OnDisable never runs.
        // Destroyed objects leave through Prune instead.
        private void OnDisable() { }

        private void Update()
        {
            if (_expiring || _expiresAt < 0f) return;
            if (Time.time < _expiresAt) return;

            _expiring = true;
            OnExpired?.Invoke();
        }
    }
}
