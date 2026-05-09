using System.Collections.Generic;
using UnityEngine;
using Valkur.Data.Dungeon.Udemy;

namespace Valkur.Data
{
    /// <summary>
    /// Catalog of all <see cref="RoomTemplateSO"/> assets known to the project.
    /// Used by the runtime NodeGraph editor (picker), the dungeon builder
    /// (template lookup by GUID and by node type) and editor tooling.
    /// Mirrors the <see cref="BuildingCatalog"/> shape: list + lazy GUID dict +
    /// upsert that invalidates the cache.
    /// </summary>
    [CreateAssetMenu(
        fileName = "RoomTemplateCatalog",
        menuName = "Valkur/Dungeon/Udemy/Room Template Catalog")]
    public class RoomTemplateCatalog : ScriptableObject
    {
        [SerializeField] private List<RoomTemplateSO> _templates = new List<RoomTemplateSO>();

        public IReadOnlyList<RoomTemplateSO> Templates => _templates;

        [System.NonSerialized] private Dictionary<string, RoomTemplateSO> _byGuid;

        private Dictionary<string, RoomTemplateSO> ByGuid
        {
            get
            {
                if (_byGuid == null) RebuildLookup();
                return _byGuid;
            }
        }

        private void RebuildLookup()
        {
            _byGuid = new Dictionary<string, RoomTemplateSO>(_templates.Count);
            foreach (var t in _templates)
            {
                if (t != null && !string.IsNullOrEmpty(t.guid))
                    _byGuid[t.guid] = t;
            }
        }

        /// <summary>Find a template by its GUID. Returns null if not found.</summary>
        public RoomTemplateSO GetByGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            return ByGuid.TryGetValue(guid, out var result) ? result : null;
        }

        /// <summary>
        /// Returns all templates whose <see cref="RoomTemplateSO.roomNodeType"/>
        /// matches the given type. Useful for the builder when picking a random
        /// template for a graph node.
        /// </summary>
        public List<RoomTemplateSO> FindByNodeType(RoomNodeTypeSO type)
        {
            var matches = new List<RoomTemplateSO>();
            if (type == null) return matches;
            for (int i = 0; i < _templates.Count; i++)
            {
                if (_templates[i] != null && _templates[i].roomNodeType == type)
                    matches.Add(_templates[i]);
            }
            return matches;
        }

        /// <summary>Add if no entry with the same GUID exists. Returns true on add.</summary>
        public bool AddTemplate(RoomTemplateSO template)
        {
            if (template == null || string.IsNullOrEmpty(template.guid)) return false;
            if (ByGuid.ContainsKey(template.guid)) return false;
            _templates.Add(template);
            _byGuid[template.guid] = template;
            return true;
        }

        /// <summary>Replace an existing entry (same GUID) or add if new.</summary>
        public void UpsertTemplate(RoomTemplateSO template)
        {
            if (template == null || string.IsNullOrEmpty(template.guid)) return;
            for (int i = 0; i < _templates.Count; i++)
            {
                if (_templates[i] != null && _templates[i].guid == template.guid)
                {
                    _templates[i] = template;
                    _byGuid = null; // invalidate cache
                    return;
                }
            }
            _templates.Add(template);
            _byGuid = null;
        }
    }
}
