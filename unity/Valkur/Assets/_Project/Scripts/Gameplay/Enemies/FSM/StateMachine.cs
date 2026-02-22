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

        public event Action<IState, IState> OnStateChanged;

        public StateMachine(GameObject owner, IState initialState)
        {
            Owner = owner;
            CurrentState = initialState;
            CurrentState.Enter(this);
        }

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
                if (!isSpecial) return;
            }

            var oldState = CurrentState;
            CurrentState.Exit(this);
            CurrentState = newState;
            CurrentState.Enter(this);
            OnStateChanged?.Invoke(oldState, newState);
        }

        public void Update(float dt)
        {
            ProcessEvents();
            CurrentState.Execute(this, dt);
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

            float stopProb = GetContextFloat("damage_stop_probability", 0.25f);
            if (UnityEngine.Random.value >= stopProb) return;

            float duration = GetContextFloat("damage_duration", 0.25f);
            ChangeState(new DamageState(duration, evt.FromLeft));
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
