using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Catalog mapping NPC entity names to persona definitions.
    /// Maps to Python's data/chat/assignments.json.
    /// </summary>
    [CreateAssetMenu(fileName = "ChatAssignmentCatalog", menuName = "Valkur/Chat/Assignment Catalog")]
    public class ChatAssignmentCatalog : ScriptableObject
    {
        [Tooltip("Chat assignments mapping entity names to persona definitions.")]
        public List<ChatAssignment> assignments = new List<ChatAssignment>();

        private Dictionary<string, NPCPersonaDefinition> _lookup;

        public NPCPersonaDefinition GetPersona(string entityName)
        {
            if (_lookup == null) RebuildLookup();
            _lookup.TryGetValue(entityName, out var persona);
            return persona;
        }

        public void RebuildLookup()
        {
            _lookup = new Dictionary<string, NPCPersonaDefinition>();
            foreach (var a in assignments)
            {
                if (a.persona != null && !string.IsNullOrEmpty(a.entityName))
                    _lookup[a.entityName] = a.persona;
            }
        }

        [System.Serializable]
        public struct ChatAssignment
        {
            [Tooltip("Entity display name (matches NPCInteractable or Identity name).")]
            public string entityName;
            public NPCPersonaDefinition persona;
        }

#if UNITY_EDITOR
        private void OnValidate() => _lookup = null;
#endif
    }
}
