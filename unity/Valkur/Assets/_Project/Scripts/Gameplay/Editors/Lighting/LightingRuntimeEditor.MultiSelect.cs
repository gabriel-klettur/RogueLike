using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// The seam the cross-domain Selection tool reaches this editor through. A PARTIAL for the
    /// reason the Buildings twin records.
    ///
    /// <para>This is the thinnest of the three, because <see cref="WorldLightLoader"/> already
    /// exposes the whole verb set publicly — <c>CollectActiveLights</c>, <c>MoveLight</c>,
    /// <c>RemoveLight</c> and the <c>CaptureLight</c>/<c>RestoreLight</c> pair that IS an
    /// undo record. What the loader does NOT own is <see cref="_authoredRemovals"/>, the
    /// counter the save's anti-wipe guard reads to tell "the author deleted six lights" from
    /// "the world was torn down underneath us"; every delete here has to move it, or a group
    /// delete followed by a save is refused as a suspected wipe.</para>
    ///
    /// <para>ONLY AUTHORED LIGHTS ARE SELECTABLE. A light emitted by a lamp-post building is
    /// registered with <c>persistent = false</c>: it is not in <c>light_instances.json</c>,
    /// a save never writes it, and deleting one does nothing the next world load does not
    /// undo. The Instances list already excludes them for the same reason.</para>
    /// </summary>
    public partial class LightingRuntimeEditor
    {
        /// <summary>The grab box for a light, in world units. A light has no footprint of its
        /// own that means anything here — its outer radius is how far it REACHES, not how big
        /// it is — so the marker is a fixed handle the author can hit at any zoom.</summary>
        internal const float LIGHT_HANDLE_HALF_EXTENT = 0.45f;

        /// <summary>Every AUTHORED light the tool may select.</summary>
        internal void MultiSelectCollect(List<GameObject> buffer)
        {
            if (buffer == null) return;
            buffer.Clear();

            var loader = WorldLightLoader.Instance;
            if (loader == null) return;

            var handles = new List<WorldLightLoader.LightHandle>();
            loader.CollectActiveLights(handles);
            for (int i = 0; i < handles.Count; i++)
            {
                var h = handles[i];
                if (h.Go == null || !h.Persistent) continue;
                buffer.Add(h.Go);
            }
        }

        /// <summary>The fixed grab handle centred on the light.</summary>
        internal bool MultiSelectTryGetRect(GameObject go, out Rect rect)
        {
            rect = default;
            if (go == null) return false;
            var p = go.transform.position;
            rect = new Rect(p.x - LIGHT_HANDLE_HALF_EXTENT, p.y - LIGHT_HANDLE_HALF_EXTENT,
                            LIGHT_HANDLE_HALF_EXTENT * 2f, LIGHT_HANDLE_HALF_EXTENT * 2f);
            return true;
        }

        internal void MultiSelectApplyMove(GameObject go, Vector3 worldPos)
        {
            var loader = WorldLightLoader.Instance;
            if (loader == null || go == null) return;
            loader.MoveLight(go, new Vector3(worldPos.x, worldPos.y, go.transform.position.z));
        }

        /// <summary>Capture a light so the tool's undo can put it back with its own id.</summary>
        internal WorldLightLoader.LightSnapshot MultiSelectCapture(GameObject go)
        {
            var loader = WorldLightLoader.Instance;
            return loader != null && go != null ? loader.CaptureLight(go) : null;
        }

        /// <summary>
        /// Remove an authored light and COUNT it. The count is what lets the next save tell a
        /// deliberate deletion from a torn-down world; without it a group delete of six lights
        /// is refused by the guard as a suspected wipe.
        /// </summary>
        internal void MultiSelectRemove(GameObject go)
        {
            var loader = WorldLightLoader.Instance;
            if (loader == null || go == null) return;
            if (go == _selectedLight) _selectedLight = null;
            loader.RemoveLight(go);
            _authoredRemovals++;
        }

        /// <summary>
        /// Bring a captured light back. Decrements the removal budget the delete spent, so an
        /// undo leaves the guard's arithmetic exactly where it started — otherwise a
        /// delete-then-undo would leave the next save permitted to lose six records it no
        /// longer has any reason to expect.
        /// </summary>
        internal GameObject MultiSelectRestore(WorldLightLoader.LightSnapshot snapshot)
        {
            var loader = WorldLightLoader.Instance;
            if (loader == null || snapshot == null) return null;
            var go = loader.RestoreLight(snapshot);
            if (go != null && _authoredRemovals > 0) _authoredRemovals--;
            return go;
        }

        /// <summary>Place a copy of a captured light at <paramref name="worldPos"/>. The
        /// snapshot's own id is cleared, so <c>RestoreLight</c> mints a fresh one rather than
        /// shipping two records under one id.</summary>
        internal GameObject MultiSelectDuplicate(WorldLightLoader.LightSnapshot snapshot, Vector3 worldPos)
        {
            var loader = WorldLightLoader.Instance;
            if (loader == null || snapshot == null) return null;

            snapshot.Id = 0;
            var go = loader.RestoreLight(snapshot);
            if (go != null) loader.MoveLight(go, new Vector3(worldPos.x, worldPos.y, go.transform.position.z));
            return go;
        }

        /// <summary>Select this light in this editor. Goes through the editor's own
        /// <c>FocusLight</c> so the instances list, the outline and the properties panel all
        /// agree — writing <c>_selectedLight</c> directly would move one of the three.</summary>
        internal void MultiSelectFocus(GameObject go)
        {
            if (go == null) return;
            FocusLight(go);
        }

        /// <summary>Write the lights file. Routed through the editor's own save so the
        /// interior refusal, the anti-wipe guard and the removal budget all still apply.</summary>
        internal void MultiSelectPersist() => DoSave(auto: true);
    }
}
