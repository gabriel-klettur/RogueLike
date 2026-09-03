using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Implemented by any projectile visual (ParticleProjectileVisual,
    /// ElementalProjectileVisual). Lets <see cref="Projectile"/> trigger an impact
    /// effect without coupling to a specific concrete type.
    /// </summary>
    public interface IProjectileVisual
    {
        void OnImpact(Vector3 worldPos);
    }
}
