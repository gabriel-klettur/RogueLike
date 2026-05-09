using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data.Dungeon.Udemy
{
    /// <summary>
    /// One playable dungeon level â€” a set of room templates the builder may
    /// assemble plus one or more candidate node graphs to assemble them into.
    /// At runtime the builder picks one graph at random; if it can't be laid
    /// out without overlaps after retry, it picks a different one.
    /// </summary>
    [CreateAssetMenu(
        fileName = "DungeonLevel_",
        menuName = "Valkur/Dungeon/Udemy/Dungeon Level")]
    public class DungeonLevelSO : ScriptableObject
    {
        [Tooltip("Display name of this level (shown in HUD / minimap / debug).")]
        public string levelName;

        [Tooltip("Pool of room templates available to the builder for this level.")]
        public List<RoomTemplateSO> roomTemplateList = new List<RoomTemplateSO>();

        [Tooltip("Candidate node graphs. Builder picks one at random; switches if layout fails.")]
        public List<RoomNodeGraphSO> roomNodeGraphList = new List<RoomNodeGraphSO>();

        /// <summary>
        /// Lightweight runtime check: every node type referenced in any graph
        /// must have at least one matching template. Returns true on success;
        /// when false, <paramref name="warning"/> contains the first issue.
        /// </summary>
        public bool CheckRoomTemplatesAndNodeGraphs(out string warning)
        {
            warning = string.Empty;
            if (roomTemplateList.Count == 0)
            {
                warning = $"DungeonLevel '{levelName}' has no room templates.";
                return false;
            }
            if (roomNodeGraphList.Count == 0)
            {
                warning = $"DungeonLevel '{levelName}' has no node graphs.";
                return false;
            }

            foreach (var graph in roomNodeGraphList)
            {
                if (graph == null) continue;
                foreach (var node in graph.RoomNodeList)
                {
                    if (node == null || node.roomNodeType == null) continue;
                    if (node.roomNodeType.IsCorridor)
                    {
                        // Corridors resolve to NS or EW based on doorway orientation
                        // â€” checked separately by the builder. Skip here.
                        continue;
                    }
                    if (!HasTemplateForType(node.roomNodeType))
                    {
                        warning = $"DungeonLevel '{levelName}' missing template for type "
                                  + $"'{node.roomNodeType.RoomNodeTypeName}'.";
                        return false;
                    }
                }
            }
            return true;
        }

        private bool HasTemplateForType(RoomNodeTypeSO type)
        {
            for (int i = 0; i < roomTemplateList.Count; i++)
            {
                var tmpl = roomTemplateList[i];
                if (tmpl != null && tmpl.roomNodeType == type) return true;
            }
            return false;
        }
    }
}
