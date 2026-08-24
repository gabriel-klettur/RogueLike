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
            identity.Restore(record.PresetId, record.Guid, scaleMultiplier, record.Overrides);

            // COPY ON PLACE, and the migration that goes with it. A record written before v4
            // carries no configuration, so it takes one now from the preset it names, with its
            // size ratios folded in: the world renders exactly as it did, and from this moment
            // it is detached from later edits to that asset.
            //
            // The snapshot goes BACK ONTO THE RECORD, and LoadAndSpawn writes the file once
            // when any record needed one. In memory alone the freeze lasts a session: retune
            // the asset, restart, and every un-migrated placement would take the new values —
            // which is the coupling copy-on-place exists to remove, surviving one restart.
            var config = record.Config ?? ParticleInstanceConfig.SnapshotOf(preset, record.Overrides);
            record.Config = config;
            record.ScaleMultiplier = scaleMultiplier;   // the effective one, fallbacks included
            identity.SetConfig(config);

            var emitter = go.AddComponent<ParticleEmitter>();
            emitter.ApplyConfig(preset, config, scaleMultiplier);

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
