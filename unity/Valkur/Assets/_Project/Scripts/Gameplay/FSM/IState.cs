namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Base interface for FSM states.
    /// Maps to Python's State ABC (enter, execute, exit).
    /// </summary>
    public interface IState
    {
        void Enter(StateMachine fsm);
        void Execute(StateMachine fsm, float dt);
        void Exit(StateMachine fsm);
    }
}
