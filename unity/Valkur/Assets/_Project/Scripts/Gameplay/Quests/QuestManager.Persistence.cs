using System.Collections.Generic;
using Valkur.Data;

namespace Valkur.Gameplay.Quests
{
    /// <summary>
    /// The bridge between the manager's own <see cref="QuestManager.Snapshot"/> and
    /// the save document's <see cref="QuestSaveData"/>.
    ///
    /// <para>Two shapes and not one, because they answer to different masters. The
    /// snapshot is the manager's natural form — a jagged list of per-quest counters —
    /// and the save document has to survive <c>JsonUtility</c>, which refuses a
    /// jagged array outright. So the counters are FLATTENED end to end with a
    /// parallel list of run lengths, the same trick the progression document uses for
    /// its parallel id/rank lists.</para>
    ///
    /// <para>The flattening is the part that can silently corrupt, so it is written
    /// once here rather than at the collector and the restorer — two copies of a
    /// pack/unpack pair is how one of them ends up off by one and a reloaded quest
    /// comes back with another quest's progress in it.</para>
    /// </summary>
    public sealed partial class QuestManager
    {
        /// <summary>Pack the live state into the save document.</summary>
        public void WriteTo(QuestSaveData data)
        {
            if (data == null) return;

            data.activeQuestIds.Clear();
            data.completedQuestIds.Clear();
            data.activeObjectiveCounts.Clear();
            data.activeProgress.Clear();

            var snap = ToSnapshot();

            for (int i = 0; i < snap.activeQuestIds.Count; i++)
            {
                data.activeQuestIds.Add(snap.activeQuestIds[i]);

                var counters = i < snap.activeProgress.Count ? snap.activeProgress[i] : null;
                int n = counters?.Length ?? 0;
                data.activeObjectiveCounts.Add(n);
                for (int j = 0; j < n; j++) data.activeProgress.Add(counters[j]);
            }

            for (int i = 0; i < snap.completedQuestIds.Count; i++)
                data.completedQuestIds.Add(snap.completedQuestIds[i]);
        }

        /// <summary>
        /// Unpack the save document over the live state, resolving quest ids against
        /// <paramref name="catalog"/>.
        ///
        /// <para>A run length that overruns the flattened list is TRUNCATED rather
        /// than throwing: a hand-edited or half-written save should cost the player
        /// one quest's progress, not the whole load.</para>
        /// </summary>
        public void ReadFrom(QuestSaveData data, IReadOnlyList<QuestDefinition> catalog)
        {
            var snap = new Snapshot();
            if (data != null)
            {
                if (data.completedQuestIds != null)
                    snap.completedQuestIds.AddRange(data.completedQuestIds);

                int cursor = 0;
                int count = data.activeQuestIds?.Count ?? 0;
                for (int i = 0; i < count; i++)
                {
                    snap.activeQuestIds.Add(data.activeQuestIds[i]);

                    int n = (data.activeObjectiveCounts != null && i < data.activeObjectiveCounts.Count)
                        ? data.activeObjectiveCounts[i] : 0;
                    int available = (data.activeProgress?.Count ?? 0) - cursor;
                    if (n > available) n = available < 0 ? 0 : available;

                    var counters = new int[n];
                    for (int j = 0; j < n; j++) counters[j] = data.activeProgress[cursor + j];
                    cursor += n;

                    snap.activeProgress.Add(counters);
                }
            }

            FromSnapshot(snap, catalog);
        }
    }
}
