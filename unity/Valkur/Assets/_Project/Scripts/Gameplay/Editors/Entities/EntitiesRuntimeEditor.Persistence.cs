using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.FSM;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// Where this editor's map placements go: <see cref="PlacedEntityService"/>.
    ///
    /// <para>This file used to BE the persistence layer — load on the first Update, save by
    /// enumerating live <see cref="PersistedEntityInstance"/>s — and both halves were wrong in ways
    /// that only showed up in play. Saving the scene meant a monster the player killed vanished from
    /// the file the next time anything else was edited (and came back on the next Play when nothing
    /// was), and a monster that wandered was saved where it wandered to. Loading from an editor meant
    /// a release build, which does not create the editors, spawned no placements at all.</para>
    ///
    /// <para>The editor now only AUTHORS: place, move, delete and the respawn time go through the
    /// service, which owns the file, the standing instances and the run's kills.</para>
    /// </summary>
    public partial class EntitiesRuntimeEditor : SingletonMonoBehaviour<EntitiesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        /// <summary>The service, created on demand and handed this editor's catalogue.</summary>
        private PlacedEntityService PlacedEntities
        {
            get
            {
                var service = PlacedEntityService.GetOrCreate();
                service.SetMonsterCatalog(_monsterCatalog);
                return service;
            }
        }

        /// <summary>Write a pending placement edit now, rather than leaving it to the debounce.</summary>
        private void FlushPlacedEntities() => PlacedEntityService.Instance?.FlushSave();

        /// <summary>
        /// The placement a brain stands for, or null for a spawner-made monster, a dungeon monster,
        /// or the corpse of a placement that has been killed.
        /// </summary>
        private static PersistedEntityInstance PlacementOf(FSMMonsterBrain brain)
        {
            if (brain == null) return null;
            var marker = brain.GetComponent<PersistedEntityInstance>();
            return marker != null && !marker.IsDefeated ? marker : null;
        }

        /// <summary>
        /// A placement moved at the author's hand: record where. The GameObject has already been
        /// moved by the caller; this is what makes the move reach the file.
        /// </summary>
        private void NotePlacementMoved(FSMMonsterBrain brain)
        {
            var marker = PlacementOf(brain);
            if (marker == null) return;
            PlacedEntities.SetAuthoredPosition(marker.PlacementId, brain.transform.position);
        }
    }
}
