using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// What a rename is about to break, said BEFORE it happens.
    ///
    /// <para><c>monsterKey</c> is not a label. It is the join key three other subsystems point at
    /// by STRING, none of which this editor can fix up: <c>StreamingAssets/FSM/assignments.json</c>
    /// resolves a monster's brain through <c>by_archetype</c>, every spawner's wave list names the
    /// entity to spawn by it, and each placed entity on the map stores it. Re-keying is therefore
    /// a refactor wearing a text field, and its failure mode is the quietest one this project
    /// has: the monster keeps working, but it boots a bare <c>IdleState</c> with no transitions
    /// and no allowed-state guard, and the camp that used to spawn it spawns nothing.</para>
    ///
    /// <para>So: count the references, show them, and require a second press. Not a modal — the
    /// author is mid-gesture with a key already typed, and a dialog that steals focus loses it.
    /// The arming is cleared by anything that changes what the rename would mean, because a
    /// warning about the previous selection is worse than no warning.</para>
    /// </summary>
    public partial class EntitiesRuntimeEditor
    {
        private string _renameArmedForKey;

        /// <summary>
        /// Rename, or — the first time, when something points at the old key — report and arm.
        /// </summary>
        private void OnRenameRequested()
        {
            if (string.IsNullOrEmpty(_selectedKey) || _selectedIsPlayer)
            {
                SetStatus("Select a monster in the Picker to rename.");
                return;
            }

            if (_renameArmedForKey == _selectedKey)
            {
                _renameArmedForKey = null;
                RenameSelectedDefinition(_pendingKeyInput);
                return;
            }

            string report = DescribeKeyReferences(_selectedKey);
            if (report == null)
            {
                // Nothing points at it: renaming is safe and asking twice would be ceremony.
                RenameSelectedDefinition(_pendingKeyInput);
                return;
            }

            _renameArmedForKey = _selectedKey;
            SetStatus($"'{_selectedKey}' is referenced by {report}. Those point at the key by " +
                      "STRING and will not follow it. Press Rename again to confirm.");
        }

        /// <summary>Forget an arming that no longer describes what the button would do.</summary>
        private void DisarmRename() => _renameArmedForKey = null;

        /// <summary>
        /// A human-readable list of what names <paramref name="key"/>, or null when nothing does.
        ///
        /// <para>Counted from the live data rather than from a list of subsystems kept by hand:
        /// the FSM layer is asked whether it has a set for this archetype, the spawner catalogue
        /// is walked for wave entries, and the scene is walked for placements. A fourth consumer
        /// added later will be missing from here, which is why the message says which three it
        /// checked rather than claiming to be exhaustive.</para>
        /// </summary>
        private string DescribeKeyReferences(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            var parts = new List<string>();

            if (Valkur.Gameplay.Enemies.FSM.FSMRuntimeFactory.HasSetForArchetype(key))
                parts.Add("an FSM assignment");

            int waves = CountSpawnerWaveReferences(key);
            if (waves > 0) parts.Add($"{waves} spawner wave entr{(waves == 1 ? "y" : "ies")}");

            int placed = CountPlacedInstances(key);
            if (placed > 0) parts.Add($"{placed} placement{(placed == 1 ? "" : "s")} on the map");

            if (parts.Count == 0) return null;

            var sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) sb.Append(i == parts.Count - 1 ? " and " : ", ");
                sb.Append(parts[i]);
            }
            return sb.ToString();
        }

        private static int CountSpawnerWaveReferences(string key)
        {
            var catalog = Resources.Load<SpawnerTemplateCatalog>("SpawnerTemplateCatalog");
            if (catalog == null)
            {
                foreach (var found in Resources.FindObjectsOfTypeAll<SpawnerTemplateCatalog>())
                {
                    catalog = found;
                    break;
                }
            }
            if (catalog?.Templates == null) return 0;

            int count = 0;
            for (int t = 0; t < catalog.Templates.Count; t++)
            {
                var template = catalog.Templates[t];
                if (template?.waves == null) continue;
                for (int w = 0; w < template.waves.Count; w++)
                {
                    var wave = template.waves[w];
                    if (wave?.spawns == null) continue;
                    for (int e = 0; e < wave.spawns.Count; e++)
                    {
                        var entry = wave.spawns[e];
                        if (entry == null) continue;
                        if (string.Equals(entry.entityId, key, System.StringComparison.Ordinal))
                            count++;
                    }
                }
            }
            return count;
        }

        private static int CountPlacedInstances(string key)
        {
            int count = 0;

            // The AUTHORED placements, not the standing ones: a placement the player has killed
            // is still in the file and still joins to this key, and a corpse still carrying its
            // marker is not a second placement.
            var service = PlacedEntityService.Instance;
            if (service != null && service.IsLoaded)
            {
                foreach (var record in service.Records)
                    if (record != null && string.Equals(record.MonsterKey, key, System.StringComparison.Ordinal))
                        count++;
                foreach (var record in service.UnresolvedRecords)
                    if (record != null && string.Equals(record.MonsterKey, key, System.StringComparison.Ordinal))
                        count++;
                return count;
            }

            foreach (var marker in Object.FindObjectsOfType<PersistedEntityInstance>())
            {
                if (marker == null || marker.IsDefeated) continue;
                if (string.Equals(marker.MonsterKey, key, System.StringComparison.Ordinal))
                    count++;
            }
            return count;
        }
    }
}
