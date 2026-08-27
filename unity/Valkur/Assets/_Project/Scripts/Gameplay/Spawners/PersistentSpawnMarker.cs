using UnityEngine;

namespace Valkur.Gameplay.Spawners
{
    /// <summary>
    /// Marks a monster GameObject as spawned from a
    /// <see cref="Valkur.Data.SpawnerTemplateData"/> whose <c>persistent</c> flag is set —
    /// exempting it from <see cref="Valkur.Gameplay.MonsterSpawner"/>'s distance-based despawn
    /// sweep (<c>despawnRadius</c>, 100 world units from the player by default).
    ///
    /// Every shipped vendor respawn template (banker, blacksmith, alchemist, …) carries
    /// <c>persistent = true</c>; before this marker existed the field had zero readers, so a
    /// vendor the player walked more than 100 units away from was silently destroyed on the
    /// next <see cref="MonsterSpawner"/> tick like any ordinary hostile.
    ///
    /// Attached by <see cref="MonsterSpawner.SpawnEntity"/> when its caller passes
    /// <c>persistent: true</c> — <see cref="SpawnerInstance"/> does this for every entity a
    /// persistent template spawns. See <see cref="MonsterSpawner.IsExemptFromDespawn"/> for the
    /// other exemption (F5-placed entities, via
    /// <see cref="Valkur.Gameplay.Entities.PersistedEntityInstance"/>) and why the two are kept
    /// as separate markers rather than reusing one for both: they answer different questions
    /// ("should this be saved to entities_instances.json?" vs "should this survive the distance
    /// cull?") that happen to overlap today but are not the same question.
    /// </summary>
    public sealed class PersistentSpawnMarker : MonoBehaviour
    {
    }
}
