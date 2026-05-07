using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// ScriptableObject mapping a spell key to a slash-style VFX prefab. Used by
    /// <c>Valkur.Gameplay.Spells.SlashExecutor</c> to swap the procedural arc for
    /// a designer-authored particle prefab (currently the "Free Slash VFX" pack
    /// from the Asset Store under <c>Assets/Free Slash VFX/Prefabs/</c>).
    ///
    /// Lookup order in <see cref="Resolve"/>:
    /// 1. Per-spell-key override (exact match in <see cref="overrides"/>).
    /// 2. <see cref="defaultPrefab"/> (used for any slash without an override).
    /// 3. <c>null</c> — caller falls back to the procedural <c>SlashArcFX</c>.
    ///
    /// The catalog asset must live in any <c>Resources/</c> folder so it can be
    /// loaded at runtime via <c>Resources.Load&lt;SlashVfxCatalog&gt;("SlashVfxCatalog")</c>
    /// without a scene-level wiring step. Designers populate the prefab fields
    /// in the Inspector.
    /// </summary>
    [CreateAssetMenu(fileName = "SlashVfxCatalog", menuName = "Valkur/Data/Slash VFX Catalog")]
    public class SlashVfxCatalog : ScriptableObject
    {
        [Tooltip("Prefab spawned for any slash spell that has no per-key override. " +
                 "Tinted at runtime by the spell's particleColor.")]
        public GameObject defaultPrefab;

        [Serializable]
        public struct Override
        {
            [Tooltip("Exact SpellDefinition.spellKey to override (e.g. 'hostile_slash_red').")]
            public string spellKey;

            [Tooltip("Prefab spawned in place of the default for this spell key.")]
            public GameObject prefab;
        }

        [Tooltip("Per-spell-key prefab overrides. Empty list = always use defaultPrefab.")]
        public List<Override> overrides = new List<Override>();

        /// <summary>
        /// Resolve the prefab for the given spell key. Returns null when neither
        /// an override nor a default is set — callers must handle null by using
        /// the procedural fallback.
        /// </summary>
        public GameObject Resolve(string spellKey)
        {
            if (!string.IsNullOrEmpty(spellKey) && overrides != null)
            {
                for (int i = 0; i < overrides.Count; i++)
                {
                    var o = overrides[i];
                    if (o.prefab != null && string.Equals(o.spellKey, spellKey, StringComparison.Ordinal))
                        return o.prefab;
                }
            }
            return defaultPrefab;
        }
    }
}
