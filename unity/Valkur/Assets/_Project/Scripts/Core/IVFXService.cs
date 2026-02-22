using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// Interface for the VFX service, allowing decoupled access via ServiceLocator.
    /// </summary>
    public interface IVFXService
    {
        void SpawnImpact(Vector3 position, Color color, float duration = 0.3f, float scale = 1f);
        void SpawnSlashArc(Vector3 position, Vector2 direction, Color color, float arc = 90f, float radius = 1.5f, float duration = 0.2f);
        void SpawnAreaIndicator(Vector3 position, Color color, float radius = 2f, float duration = 0.5f);
        GameObject Spawn(string key, Vector3 position, Quaternion rotation);
        void Despawn(string key, GameObject obj);
        void RegisterPrefab(string key, GameObject prefab, int warmCount = 0);
    }
}
