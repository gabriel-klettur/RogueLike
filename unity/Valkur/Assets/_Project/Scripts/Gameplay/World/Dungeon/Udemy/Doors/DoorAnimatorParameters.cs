using UnityEngine;

namespace Valkur.Gameplay.World.Dungeon.Udemy.Doors
{
    /// <summary>
    /// Cached animator parameter hashes used by <see cref="Door"/>. Centralized
    /// so a future rename of the Animator parameter only touches one place.
    /// </summary>
    public static class DoorAnimatorParameters
    {
        /// <summary>"open" Animator bool — true = play open clip, false = play closed clip.</summary>
        public static readonly int Open = Animator.StringToHash("open");
    }
}
