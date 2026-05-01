using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.VFX
{
    public partial class ParticleInstancesLoader : MonoBehaviour
    {
        // ------------------------------------------------------------------ spawn

        private void SpawnEmitter(ParticlePresetDefinition preset, Vector2 worldPos, ParticleInstanceRecord record)
        {
            float scaleMultiplier = record.ScaleMultiplier > 0f
                ? record.ScaleMultiplier
                : (record.PresetId != null && record.PresetId.StartsWith("portal_", StringComparison.Ordinal) ? 2f : 1f);

            var go = new GameObject($"PE_{preset.id}");
            go.transform.SetParent(_emittersParent, false);
            go.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);

            // Attach identity component so the editor can include this emitter in saves,
            // even when it is inactive due to viewport culling (bug #1 fix).
            var identity = go.AddComponent<PersistedParticleInstance>();
            identity.Restore(record.PresetId, record.Guid, scaleMultiplier);

            var emitter = go.AddComponent<ParticleEmitter>();
            emitter.ApplyPreset(preset, scaleMultiplier);

            _spawnedEmitters.Add(go);
        }

        // ------------------------------------------------------------------ zone helpers

        private static ZoneManager FindZoneManager()
        {
            try
            {
                return UnityEngine.Object.FindObjectOfType<ZoneManager>();
            }
            catch
            {
                return null;
            }
        }
    }
}
