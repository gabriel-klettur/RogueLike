using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Translates <see cref="SkillEffect"/> entries on learned skill nodes
    /// into runtime side-effects: stat boosts on Health/Mana, spell unlocks
    /// in SpellCaster, etc. Subscribes to <see cref="LearnedSkills.OnSkillLearned"/>
    /// and applies every effect on the node when the player learns it.
    ///
    /// Sits next to <see cref="LearnedSkills"/> on the player. The split
    /// keeps the data layer (SkillNode/SkillTree/LearnedSkills) ignorant
    /// of gameplay components; this class is the bridge.
    ///
    /// Save reload: when a save is loaded, <see cref="LearnedSkills.FromSnapshot"/>
    /// repopulates the learned set silently (no OnSkillLearned events). The
    /// applicator's <see cref="ReapplyAll"/> seam exists so the save loader
    /// can replay the cumulative stat boosts after rehydration.
    ///
    /// Recognised stat keys (case-insensitive):
    ///   "maxHp" / "maxHealth"   -> Health.IncreaseMaxHp
    ///   "maxMana"               -> Mana.IncreaseMaxMana
    /// Unknown keys log a warning once per node (so a typo in an SO
    /// surfaces during play instead of silently doing nothing).
    /// </summary>
    [RequireComponent(typeof(LearnedSkills))]
    public class SkillEffectApplicator : MonoBehaviour
    {
        [Tooltip("Optional spell catalog used to resolve UnlockSpell effect keys " +
                 "to SpellDefinition assets. Falls back to scanning the active " +
                 "SpellCaster's already-registered spells when null.")]
        [SerializeField] private SpellCatalog spellCatalog;

        private LearnedSkills _skills;

        public void SetSpellCatalog(SpellCatalog catalog) { spellCatalog = catalog; }

        private void Awake()
        {
            _skills = GetComponent<LearnedSkills>();
        }

        private void OnEnable()
        {
            if (_skills == null) _skills = GetComponent<LearnedSkills>();
            if (_skills != null) _skills.OnSkillLearned += OnSkillLearned;
        }

        private void OnDisable()
        {
            if (_skills != null) _skills.OnSkillLearned -= OnSkillLearned;
        }

        // Public seam used by the save loader after FromSnapshot to replay
        // stat boosts the learned-skills HashSet doesn't track per node.
        public void ReapplyAll()
        {
            if (_skills == null || _skills.Tree == null) return;
            foreach (var id in _skills.LearnedIds)
            {
                if (_skills.Tree.TryGet(id, out var node))
                    ApplyNode(node);
            }
        }

        private void OnSkillLearned(string skillId)
        {
            if (_skills == null || _skills.Tree == null) return;
            if (_skills.Tree.TryGet(skillId, out var node))
                ApplyNode(node);
        }

        private void ApplyNode(SkillNode node)
        {
            if (node == null || node.effects == null) return;
            foreach (var eff in node.effects)
            {
                switch (eff.kind)
                {
                    case SkillEffectKind.StatBoost:    ApplyStatBoost(node, eff);  break;
                    case SkillEffectKind.UnlockSpell:  ApplyUnlockSpell(eff);      break;
                    case SkillEffectKind.PassiveAura:
                        if (!AuraRegistry.TryApply(eff.key, gameObject, eff.value))
                        {
                            Debug.LogWarning($"[SkillEffectApplicator] PassiveAura '{eff.key}' on " +
                                             $"skill '{node.skillId}' has no registered handler. " +
                                             "Register one via AuraRegistry.Register at boot.");
                        }
                        break;
                }
            }
        }

        private void ApplyStatBoost(SkillNode node, SkillEffect eff)
        {
            int delta = Mathf.RoundToInt(eff.value);
            if (delta <= 0) return;

            string key = eff.key ?? string.Empty;
            if (key.Equals("maxHp", System.StringComparison.OrdinalIgnoreCase) ||
                key.Equals("maxHealth", System.StringComparison.OrdinalIgnoreCase))
            {
                var health = GetComponent<Health>();
                if (health != null) health.IncreaseMaxHp(delta);
                return;
            }

            if (key.Equals("maxMana", System.StringComparison.OrdinalIgnoreCase))
            {
                var mana = GetComponent<Mana>();
                if (mana != null) mana.IncreaseMaxMana(delta);
                return;
            }

            Debug.LogWarning($"[SkillEffectApplicator] Unknown stat key '{eff.key}' on " +
                             $"skill '{node.skillId}'. Add it to the StatBoost dispatch.");
        }

        private void ApplyUnlockSpell(SkillEffect eff)
        {
            if (string.IsNullOrEmpty(eff.key)) return;

            var caster = GetComponent<SpellCaster>();
            if (caster == null)
            {
                Debug.LogWarning($"[SkillEffectApplicator] UnlockSpell '{eff.key}' fired but " +
                                 "the player has no SpellCaster — spell will not be available.");
                return;
            }

            // Resolve via catalog first, then via the caster's spell book.
            // Designers may unlock a spell that lives in the catalog but
            // hasn't been pre-registered on the caster.
            SpellDefinition def = null;
            if (spellCatalog != null && spellCatalog.TryGet(eff.key, out var fromCat))
                def = fromCat;

            if (def == null)
            {
                // Catalog miss — the spell may already be registered in the
                // caster's spell book by some earlier path; the unlock then
                // becomes a no-op (already learned). Log instead of error.
                Debug.LogWarning($"[SkillEffectApplicator] UnlockSpell '{eff.key}': " +
                                 "spell not found in SpellCatalog. Skipped.");
                return;
            }

            caster.RegisterSpell(eff.key, def);
        }
    }
}
