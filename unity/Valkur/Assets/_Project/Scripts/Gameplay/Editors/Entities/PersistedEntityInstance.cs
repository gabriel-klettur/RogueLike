using UnityEngine;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// Marks a monster GameObject as placed through the Entities runtime editor (F5) and
    /// therefore owed a place in <c>StreamingAssets/Entities/entities_instances.json</c>.
    ///
    /// Monsters created by the dungeon generator, an F3 <c>SpawnerInstance</c>'s wave, or a
    /// boss hand-off never carry this component — each of those already persists (or
    /// deliberately does not persist) through its own file. <c>EntitiesRuntimeEditor</c>'s save
    /// path enumerates every LIVE instance of this component via <c>FindObjectsOfType</c>, so it
    /// writes exactly what F5 put down, the same "the scene is the source of truth at save time"
    /// pattern <c>SpawnerInstance</c> / <c>PersistedParticleInstance</c> already use — never a
    /// spawner's own population.
    ///
    /// Deliberately not used for player placements: F5's Players tab spawns a throwaway class
    /// preview and refuses a second live player outright
    /// (<see cref="EntitiesRuntimeEditor.SpawnPlayerAt"/>); persisting one across a Stop would
    /// recreate the exact "two players" hazard that guard exists to prevent.
    /// </summary>
    public sealed class PersistedEntityInstance : MonoBehaviour
    {
        [Tooltip("Stable id generated the first time this monster was placed. Preserved across " +
                 "a save/load round trip so re-saving an untouched placement is not read as a " +
                 "delete-and-recreate.")]
        [SerializeField] private string _placementId;

        [Tooltip("MonsterDefinition.monsterKey this instance was placed from.")]
        [SerializeField] private string _monsterKey;

        public string PlacementId => _placementId;
        public string MonsterKey  => _monsterKey;

        /// <summary>Sets the identity fields. <paramref name="placementId"/> empty/null mints a
        /// fresh id — the path a brand-new placement takes; a non-empty id is what a boot-time
        /// reload passes to keep the same record instead of minting a new one every session.</summary>
        public void Initialize(string placementId, string monsterKey)
        {
            _placementId = string.IsNullOrEmpty(placementId)
                ? System.Guid.NewGuid().ToString("N")
                : placementId;
            _monsterKey = monsterKey;

            // The identity arrives AFTER EntitySetup.ConfigureMonster built this entity's
            // FSM, so this is the first moment a by_eid override in assignments.json is
            // knowable. No-ops unless one is actually authored for this id.
            GetComponent<Valkur.Gameplay.FSM.FSMMonsterBrain>()
                ?.RebindFsmForPlacement(_placementId);
        }
    }
}
