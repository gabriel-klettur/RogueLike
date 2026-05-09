using UnityEngine;

namespace Valkur.Data.Dungeon.Udemy
{
    /// <summary>
    /// Defines a kind of room node in the dungeon graph. Used as an enum-like
    /// reference type so designers can edit the type list as project data
    /// (instead of code) and add new types without recompiling.
    /// </summary>
    [CreateAssetMenu(
        fileName = "RoomNodeType_",
        menuName = "Valkur/Dungeon/Udemy/Room Node Type")]
    public class RoomNodeTypeSO : ScriptableObject
    {
        [Tooltip("Display name of the room node type (e.g. 'Entrance', 'Corridor NS').")]
        [SerializeField] private string roomNodeTypeName;

        [Tooltip("If true, this type appears in the runtime NodeGraph editor's add-node picker.")]
        [SerializeField] private bool displayInNodeGraphEditor = true;

        [Tooltip("Generic corridor type â€” runtime resolves to NS or EW based on doorway orientation.")]
        [SerializeField] private bool isCorridor;

        [Tooltip("Concrete corridor template type, northâ€“south orientation.")]
        [SerializeField] private bool isCorridorNS;

        [Tooltip("Concrete corridor template type, eastâ€“west orientation.")]
        [SerializeField] private bool isCorridorEW;

        [Tooltip("The graph's single root entrance node. Exactly one expected.")]
        [SerializeField] private bool isEntrance;

        [Tooltip("The graph's boss room node. At most one connected boss expected.")]
        [SerializeField] private bool isBossRoom;

        [Tooltip("Unassigned/sentinel type. Cannot participate in connections.")]
        [SerializeField] private bool isNone;

        public string RoomNodeTypeName => roomNodeTypeName;
        public bool DisplayInNodeGraphEditor => displayInNodeGraphEditor;
        public bool IsCorridor => isCorridor;
        public bool IsCorridorNS => isCorridorNS;
        public bool IsCorridorEW => isCorridorEW;
        public bool IsEntrance => isEntrance;
        public bool IsBossRoom => isBossRoom;
        public bool IsNone => isNone;

        // Test hook â€” designers should not call this in production. Exists so EditMode
        // tests can assemble fixture types without going through the asset pipeline.
        public void TestSetTypeFlags(
            string typeName,
            bool entrance = false,
            bool corridor = false,
            bool corridorNS = false,
            bool corridorEW = false,
            bool boss = false,
            bool none = false,
            bool display = true)
        {
            roomNodeTypeName = typeName;
            isEntrance = entrance;
            isCorridor = corridor;
            isCorridorNS = corridorNS;
            isCorridorEW = corridorEW;
            isBossRoom = boss;
            isNone = none;
            displayInNodeGraphEditor = display;
        }
    }
}
