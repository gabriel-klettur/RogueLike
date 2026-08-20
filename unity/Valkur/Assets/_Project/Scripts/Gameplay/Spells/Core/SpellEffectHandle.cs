using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Marker attached by <see cref="SpellEffectRegistry.Track"/> to a spell-spawned world
    /// object. Its whole job is to notice its own destruction, whoever caused it — the
    /// effect's own countdown, the instance cap, a zone change, or the caster dying — and
    /// tell the registry to stop counting it.
    ///
    /// Deliberately holds no behaviour: the controller on the same GameObject owns the
    /// effect, this only owns the bookkeeping.
    /// </summary>
    [DisallowMultipleComponent]
    public class SpellEffectHandle : MonoBehaviour
    {
        /// <summary>Catalog key of the spell that spawned this effect.</summary>
        public string SpellKey { get; private set; }

        /// <summary>
        /// Who cast it. May be null, and may go null later — most area effects are scene-root
        /// objects that outlive their caster, which is exactly why the registry keeps this.
        /// </summary>
        public GameObject Caster { get; private set; }

        internal void Bind(string spellKey, GameObject caster)
        {
            SpellKey = spellKey;
            Caster = caster;
        }

        private void OnDestroy()
        {
            SpellEffectRegistry.Forget(this);
        }
    }
}
