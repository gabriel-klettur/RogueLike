using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Catalog of SpawnerTemplateData assets for runtime lookup by template ID.
    /// Edit via the Inspector.
    /// </summary>
    [CreateAssetMenu(fileName = "SpawnerTemplateCatalog", menuName = "Valkur/Spawner/Template Catalog")]
    public class SpawnerTemplateCatalog : ScriptableObject
    {
        [SerializeField] private List<SpawnerTemplateData> templates = new List<SpawnerTemplateData>();

        private Dictionary<string, SpawnerTemplateData> _lookup;

        public IReadOnlyList<SpawnerTemplateData> Templates => templates;

        public SpawnerTemplateData GetById(string templateId)
        {
            if (_lookup == null) RebuildLookup();
            _lookup.TryGetValue(templateId, out var result);
            return result;
        }

        public void UpsertTemplate(SpawnerTemplateData template)
        {
            for (int i = 0; i < templates.Count; i++)
            {
                if (templates[i] != null && templates[i].templateId == template.templateId)
                {
                    templates[i] = template;
                    _lookup = null;
                    return;
                }
            }
            templates.Add(template);
            _lookup = null;
        }

        private void RebuildLookup()
        {
            _lookup = new Dictionary<string, SpawnerTemplateData>();
            foreach (var t in templates)
            {
                if (t != null && !string.IsNullOrEmpty(t.templateId))
                    _lookup[t.templateId] = t;
            }
        }

        private void OnEnable() => _lookup = null;
    }
}
