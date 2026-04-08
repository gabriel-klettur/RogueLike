using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Catalog of all BuildingTemplateData assets for the project.
    /// Singleton asset at Assets/_Project/Data/Catalogs/Buildings/BuildingCatalog.asset.
    ///
    /// Maps to Python's buildings_templates.json (the global template list).
    /// Used by BuildingLoader at runtime and BuildingsEditorWindow at design time.
    /// </summary>
    [CreateAssetMenu(fileName = "BuildingCatalog", menuName = "Valkur/Buildings/Catalog")]
    public class BuildingCatalog : ScriptableObject
    {
        [SerializeField] private List<BuildingTemplateData> _templates = new List<BuildingTemplateData>();

        public IReadOnlyList<BuildingTemplateData> Templates => _templates;

        /// <summary>
        /// Find a template by its integer template ID.
        /// Returns null if not found.
        /// Used by BuildingLoader to resolve 'template_id' from instances JSON.
        /// </summary>
        public BuildingTemplateData GetById(int id)
        {
            foreach (var t in _templates)
            {
                if (t != null && t.templateId == id)
                    return t;
            }
            return null;
        }

        /// <summary>
        /// Add a template if no entry with the same templateId already exists.
        /// Called by BuildingImporter during migration.
        /// </summary>
        public bool AddTemplate(BuildingTemplateData template)
        {
            if (template == null) return false;
            foreach (var t in _templates)
            {
                if (t != null && t.templateId == template.templateId)
                    return false;
            }
            _templates.Add(template);
            return true;
        }

        /// <summary>
        /// Replace an existing entry (same templateId) or add if new.
        /// Called by BuildingImporter on re-import to refresh data.
        /// </summary>
        public void UpsertTemplate(BuildingTemplateData template)
        {
            if (template == null) return;
            for (int i = 0; i < _templates.Count; i++)
            {
                if (_templates[i] != null && _templates[i].templateId == template.templateId)
                {
                    _templates[i] = template;
                    return;
                }
            }
            _templates.Add(template);
        }
    }
}
