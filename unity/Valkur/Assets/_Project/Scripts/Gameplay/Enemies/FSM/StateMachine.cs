using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Generic finite state machine matching Python's FiniteStateMachine.
    /// Supports context dict, allowed state guards, event queue, and transition history.
    /// </summary>
    public class StateMachine
    {
        public IState CurrentState { get; private set; }
        public GameObject Owner { get; private set; }
        public Dictionary<string, object> Context { get; private set; } = new Dictionary<string, object>();

        private readonly List<FSMEvent> _eventQueue = new List<FSMEvent>();
        private HashSet<string> _allowedStates;

        // Authored transitions, highest priority first. Null = none authored, which is
        // the shipped state today and makes Update behave exactly as it always did.
        private FSMTransition[] _transitions;
        private Dictionary<string, float> _transitionCooldowns;

        /// <summary>
        /// Seconds since the current state was entered. Exposed because the authored
        /// guard grammar has a <c>state_time</c> signal — "flee if you have been chasing
        /// for more than three seconds" is not expressible without it.
        /// </summary>
        public float TimeInCurrentState { get; private set; }

        /// <summary>
        /// Seconds since this entity last took a hit, capped so it cannot drift to
        /// infinity. Starts at the cap, i.e. "never been hit".
        ///
        /// Exists so a guard can express "something just shot me": the OnHit event is
        /// otherwise consumed entirely by the flinch roll, and a monster shot from beyond
        /// its aggro range was ignored nine times out of ten with nothing left for an
        /// authored edge to react to.
        /// </summary>
        public float TimeSinceLastHit { get; private set; } = HitMemorySeconds;

        /// <summary>Ceiling for <see cref="TimeSinceLastHit"/>. Any guard interested in a
        /// recent hit asks for far less than this.</summary>
        public const float HitMemorySeconds = 999f;

        public event Action<IState, IState> OnStateChanged;

        /// <summary>
        /// True until the initial state has actually been entered. See
        /// <see cref="Begin"/> for why the constructor no longer enters it.
        /// </summary>
        private bool _initialEnterPending;

        /// <summary>
        /// The initial state is installed but NOT entered here.
        ///
        /// Enter() is where a state reads the context — IdleState asks for
        /// <see cref="FSMComponents"/> to stop the body and set the Idle animation,
        /// PatrolState reads <c>patrol_waypoints</c> — and every caller populates the
        /// context AFTER constructing the machine, because the context is what the
        /// constructor's arguments cannot carry. Entering here therefore ran the
        /// initial state against an empty dictionary: the Idle pose was never applied,
        /// and a monster whose authored initial state is PatrolState took its waypoint
        /// list as null and stood still forever, since nothing re-enters a state you
        /// never leave.
        ///
        /// <see cref="Begin"/> performs the entry once the context is in place;
        /// <see cref="Update"/> calls it for any caller that forgets.
        /// </summary>
        public StateMachine(GameObject owner, IState initialState)
        {
            Owner = owner;
            CurrentState = initialState;
            _initialEnterPending = initialState != null;
        }

        /// <summary>
        /// Enters the initial state. Idempotent — calling it twice, or calling it after
        /// <see cref="Update"/> already did, enters nothing a second time. Call it once
        /// the context this machine's states need is fully published.
        /// </summary>
        public void Begin()
        {
            if (!_initialEnterPending) return;
            _initialEnterPending = false;
            TimeInCurrentState = 0f;
            CurrentState?.Enter(this);
        }

        /// <summary>True when <see cref="Begin"/> has not run yet.</summary>
        public bool IsInitialEnterPending => _initialEnterPending;

        /// <summary>
        /// Install the transitions authored in the F12 editor for this entity's set.
        /// Sorted by descending priority by the caller. Null or empty is normal.
        /// </summary>
        public void SetTransitions(FSMTransition[] transitions)
        {
            _transitions = (transitions != null && transitions.Length > 0) ? transitions : null;
            _transitionCooldowns = null;
        }

        /// <summary>True when this machine has authored transitions to evaluate.</summary>
        public bool HasAuthoredTransitions => _transitions != null;

        /// <summary>
        /// Set allowed state class names. Null = all allowed.
        /// Maps to Python's context['allowed_state_classes'].
        /// </summary>
        public void SetAllowedStates(HashSet<string> allowed)
        {
            _allowedStates = allowed;
        }

        /// <summary>
        /// Change to a new state with guard checking.
        /// Maps to Python's FSM.change_state() with allowed_state_classes guard.
        /// </summary>
        public void ChangeState(IState newState)
        {
            if (newState == null) return;

            string newName = newState.GetType().Name;

            // Guard: check allowed states (always allow Death/Damage/Unconscious)
            if (_allowedStates != null && !_allowedStates.Contains(newName))
            {
                bool isSpecial = newName == "DeathState" || newName == "DamageState" || newName == "UnconsciousState";
                if (!isSpecial)
                {
                    WarnRefusedOnce(CurrentState?.GetType().Name, newName);
                    return;
                }
            }

            var oldState = CurrentState;
            // A state that was never entered has nothing to exit — leaving one would
            // run Exit's teardown against setup that never happened.
            if (_initialEnterPending) _initialEnterPending = false;
            else CurrentState.Exit(this);
            CurrentState = newState;
            TimeInCurrentState = 0f;
            CurrentState.Enter(this);
            OnStateChanged?.Invoke(oldState, newState);
        }

        /// <summary>
        /// Refused transitions seen so far, keyed "From>To". Static because the point is one
        /// message per misconfiguration for the whole session, not one per monster: a set
        /// with a deleted node produces the same refusal on every entity that uses it.
        /// </summary>
        private static readonly HashSet<string> _refusalsWarned = new HashSet<string>();

        /// <summary>
        /// Says out loud that a transition was dropped by the allowed-state guard.
        ///
        /// This used to return in silence, which turned deleting a node in F12 into an
        /// invisible deadlock: remove ChaseState from a set and IdleState asks to enter it
        /// every single tick, forever, while the monster stands still and the console stays
        /// clean. The Delete tool presents itself as an ordinary, undoable edit and gives no
        /// hint that the machine now has a hole in it.
        ///
        /// Once per From>To pair, not per frame — the failure repeats at frame rate, and a
        /// warning that repeats with it is the same as no warning. This is NOT the "expected
        /// steady state" case CLAUDE.md says to gate through VerboseLog: a refused transition
        /// is always a misconfiguration, and it will not heal on its own.
        /// </summary>
        private static void WarnRefusedOnce(string from, string to)
        {
            string key = from + ">" + to;
            if (!_refusalsWarned.Add(key)) return;

            Debug.LogWarning(
                $"[FSM] Transition {from} -> {to} was refused: '{to}' is not in this set's " +
                "allowed-state list. The entity stays in its current state and will retry " +
                "every tick. Add the node back in F12, or remove whatever asks for it.");
        }

        /// <summary>Domain Reload is OFF, so the warned-set survives a Play-mode restart and
        /// would swallow the first session's worth of diagnostics on the second run.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _refusalsWarned.Clear();

        public void Update(float dt)
        {
            // Safety net for a caller that never called Begin() — the entry still
            // happens with a populated context, one tick late rather than never.
            Begin();

            TimeInCurrentState += dt;
            if (TimeSinceLastHit < HitMemorySeconds)
                TimeSinceLastHit = Mathf.Min(HitMemorySeconds, TimeSinceLastHit + dt);
            ProcessEvents();

            // Authored transitions are evaluated BEFORE the state's own logic and the
            // first passing guard wins, so a designer's edge overrides the hard-coded
            // exit rather than racing it. When nothing is authored — every set shipped
            // today — this is a null check and the behaviour is unchanged.
            if (_transitions != null && TryTakeAuthoredTransition(dt)) return;

            CurrentState.Execute(this, dt);
        }

        private bool TryTakeAuthoredTransition(float dt)
        {
            string currentName = CurrentState.GetType().Name;

            // A corpse is not steerable. Death/Unconscious own their own timelines and an
            // authored edge out of them would resurrect the entity mid-despawn.
            if (CurrentState is DeathState || CurrentState is UnconsciousState) return false;

            for (int i = 0; i < _transitions.Length; i++)
            {
                var t = _transitions[i];
                if (!t.AppliesTo(currentName)) continue;
                if (t.To == currentName) continue;           // self-edges are a no-op here
                if (IsOnCooldown(t, dt)) continue;
                if (t.Condition != null && !t.Condition.Evaluate(this)) continue;

                // Fully qualified: the factory lives in Valkur.Gameplay.Enemies.FSM while
                // this type is in Valkur.Gameplay.FSM, and importing the former would make
                // every unqualified FSM name in this file ambiguous.
                var next = Valkur.Gameplay.Enemies.FSM.FSMRuntimeFactory.CreateState(t.To);
                if (next == null) continue;                  // logged once at load time

                StampCooldown(t);
                ChangeState(next);
                return true;
            }
            return false;
        }

        private bool IsOnCooldown(FSMTransition t, float dt)
        {
            if (t.CooldownSeconds <= 0f || _transitionCooldowns == null) return false;

            string key = t.From + ">" + t.To;
            if (!_transitionCooldowns.TryGetValue(key, out float remaining)) return false;

            remaining -= dt;
            if (remaining <= 0f)
            {
                _transitionCooldowns.Remove(key);
                return false;
            }
            _transitionCooldowns[key] = remaining;
            return true;
        }

        private void StampCooldown(FSMTransition t)
        {
            if (t.CooldownSeconds <= 0f) return;
            _transitionCooldowns ??= new Dictionary<string, float>(4);
            _transitionCooldowns[t.From + ">" + t.To] = t.CooldownSeconds;
        }

        /// <summary>
        /// Queue an FSM event for processing next update.
        /// Maps to Python's FSMEventQueue component.
        /// </summary>
        public void QueueEvent(FSMEvent evt)
        {
            _eventQueue.Add(evt);
        }

        private void ProcessEvents()
        {
            if (_eventQueue.Count == 0) return;

            for (int i = 0; i < _eventQueue.Count; i++)
            {
                var evt = _eventQueue[i];
                switch (evt.Type)
                {
                    case FSMEventType.OnHit:
                        HandleHitEvent(evt);
                        break;
                    case FSMEventType.OnDeath:
                        HandleDeathEvent();
                        break;
                }
            }
            _eventQueue.Clear();
        }

        private void HandleHitEvent(FSMEvent evt)
        {
            if (CurrentState is DeathState || CurrentState is UnconsciousState) return;

            // Stamp EVERY hit, not just the ones that win the flinch roll below. The roll
            // decides whether the entity staggers; whether it noticed being shot at all is
            // a different question, and authored guards need the second one.
            TimeSinceLastHit = 0f;

            float stopProb = GetContextFloat("damage_stop_probability", 0.25f);
            if (UnityEngine.Random.value >= stopProb) return;

            float duration = GetContextFloat("damage_duration", 0.25f);

            // Remember what the flinch interrupted so DamageState can put the entity back
            // where it found it, instead of dumping everything into ChaseState.
            //
            // A hit landing DURING a flinch restarts the stagger but must preserve the
            // ORIGINAL interrupted state: capturing the current name here would record
            // "DamageState", which the resume path cannot construct (three-parameter
            // constructor -> MissingMethodException, logged once per occurrence) and
            // silently degraded into ChaseState — a patrolling monster hit twice in half a
            // second came out of the stagger hunting instead of patrolling.
            string resumeState = CurrentState is DamageState dmg
                ? dmg.ReturnStateClass
                : CurrentState?.GetType().Name;
            ChangeState(new DamageState(duration, evt.FromLeft, resumeState));
        }

        private void HandleDeathEvent()
        {
            if (CurrentState is DeathState || CurrentState is UnconsciousState) return;
            ChangeState(new UnconsciousState());
        }

        #region Context Helpers

        public void SetContext(string key, object value) => Context[key] = value;

        public T GetContext<T>(string key, T defaultValue = default)
        {
            if (Context.TryGetValue(key, out var val) && val is T typed)
                return typed;
            return defaultValue;
        }

        public float GetContextFloat(string key, float defaultValue = 0f)
        {
            if (Context.TryGetValue(key, out var val))
            {
                if (val is float f) return f;
                if (val is int i) return i;
                if (val is double d) return (float)d;
            }
            return defaultValue;
        }

        public bool GetContextBool(string key, bool defaultValue = false)
        {
            if (Context.TryGetValue(key, out var val) && val is bool b)
                return b;
            return defaultValue;
        }

        #endregion
    }

    public enum FSMEventType { OnHit, OnDeath }

    public struct FSMEvent
    {
        public FSMEventType Type;
        public bool FromLeft;
        public int Damage;
    }
}
