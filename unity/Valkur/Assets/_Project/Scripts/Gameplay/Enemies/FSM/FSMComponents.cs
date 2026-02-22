using UnityEngine;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Pre-resolved component references for FSM states.
    /// Cached once in StateMachine initialization to avoid per-frame GetComponent calls.
    /// States retrieve this via fsm.GetContext&lt;FSMComponents&gt;("components").
    /// </summary>
    public class FSMComponents
    {
        public readonly Rigidbody2D Rb;
        public readonly Health Health;
        public readonly MeleeCombat Combat;
        public readonly SpriteRenderer Sprite;

        public FSMComponents(GameObject owner)
        {
            Rb = owner.GetComponent<Rigidbody2D>();
            Health = owner.GetComponent<Health>();
            Combat = owner.GetComponent<MeleeCombat>();
            Sprite = owner.GetComponentInChildren<SpriteRenderer>();
        }

        public const string KEY = "components";
    }
}
