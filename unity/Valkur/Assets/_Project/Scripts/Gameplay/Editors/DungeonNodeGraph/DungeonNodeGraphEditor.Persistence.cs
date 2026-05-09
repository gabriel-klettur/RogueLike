using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Valkur.Data.Dungeon.Udemy;

namespace Valkur.Gameplay.Editors.DungeonNodeGraph
{
    public partial class DungeonNodeGraphEditor
    {
        /// <summary>
        /// Subdirectory inside <see cref="Application.persistentDataPath"/>
        /// where each graph file lives. Path-only — keeps the editor
        /// orthogonal to the active Map slot for now (every project shares
        /// one pool of graph definitions). Phase 7's slot integration may
        /// move this under <c>Maps/&lt;Slot&gt;/dungeon_graphs/</c>.
        /// </summary>
        public const string GraphsSubdirectory = "DungeonGraphs";

        public static string GraphsDirectory =>
            Path.Combine(Application.persistentDataPath, GraphsSubdirectory);

        // ─────────────────────────────────────────────────────────────────
        // Save / Load — public API surface used by the UI buttons + tests.
        // ─────────────────────────────────────────────────────────────────

        public static List<string> ListGraphFiles()
        {
            var files = new List<string>();
            try
            {
                if (!Directory.Exists(GraphsDirectory)) return files;
                foreach (var path in Directory.GetFiles(GraphsDirectory, "*.json"))
                    files.Add(Path.GetFileNameWithoutExtension(path));
                files.Sort(System.StringComparer.OrdinalIgnoreCase);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[DungeonNodeGraphEditor] List failed: {ex.Message}");
            }
            return files;
        }

        public static bool SaveToFile(string fileName, DungeonGraphDto dto)
        {
            if (string.IsNullOrEmpty(fileName) || dto == null) return false;
            try
            {
                Directory.CreateDirectory(GraphsDirectory);
                var path = Path.Combine(GraphsDirectory, Sanitise(fileName) + ".json");
                File.WriteAllText(path, JsonUtility.ToJson(dto, prettyPrint: true));
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[DungeonNodeGraphEditor] Save failed: {ex.Message}");
                return false;
            }
        }

        public static DungeonGraphDto LoadFromFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            try
            {
                var path = Path.Combine(GraphsDirectory, Sanitise(fileName) + ".json");
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                return JsonUtility.FromJson<DungeonGraphDto>(json);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[DungeonNodeGraphEditor] Load failed: {ex.Message}");
                return null;
            }
        }

        public static bool DeleteFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            try
            {
                var path = Path.Combine(GraphsDirectory, Sanitise(fileName) + ".json");
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[DungeonNodeGraphEditor] Delete failed: {ex.Message}");
                return false;
            }
        }

        // Strip path separators / leading dots so user-entered names can't
        // escape the graphs directory or shadow hidden files.
        private static string Sanitise(string fileName)
        {
            var sb = new System.Text.StringBuilder(fileName.Length);
            foreach (var c in fileName)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ') sb.Append(c);
            }
            var clean = sb.ToString().Trim();
            return string.IsNullOrEmpty(clean) ? "untitled" : clean;
        }

        // ─────────────────────────────────────────────────────────────────
        // SO ↔ DTO conversion. Used by the UI to round-trip the in-memory
        // graph and by EditMode tests for round-trip checks.
        // ─────────────────────────────────────────────────────────────────

        public static DungeonGraphDto ToDto(string graphName, IList<DungeonGraphNodeData> nodes)
        {
            var dto = new DungeonGraphDto { graphName = graphName ?? string.Empty };
            if (nodes == null) return dto;
            foreach (var n in nodes)
            {
                if (n == null) continue;
                dto.nodes.Add(new DungeonNodeDto
                {
                    id = n.Id,
                    roomNodeName = n.RoomNodeName,
                    nodeTypeName = n.NodeType != null ? n.NodeType.RoomNodeTypeName : string.Empty,
                    position = n.Position,
                    parentIds = new List<string>(n.ParentIds),
                    childIds = new List<string>(n.ChildIds),
                });
            }
            return dto;
        }

        public static List<DungeonGraphNodeData> FromDto(DungeonGraphDto dto, RoomNodeTypeListSO nodeTypes)
        {
            var nodes = new List<DungeonGraphNodeData>();
            if (dto == null || dto.nodes == null) return nodes;
            foreach (var nodeDto in dto.nodes)
            {
                var data = new DungeonGraphNodeData
                {
                    Id = nodeDto.id,
                    RoomNodeName = nodeDto.roomNodeName,
                    NodeType = nodeTypes != null ? nodeTypes.FindByName(nodeDto.nodeTypeName) : null,
                    Position = nodeDto.position,
                };
                data.ParentIds.AddRange(nodeDto.parentIds ?? new List<string>());
                data.ChildIds.AddRange(nodeDto.childIds ?? new List<string>());
                nodes.Add(data);
            }
            return nodes;
        }
    }

    /// <summary>
    /// In-memory editor representation of one node. Decoupled from
    /// <c>RoomNodeSO</c> so the editor can mutate freely without going
    /// through ScriptableObject's editor-time serialization rules.
    /// </summary>
    public sealed class DungeonGraphNodeData
    {
        public string Id = System.Guid.NewGuid().ToString();
        public string RoomNodeName = "Node";
        public RoomNodeTypeSO NodeType;
        public Vector2 Position;
        public List<string> ParentIds = new List<string>();
        public List<string> ChildIds = new List<string>();
    }
}
