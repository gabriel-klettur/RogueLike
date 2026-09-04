using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The grimoire half of <see cref="PlayerProgression"/>: which schools the character
    /// carries, and keeping the live <see cref="SpellCaster"/> spell book in step with
    /// what they actually know.
    /// </summary>
    public sealed partial class PlayerProgression
    {
        // Rebuilt on every grimoire change rather than kept incrementally, for the same
        // reason the stat layers are: a rebuild makes "learned one spell", "loaded a save"
        // and "respecced" the same code path.
        private readonly List<string> _knownScratch = new List<string>(16);

        private void EnsureSpellComponent()
        {
            _spells = GetComponent<KnownSpells>();
            if (_spells == null) _spells = gameObject.AddComponent<KnownSpells>();

            _spells.Configure(
                catalog != null ? catalog.spellTrees : null,
                _classKey,
                catalog != null ? catalog.alwaysKnownSpellKeys : null);

            if (catalog != null && catalog.startingArcanePoints > 0)
                _spells.AddPoints(catalog.startingArcanePoints);

            _spells.OnLoadoutChanged -= OnGrimoireChanged;
            _spells.OnLoadoutChanged += OnGrimoireChanged;
        }

        private void OnGrimoireChanged()
        {
            RebuildGrimoireLayer();
            SyncSpellBook();
        }

        public void RebuildGrimoireLayer()
        {
            if (_stats == null) return;
            _scratch.Clear();
            if (_spells != null) _spells.CollectModifiers(_scratch);
            _stats.SetLayer(StatLayer.Grimoire, _scratch);
        }

        /// <summary>
        /// Makes the caster's spell book equal to what the character knows — nothing more.
        ///
        /// This is the line that turns 46 castable spells handed out in the first frame
        /// into content that has to be earned. It REPLACES the book rather than adding to
        /// it, because a respec has to take spells away and an additive sync could never do
        /// that; and because "the book is exactly the known set" is a statement a test can
        /// check, while "the book has at least the known set" is not.
        ///
        /// Slot 0 is deliberately left alone. It is bound to the left mouse button by
        /// EntitySetup and having it empty on a fresh character would read as the game not
        /// responding to clicks rather than as a spell not being known yet.
        /// </summary>
        public void SyncSpellBook()
        {
            var caster = GetComponent<SpellCaster>();
            if (caster == null || _spells == null) return;

            _knownScratch.Clear();
            _spells.CollectKnownSpellKeys(_knownScratch);

            caster.ReplaceSpellBook(_knownScratch, ResolveSpellByKey);
        }

        /// <summary>
        /// Resolves a spell key to its definition through the catalog the caster already
        /// holds, falling back to the book it is being asked to replace. The fallback
        /// matters on a save load, where the known keys arrive before anything has
        /// re-scanned the catalog.
        /// </summary>
        private SpellDefinition ResolveSpellByKey(string key)
        {
            if (string.IsNullOrEmpty(key) || catalog == null) return null;

            if (catalog.TryFindSpellNode(key, out _, out var node) && node != null)
                return node.spell;

            return null;
        }

        // ── Save/load ───────────────────────────────────────────────────────────

        /// <summary>
        /// Rehydrates both trees from a save and rebuilds every layer that depends on them.
        /// The rebuild is not optional: <see cref="LearnedSkills.ReadFrom"/> repopulates
        /// state without replaying a purchase per node, so nothing else would notice.
        ///
        /// Writing is the mirror: <see cref="WriteTo"/> below.
        /// </summary>
        public void RestoreFrom(ProgressionSaveData data, int level)
        {
            if (data == null || data.IsEmpty)
            {
                // A save that says nothing about progression is either a legacy save or a
                // character who has spent nothing — and the two are indistinguishable, so
                // they get the same treatment. Reading the empty document would zero BOTH
                // point balances, which silently destroys the starting grant the character
                // was just given at spawn; measured live, a freshly loaded dwarf came back
                // with 0 arcane points instead of 1.
                //
                // Instead, reconstruct what the levels earned should have paid out. It is
                // the only migration that leaves a level-30 legacy character able to open
                // either tree at all.
                BackfillCurrenciesForLevel(level);
                RebuildLevelLayer(level);
                RebuildSkillLayer();
                RebuildGrimoireLayer();
                SyncSpellBook();
                return;
            }

            if (_skills != null) _skills.ReadFrom(data);
            if (_spells != null) _spells.ReadFrom(data);

            RebuildLevelLayer(level);
            RebuildSkillLayer();
            RebuildGrimoireLayer();
            SyncSpellBook();
        }

        /// <summary>Collects both halves into the shared progression document.</summary>
        public void WriteTo(ProgressionSaveData data)
        {
            if (data == null) return;
            if (_skills != null) _skills.WriteTo(data);
            if (_spells != null) _spells.WriteTo(data);
        }
    }
}
