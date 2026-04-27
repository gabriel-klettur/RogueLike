using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// ScriptableObject catalog of all spell definitions.
    /// Provides runtime lookup by spellKey.
    /// Populate via 'Valkur > Spells > Import Spells from Python JSON' or the Spells Editor.
    /// </summary>
    [CreateAssetMenu(fileName = "SpellCatalog", menuName = "Valkur/Data/Spell Catalog")]
    public class SpellCatalog : ScriptableObject
    {
        [SerializeField, Tooltip("All spell definitions in the game.")]
        private SpellDefinition[] spells = System.Array.Empty<SpellDefinition>();

        private Dictionary<string, SpellDefinition> _lookup;

        public SpellDefinition[] AllSpells => spells;
        public int Count => spells != null ? spells.Length : 0;

        public SpellDefinition GetByKey(string key)
        {
            EnsureLookup();
            if (string.IsNullOrEmpty(key)) return null;
            _lookup.TryGetValue(key, out var spell);
            return spell;
        }

        public bool TryGet(string key, out SpellDefinition spell)
        {
            EnsureLookup();
            if (string.IsNullOrEmpty(key)) { spell = null; return false; }
            return _lookup.TryGetValue(key, out spell);
        }

        public string[] GetAllKeys()
        {
            EnsureLookup();
            var keys = new string[_lookup.Count];
            _lookup.Keys.CopyTo(keys, 0);
            return keys;
        }

        private void EnsureLookup()
        {
            if (_lookup != null) return;
            _lookup = new Dictionary<string, SpellDefinition>(System.StringComparer.OrdinalIgnoreCase);
            if (spells == null) return;
            foreach (var s in spells)
            {
                if (s == null || string.IsNullOrEmpty(s.spellKey)) continue;
                _lookup[s.spellKey] = s;
            }
        }

        private void OnValidate()
        {
            _lookup = null; // rebuild on next access
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only: replace the spells array and mark dirty.
        /// </summary>
        public void SetSpells(SpellDefinition[] newSpells)
        {
            spells = newSpells ?? System.Array.Empty<SpellDefinition>();
            _lookup = null;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        /// <summary>
        /// Runtime-safe: replace the spells array and clear the lookup cache.
        /// Used by the in-game F4 Spells Editor.
        /// </summary>
        public void SetSpellsRuntime(SpellDefinition[] newSpells)
        {
            spells = newSpells ?? System.Array.Empty<SpellDefinition>();
            _lookup = null;
        }
    }
}
