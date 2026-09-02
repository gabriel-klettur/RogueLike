using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// A world obstacle that can be attacked down, as distinct from an entity that can be
    /// killed.
    ///
    /// <para>WHY IT IS NOT JUST A <see cref="Health"/>. Every damage path in the game finds
    /// its victims through a <c>LayerMask</c>: the player's melee targets NPC(9), a monster's
    /// targets Player(8), and a projectile treats World(11)/Building(14) as ObstacleLayers
    /// that stop it WITHOUT taking damage. A blocking wall has to live on Building — that is
    /// what makes it block — and there is no single layer it could sit on that both the
    /// player's and the monsters' masks contain. So the ice wall shipped with a
    /// <c>Health(100)</c> that nothing in the project could ever reduce: its HP, its hit
    /// flash and its destruction sound were unreachable code, and the wall always died to
    /// its six-second timer.</para>
    ///
    /// <para>This interface plus <see cref="DestructibleObstacleRegistry"/> is the seam. It
    /// is deliberately NOT a mask change: widening melee to Building would make every swing
    /// query every painted collision cell in range.</para>
    /// </summary>
    public interface IDestructibleObstacle
    {
        /// <summary>Where the obstacle is, for range and arc tests.</summary>
        Vector2 ObstaclePosition { get; }

        /// <summary>
        /// World-space extent used to find the contact point. A barrier is long, so measuring
        /// it from its centre would let a swing at one end miss it entirely.
        /// </summary>
        Bounds ObstacleBounds { get; }

        /// <summary>False once it is dying, so a corpse cannot be hit again.</summary>
        bool AcceptsDamage { get; }

        /// <summary>Apply a blow. <paramref name="contactPoint"/> is where it landed.</summary>
        void ApplyObstacleDamage(int amount, GameObject attacker, Vector2 contactPoint, SpellElement? element);
    }
}
