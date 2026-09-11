using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// The seam the cross-domain Selection tool reaches this editor through. A PARTIAL for the
    /// reason the Buildings twin records: it shares the class's private members, so nothing
    /// already written changes visibility.
    ///
    /// <para>DELETION HERE IS A REAL DESTROY, and it is the opposite of the Buildings seam.
    /// <c>SaveInstancesToJson</c> collects with <c>FindObjectsOfType&lt;PersistedParticleInstance&gt;
    /// (includeInactive: true)</c> — deliberately, so a viewport-culled emitter is not dropped
    /// from the file — which means deactivating one deletes NOTHING. The undo therefore has to
    /// rebuild the emitter, and <see cref="ParticleRespawnRecipe"/> is what it rebuilds from.</para>
    ///
    /// <para>A DUPLICATE CARRIES THE INSTANCE'S OWN <c>config</c>, never just its preset id.
    /// <c>particles_instances.json</c> is schema v4 and every record holds the copy of the
    /// preset it was placed with; duplicating by id alone would hand the copy whatever the
    /// asset says TODAY, which is the coupling copy-on-place exists to remove, coming back
    /// through the one door that looks harmless.</para>
    /// </summary>
    public partial class ParticlesRuntimeEditor
    {
        /// <summary>
        /// Everything needed to put a deleted emitter back exactly as it was — identity,
        /// GUID, scale, size overrides and the v4 config. Captured BEFORE the destroy,
        /// because none of it is readable afterwards.
        /// </summary>
        internal sealed class ParticleRespawnRecipe
        {
            public string                    PresetId;
            public string                    Guid;
            public float                     ScaleMultiplier;
            public ParticleInstanceOverrides Overrides;
            public ParticleInstanceConfig    Config;
            public Vector3                   Position;
            public string                    Name;
        }

        /// <summary>Every placed emitter the tool may select. Preview emitters are excluded —
        /// they are not parented under the loader and must never be persisted or edited.</summary>
        internal void MultiSelectCollect(List<GameObject> buffer)
        {
            if (buffer == null) return;
            buffer.Clear();
            var all = FindObjectsOfType<PersistedParticleInstance>(includeInactive: true);
            for (int i = 0; i < all.Length; i++)
            {
                var inst = all[i];
                if (inst == null) continue;
                var go = inst.gameObject;
                if (IsPreviewEmitter(go)) continue;
                buffer.Add(go);
            }
        }

        /// <summary>
        /// The emitter's drawn footprint in WORLD space. <see cref="ParticleFootprint.Center"/>
        /// is an offset from the emitter, so it is added to the transform rather than used as
        /// a position. A haze two units across and a dash puff a fifth of one both come back
        /// honestly; the tool applies its own minimum grab size, exactly as
        /// <c>HitTestEmitter</c> does, or the small ones would be unclickable.
        /// </summary>
        internal bool MultiSelectTryGetRect(GameObject go, out Rect rect)
        {
            rect = default;
            if (go == null) return false;

            var emitter = go.GetComponent<ParticleEmitter>();
            var fp = emitter != null ? ParticleFootprint.OfLive(emitter) : ParticleFootprint.Default;

            Vector2 centre = (Vector2)go.transform.position + fp.Center;
            rect = new Rect(centre.x - fp.HalfWidth, centre.y - fp.HalfHeight,
                            fp.HalfWidth * 2f, fp.HalfHeight * 2f);
            return true;
        }

        /// <summary>Move a placed emitter. Its record is recomputed from the transform on the
        /// next save, so there is nothing else to keep in step.</summary>
        internal void MultiSelectApplyMove(GameObject go, Vector3 worldPos)
        {
            if (go == null) return;
            go.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);
        }

        /// <summary>Capture everything needed to rebuild <paramref name="go"/>, before it dies.</summary>
        internal ParticleRespawnRecipe MultiSelectCapture(GameObject go)
        {
            if (go == null) return null;
            var identity = go.GetComponent<PersistedParticleInstance>();
            if (identity == null) return null;

            return new ParticleRespawnRecipe
            {
                PresetId        = identity.PresetId,
                Guid            = identity.StableGuid,
                ScaleMultiplier = identity.ScaleMultiplier,
                Overrides       = identity.Overrides,
                Config          = identity.Config,
                Position        = go.transform.position,
                Name            = go.name,
            };
        }

        /// <summary>Destroy a placed emitter. Pair every call with a captured recipe.</summary>
        internal void MultiSelectDestroy(GameObject go)
        {
            if (go == null) return;
            if (go == _activeInstance) SetActiveInstance(null);
            SafeDestroy.Of(go);
        }

        /// <summary>
        /// Rebuild an emitter from a recipe. Used both by the tool's undo (same GUID, same
        /// place) and by its duplicate (fresh GUID, new place), which is why the GUID and the
        /// position are parameters rather than read off the recipe.
        /// </summary>
        internal GameObject MultiSelectRespawn(ParticleRespawnRecipe recipe, Vector3 worldPos, string guid)
        {
            if (recipe == null || string.IsNullOrEmpty(recipe.PresetId)) return null;
            var preset = _catalog != null ? _catalog.GetById(recipe.PresetId) : null;
            if (preset == null) return null;

            var loader = FindObjectOfType<ParticleInstancesLoader>();
            Transform parent = loader != null ? loader.transform : null;

            var go = new GameObject(string.IsNullOrEmpty(recipe.Name) ? $"PE_{preset.id}" : recipe.Name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);

            var identity = go.AddComponent<PersistedParticleInstance>();
            identity.Restore(recipe.PresetId, guid, recipe.ScaleMultiplier, recipe.Overrides);

            // A recipe taken from a v1-v3 placement the loader had not yet frozen carries no
            // config; snapshotting the preset here is the same migration the loader performs,
            // so the copy is detached from later asset edits either way.
            var config = recipe.Config ?? ParticleInstanceConfig.SnapshotOf(preset, recipe.Overrides);
            identity.SetConfig(config);

            var emitter = go.AddComponent<ParticleEmitter>();
            emitter.ApplyConfig(preset, config, recipe.ScaleMultiplier);
            return go;
        }

        /// <summary>Select this emitter in this editor, so a double-click from the Selection
        /// tool lands on it with its properties already open.</summary>
        internal void MultiSelectFocus(GameObject go)
        {
            if (go == null) return;
            SetActiveInstance(go);
        }

        /// <summary>
        /// Write the particles file. <paramref name="afterDeletion"/> lifts the anti-wipe
        /// guard for exactly one write, which is what <c>ExecuteDeletionEdit</c> does for the
        /// editor's own deletes: a group delete legitimately lowers the count, and without
        /// this the guard reads that as the scene having been torn down and refuses.
        /// </summary>
        internal void MultiSelectPersist(bool afterDeletion)
        {
            if (afterDeletion) _allowEmptyWriteOnce = true;
            try
            {
                MarkInstanceDataDirty();
                PersistDirtyInstanceChanges("Selection tool", force: true);
            }
            finally { _allowEmptyWriteOnce = false; }
        }
    }
}
