using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.Editors.DungeonNodeGraph
{
    /// <summary>
    /// On-disk DTO mirror of a <c>RoomNodeGraphSO</c>. Lives at namespace scope
    /// (not nested in the editor) so JsonUtility round-trips it deterministically
    /// — same hardening rationale as <c>MapEditorPersistenceDtos</c>.
    /// </summary>
    [Serializable]
    public class DungeonGraphDto
    {
        public string graphName = string.Empty;
        public List<DungeonNodeDto> nodes = new List<DungeonNodeDto>();
    }

    [Serializable]
    public class DungeonNodeDto
    {
        public string id = string.Empty;
        public string roomNodeName = "Node";
        public string nodeTypeName = string.Empty; // RoomNodeTypeSO.RoomNodeTypeName
        public Vector2 position;
        public List<string> parentIds = new List<string>();
        public List<string> childIds = new List<string>();
    }
}
