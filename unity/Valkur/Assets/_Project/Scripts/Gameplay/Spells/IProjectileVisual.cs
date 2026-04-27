using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Implemented by any procedural projectile visual (FireballVisual,
    /// ElementalProjectileVisual). Lets <see cref="Projectile"/> trigger an impact
    /// effect without coupling to a specific concrete type.
    /// </summary>
    public interface IProjectileVisual
    {
        void OnImpact(Vector3 worldPos);
    }
}
