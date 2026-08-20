using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// Interface for the VFX service, allowing decoupled access via ServiceLocator.
    /// </summary>
    public interface IVFXService
    {
        void SpawnImpact(Vector3 position, Color color, float duration = 0.3f, float scale = 1f);
        void SpawnAreaIndicator(Vector3 position, Color color, float radius = 2f, float duration = 0.5f);
        /// <summary>
        /// Spawn a particle preset by id at world position.
        /// duration &lt; 0 uses the preset's own lifespan (+1 s buffer).
        /// Returns the spawned GameObject (or null on failure) so callers that
        /// need to drive the emitter — animate its position to leave a trail,
        /// parent it to a moving entity, or call StopEmitting early — can do
        /// so. Existing callers that ignore the return value are unaffected.
        /// Maps to Python's per-emitter systems (healing_aura, dash_trail, fireball_trail, etc.).
        /// </summary>
        GameObject SpawnParticlePreset(string presetId, Vector3 position, float duration = -1f, float scale = 1f);
        GameObject Spawn(string key, Vector3 position, Quaternion rotation);
        void Despawn(string key, GameObject obj);
        void RegisterPrefab(string key, GameObject prefab, int warmCount = 0);
    }
}
