using System;
using System.Collections;
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

            // A preset that names a light carries one. Queued rather than registered here: the
            // WorldLightLoader may not exist yet at this point in the boot, exactly as it may not
            // when a lamp-post building spawns.
            if (!string.IsNullOrEmpty(preset.lightPresetKey))
                _pendingLights.Add(new PendingLight
                {
                    owner     = go,
                    presetKey = preset.lightPresetKey,
                    worldPos  = new Vector3(worldPos.x, worldPos.y + preset.lightHeightOffset, 0f),
                });

            _spawnedEmitters.Add(go);
        }

        /// <summary>An emitter waiting for the light its preset says it emits.</summary>
        private struct PendingLight
        {
            public GameObject owner;
            public string     presetKey;
            public Vector3    worldPos;
        }

        private readonly List<PendingLight> _pendingLights = new List<PendingLight>();

        /// <summary>
        /// Give every emitter that asked for one its own light, once the light loader exists.
        ///
        /// The lights are DERIVED (persistent = false), so they never reach
        /// light_instances.json and cannot duplicate on save — the emitter's own record is
        /// already the authoritative placement. Parenting them to the emitter means one delete
        /// removes both, and a reload rebuilds both together.
        /// </summary>
        private IEnumerator AttachEmittedLights()
        {
            if (_pendingLights.Count == 0) yield break;

            const int maxFrames = 300;   // ~5 s at 60 fps, the same budget BuildingObject uses
            for (int i = 0; i < maxFrames && WorldLightLoader.Instance == null; i++)
                yield return null;

            var loader = WorldLightLoader.Instance;
            if (loader == null)
            {
                Debug.LogWarning($"[ParticleInstancesLoader] {_pendingLights.Count} emitter(s) declare a " +
                                  "light preset but no WorldLightLoader appeared — they stay dark.");
                _pendingLights.Clear();
                yield break;
            }

            int lit = 0;
            foreach (var pending in _pendingLights)
            {
                if (pending.owner == null) continue;   // culled or reloaded while we waited
                if (loader.RegisterDerivedLight(pending.presetKey, pending.worldPos,
                                                pending.owner.transform) != null) lit++;
            }
            _pendingLights.Clear();
            if (lit > 0) Debug.Log($"[ParticleInstancesLoader] {lit} emitter(s) now carry their own light.");
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
