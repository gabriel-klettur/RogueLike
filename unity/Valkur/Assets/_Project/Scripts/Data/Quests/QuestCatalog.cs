using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Every quest in the game, in one asset.
    ///
    /// <para>It lives under <c>Resources/Quests/</c> and not in <c>Data/Catalogs/</c>
    /// for the reason this project has already met four times: <c>QuestManager</c> is
    /// <c>AddComponent</c>-ed onto a bare GameObject by the boot sequence and has no
    /// inspector slot anybody could wire, so a <c>[SerializeField]</c> reference would
    /// sit null for the life of the project — exactly what happened to
    /// <c>ChatSystem._catalog</c>. A <c>Resources.Load</c> from a SUBFOLDER is the
    /// documented escape hatch (never the empty path, which deserializes the whole
    /// ~7 400-asset tree).</para>
    ///
    /// <para>The save layer resolves quest ids back to definitions through this list,
    /// so a quest pruned from the catalogue comes back from an old save as a warning
    /// and a dropped entry rather than a null reference.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "QuestCatalog", menuName = "Valkur/Data/Quest Catalog")]
    public sealed class QuestCatalog : ScriptableObject
    {
        [Tooltip("Every quest the game can offer. Order is display order in the " +
                 "offer list; put the introductory quests first.")]
        public List<QuestDefinition> quests = new List<QuestDefinition>();

        /// <summary>Resource path used by the runtime loader. Subfolder, never "".</summary>
        public const string ResourcePath = "Quests/QuestCatalog";

        public QuestDefinition Find(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return null;
            for (int i = 0; i < quests.Count; i++)
            {
                var q = quests[i];
                if (q != null && string.Equals(q.questId, questId, StringComparison.OrdinalIgnoreCase))
                    return q;
            }
            return null;
        }

        /// <summary>
        /// Every quest offered by <paramref name="personaId"/>, in catalogue order.
        /// Does NOT filter by level or prerequisites — that is
        /// <c>QuestService.OffersFor</c>'s job, because those depend on live player
        /// state and this asset knows nothing about a player.
        /// </summary>
        public void CollectByGiver(string personaId, List<QuestDefinition> into)
        {
            if (into == null) return;
            into.Clear();
            if (string.IsNullOrEmpty(personaId)) return;
            for (int i = 0; i < quests.Count; i++)
            {
                var q = quests[i];
                if (q == null) continue;
                if (string.Equals(q.giverPersonaId, personaId, StringComparison.OrdinalIgnoreCase))
                    into.Add(q);
            }
        }
    }
}
