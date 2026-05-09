using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data.Dungeon.Udemy
{
    /// <summary>
    /// Master list of every <see cref="RoomNodeTypeSO"/> known to the project.
    /// Used by the runtime NodeGraph editor's picker and by node-type popups
    /// in place of an enum (which would require code changes per new type).
    /// </summary>
    [CreateAssetMenu(
        fileName = "RoomNodeTypeList",
        menuName = "Valkur/Dungeon/Udemy/Room Node Type List")]
    public class RoomNodeTypeListSO : ScriptableObject
    {
        [Tooltip("Every RoomNodeTypeSO available in this project. Used in place of an enum.")]
        [SerializeField] private List<RoomNodeTypeSO> list = new List<RoomNodeTypeSO>();

        public IReadOnlyList<RoomNodeTypeSO> List => list;

        public RoomNodeTypeSO FindByName(string typeName)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].RoomNodeTypeName == typeName)
                    return list[i];
            }
            return null;
        }

        // Test hook â€” production code should add via the inspector.
        public void TestAdd(RoomNodeTypeSO type)
        {
            if (type != null) list.Add(type);
        }
    }
}
