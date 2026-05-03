using System;
using UnityEngine;

namespace Valkur.Gameplay.Combat.Death
{
    /// <summary>
    /// Single-source-of-truth flag for "the player is currently a spirit walking
    /// to the resurrection altar". Set/cleared by <see cref="DeathSequenceController"/>;
    /// consumed by FSM aggro states, NPC auto-cast, Health damage gating, and
    /// the player's own combat poll loop.
    ///
    /// Attached to the Player GameObject by <see cref="EntitySetup.ConfigurePlayer"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerSpiritState : MonoBehaviour
    {
        private bool _isSpirit;

        public bool IsSpirit => _isSpirit;

        public event Action<bool> OnSpiritStateChanged;

        public void EnterSpirit()
        {
            if (_isSpirit) return;
            _isSpirit = true;
            OnSpiritStateChanged?.Invoke(true);
        }

        public void ExitSpirit()
        {
            if (!_isSpirit) return;
            _isSpirit = false;
            OnSpiritStateChanged?.Invoke(false);
        }
    }
}
