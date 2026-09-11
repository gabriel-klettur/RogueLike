using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.VFX;
using Valkur.Gameplay.World;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.MultiSelect
{
    /// <summary>
    /// The three shipped domains. They live in one file because each is a thin translation of
    /// <see cref="ISelectionDomain"/> onto its editor's own <c>MultiSelect</c> seam — reading
    /// them side by side is what makes the asymmetries between the three obvious, and those
    /// asymmetries are the interesting part:
    ///
    /// <list type="bullet">
    /// <item>a building is DEACTIVATED to delete it (its save skips inactive objects);</item>
    /// <item>an emitter is DESTROYED (its save deliberately includes inactive ones, so
    /// deactivating one would delete nothing);</item>
    /// <item>a light is REMOVED through the loader and counted against the save's
    /// anti-wipe budget.</item>
    /// </list>
    ///
    /// <para>None of that is visible from the tool, which is the point of the interface.</para>
    /// </summary>
    internal sealed class BuildingSelectionDomain : ISelectionDomain
    {
        public string Id    => "building";
        public string Label         => "Edificios";
        public string LabelSingular => "edificio";
        public Color  Tint  => UITheme.DOMAIN_BUILDING;

        private static BuildingsRuntimeEditor Editor => BuildingsRuntimeEditor.Instance;
        public bool Available => Editor != null;

        private readonly List<BuildingObject> _scratch = new List<BuildingObject>(256);

        public void Collect(List<GameObject> buffer)
        {
            buffer.Clear();
            var ed = Editor;
            if (ed == null) return;
            ed.MultiSelectCollect(_scratch);
            for (int i = 0; i < _scratch.Count; i++)
                if (_scratch[i] != null) buffer.Add(_scratch[i].gameObject);
        }

        public bool TryGetRect(GameObject go, out Rect rect)
        {
            rect = default;
            var ed = Editor;
            var b  = go != null ? go.GetComponent<BuildingObject>() : null;
            return ed != null && b != null && ed.MultiSelectTryGetRect(b, out rect);
        }

        public string Describe(GameObject go)
        {
            var b = go != null ? go.GetComponent<BuildingObject>() : null;
            if (b == null) return "edificio";
            string tpl = b.Template != null ? "#" + b.Template.templateId : "sin plantilla";
            return $"Edificio {b.InstanceId} ({tpl})";
        }

        public Vector3 PositionOf(GameObject go) => go != null ? go.transform.position : Vector3.zero;

        public void MoveTo(GameObject go, Vector3 worldPos)
        {
            var b = go != null ? go.GetComponent<BuildingObject>() : null;
            if (b != null) Editor?.MultiSelectApplyMove(b, worldPos);
        }

        public object Delete(GameObject go)
        {
            var b = go != null ? go.GetComponent<BuildingObject>() : null;
            if (b == null) return null;
            Editor?.MultiSelectSetAlive(b, false);
            return b;
        }

        public GameObject Restore(object token)
        {
            if (!(token is BuildingObject b) || b == null) return null;
            Editor?.MultiSelectSetAlive(b, true);
            return b.gameObject;
        }

        public GameObject Duplicate(GameObject go, Vector3 worldPos)
        {
            var b = go != null ? go.GetComponent<BuildingObject>() : null;
            if (b == null) return null;
            var copy = Editor?.MultiSelectDuplicate(b, worldPos);
            return copy != null ? copy.gameObject : null;
        }

        public void DiscardDuplicate(GameObject go)
        {
            var b = go != null ? go.GetComponent<BuildingObject>() : null;
            if (b != null) Editor?.MultiSelectDiscardDuplicate(b);
        }

        public object CaptureForClipboard(GameObject go)
        {
            var b = go != null ? go.GetComponent<BuildingObject>() : null;
            return b != null ? Editor?.MultiSelectCaptureForClipboard(b) : null;
        }

        public GameObject SpawnFromClipboard(object token, Vector3 worldPos)
        {
            var copy = Editor?.MultiSelectSpawnFromClipboard(token, worldPos);
            return copy != null ? copy.gameObject : null;
        }

        public void CollectGhostSprites(GameObject go, List<SpriteRenderer> buffer)
        {
            var b = go != null ? go.GetComponent<BuildingObject>() : null;
            if (b != null) Editor?.MultiSelectCollectGhostSprites(b, buffer);
        }

        public bool OpenEditorFor(GameObject go, GameEditorManager.IGameEditor returnTo)
        {
            var ed = Editor;
            var b  = go != null ? go.GetComponent<BuildingObject>() : null;
            var mgr = GameEditorManager.HasInstance ? GameEditorManager.Instance : null;
            if (ed == null || b == null || mgr == null) return false;

            mgr.OpenExclusive(ed, returnTo);
            ed.MultiSelectFocus(b);
            return true;
        }

        public void Persist(bool afterDeletion) => Editor?.MultiSelectPersist();
    }

    internal sealed class ParticleSelectionDomain : ISelectionDomain
    {
        public string Id    => "particle";
        public string Label         => "Particulas";
        public string LabelSingular => "particula";
        public Color  Tint  => UITheme.DOMAIN_PARTICLE;

        private static ParticlesRuntimeEditor Editor => ParticlesRuntimeEditor.Instance;
        public bool Available => Editor != null;

        public void Collect(List<GameObject> buffer)
        {
            buffer.Clear();
            Editor?.MultiSelectCollect(buffer);
        }

        public bool TryGetRect(GameObject go, out Rect rect)
        {
            rect = default;
            var ed = Editor;
            return ed != null && ed.MultiSelectTryGetRect(go, out rect);
        }

        public string Describe(GameObject go)
        {
            var id = go != null ? go.GetComponent<PersistedParticleInstance>() : null;
            return id != null && !string.IsNullOrEmpty(id.PresetId) ? id.PresetId : "particula";
        }

        public Vector3 PositionOf(GameObject go) => go != null ? go.transform.position : Vector3.zero;

        public void MoveTo(GameObject go, Vector3 worldPos) => Editor?.MultiSelectApplyMove(go, worldPos);

        public object Delete(GameObject go)
        {
            var ed = Editor;
            if (ed == null || go == null) return null;
            // Capture BEFORE the destroy: none of the identity is readable afterwards.
            var recipe = ed.MultiSelectCapture(go);
            ed.MultiSelectDestroy(go);
            return recipe;
        }

        public GameObject Restore(object token)
        {
            var ed = Editor;
            if (ed == null || !(token is ParticlesRuntimeEditor.ParticleRespawnRecipe r)) return null;
            // Same GUID and same place: the record on disk stays the same record.
            return ed.MultiSelectRespawn(r, r.Position, r.Guid);
        }

        public GameObject Duplicate(GameObject go, Vector3 worldPos)
        {
            var ed = Editor;
            if (ed == null || go == null) return null;
            var recipe = ed.MultiSelectCapture(go);
            // An EMPTY guid, so Restore mints a fresh one — two placements under one GUID
            // would be one record on disk, and the duplicate would vanish on the next load.
            return recipe != null ? ed.MultiSelectRespawn(recipe, worldPos, string.Empty) : null;
        }

        public void DiscardDuplicate(GameObject go) => Editor?.MultiSelectDestroy(go);

        public object CaptureForClipboard(GameObject go) => Editor?.MultiSelectCapture(go);

        public GameObject SpawnFromClipboard(object token, Vector3 worldPos)
        {
            var ed = Editor;
            if (ed == null || !(token is ParticlesRuntimeEditor.ParticleRespawnRecipe r)) return null;
            // An EMPTY guid, so the respawn mints a fresh one. Two placements under one GUID
            // are one record on disk, and the paste would vanish on the next load.
            return ed.MultiSelectRespawn(r, worldPos, string.Empty);
        }

        /// <summary>An emitter draws through a ParticleSystemRenderer, not a SpriteRenderer, so
        /// it contributes no silhouette and the ghost shows its footprint box instead.</summary>
        public void CollectGhostSprites(GameObject go, List<SpriteRenderer> buffer) { }

        public bool OpenEditorFor(GameObject go, GameEditorManager.IGameEditor returnTo)
        {
            var ed  = Editor;
            var mgr = GameEditorManager.HasInstance ? GameEditorManager.Instance : null;
            if (ed == null || go == null || mgr == null) return false;

            mgr.OpenExclusive(ed, returnTo);
            ed.MultiSelectFocus(go);
            return true;
        }

        public void Persist(bool afterDeletion) => Editor?.MultiSelectPersist(afterDeletion);
    }

    internal sealed class LightSelectionDomain : ISelectionDomain
    {
        public string Id    => "light";
        public string Label         => "Luces";
        public string LabelSingular => "luz";
        public Color  Tint  => UITheme.DOMAIN_LIGHT;

        private static LightingRuntimeEditor Editor => LightingRuntimeEditor.Instance;
        public bool Available => Editor != null && WorldLightLoader.Instance != null;

        public void Collect(List<GameObject> buffer)
        {
            buffer.Clear();
            Editor?.MultiSelectCollect(buffer);
        }

        public bool TryGetRect(GameObject go, out Rect rect)
        {
            rect = default;
            var ed = Editor;
            return ed != null && ed.MultiSelectTryGetRect(go, out rect);
        }

        public string Describe(GameObject go)
        {
            var loader = WorldLightLoader.Instance;
            string preset = loader != null && go != null ? loader.GetLightPresetKey(go) : null;
            return string.IsNullOrEmpty(preset) ? "luz" : "Luz " + preset;
        }

        public Vector3 PositionOf(GameObject go) => go != null ? go.transform.position : Vector3.zero;

        public void MoveTo(GameObject go, Vector3 worldPos) => Editor?.MultiSelectApplyMove(go, worldPos);

        public object Delete(GameObject go)
        {
            var ed = Editor;
            if (ed == null || go == null) return null;
            var snapshot = ed.MultiSelectCapture(go);
            ed.MultiSelectRemove(go);
            return snapshot;
        }

        public GameObject Restore(object token)
        {
            var ed = Editor;
            if (ed == null || !(token is WorldLightLoader.LightSnapshot s)) return null;
            return ed.MultiSelectRestore(s);
        }

        public GameObject Duplicate(GameObject go, Vector3 worldPos)
        {
            var ed = Editor;
            if (ed == null || go == null) return null;
            var snapshot = ed.MultiSelectCapture(go);
            return snapshot != null ? ed.MultiSelectDuplicate(snapshot, worldPos) : null;
        }

        public void DiscardDuplicate(GameObject go) => Editor?.MultiSelectRemove(go);

        public object CaptureForClipboard(GameObject go)
        {
            var snapshot = Editor?.MultiSelectCapture(go);
            // Zero the id at CAPTURE. A paste always wants a new record, and leaving the
            // source id on would make the first paste silently adopt it whenever the original
            // had since been deleted — and log a duplicate-id warning on every paste after.
            if (snapshot != null) snapshot.Id = 0;
            return snapshot;
        }

        public GameObject SpawnFromClipboard(object token, Vector3 worldPos)
        {
            var ed = Editor;
            if (ed == null || !(token is WorldLightLoader.LightSnapshot s)) return null;
            return ed.MultiSelectDuplicate(s, worldPos);
        }

        /// <summary>A light has no sprite of its own; the ghost draws its grab handle.</summary>
        public void CollectGhostSprites(GameObject go, List<SpriteRenderer> buffer) { }

        public bool OpenEditorFor(GameObject go, GameEditorManager.IGameEditor returnTo)
        {
            var ed  = Editor;
            var mgr = GameEditorManager.HasInstance ? GameEditorManager.Instance : null;
            if (ed == null || go == null || mgr == null) return false;

            mgr.OpenExclusive(ed, returnTo);
            ed.MultiSelectFocus(go);
            return true;
        }

        public void Persist(bool afterDeletion) => Editor?.MultiSelectPersist();
    }
}
