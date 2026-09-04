using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The orchestrator of everything that makes a character grow: it resolves the
    /// <see cref="ProgressionCatalog"/>, hands each tree to its component, grants both
    /// currencies on level-up, and rebuilds the stat layers whenever anything changes.
    ///
    /// It exists so the pieces stay ignorant of each other. <see cref="LearnedSkills"/>
    /// knows what the player bought and nothing about hit points; <see cref="PlayerStats"/>
    /// knows how numbers compose and nothing about trees; <see cref="KnownSpells"/> knows
    /// what may be cast and nothing about the spell book component. This class is the only
    /// place that knows all three, which is what keeps a change to any one of them from
    /// rippling.
    ///
    /// Everything it does is a REBUILD, never a delta. On any change it recollects the
    /// whole Skill layer and the whole Grimoire layer and hands them over wholesale. That
    /// makes a respec, a save load and a single purchase the same code path, and it is the
    /// reason none of them can leave a stale bonus behind — the failure the previous
    /// `IncreaseMaxHp`-based applicator could not avoid, because a delta API has no way to
    /// express "and forget everything I said before".
    /// </summary>
    [RequireComponent(typeof(PlayerStats))]
    public sealed partial class PlayerProgression : MonoBehaviour
    {
        [SerializeField] private ProgressionCatalog catalog;

        private PlayerStats _stats;
        private LearnedSkills _skills;
        private KnownSpells _spells;
        private Experience _experience;

        private string _classKey = string.Empty;

        // Hoisted so a rebuild — which happens on every purchase and every level — does
        // not allocate a list per call.
        private readonly List<StatModifier> _scratch = new List<StatModifier>(32);

        // Auras are applied once when their node first reaches rank 1 and are NOT part of
        // the rebuild: AuraRegistry has no removal API, so re-applying on every rebuild
        // would stack a healing aura once per purchase for the rest of the run.
        private readonly HashSet<string> _appliedAuras = new HashSet<string>();

        public ProgressionCatalog Catalog => catalog;
        public LearnedSkills Skills => _skills;
        public KnownSpells Grimoire => _spells;
        public string ClassKey => _classKey;

        public void SetCatalog(ProgressionCatalog value) => catalog = value;

        private void Awake()
        {
            _stats = GetComponent<PlayerStats>();
            _experience = GetComponent<Experience>();
        }

        /// <summary>
        /// Wires the whole progression stack for a class. Called once by EntitySetup after
        /// Health / Mana / MeleeCombat exist, because <see cref="PlayerStats"/> refuses to
        /// push into a component whose Initialize has not run.
        /// </summary>
        public void Configure(PlayerDefinition def)
        {
            _stats = GetComponent<PlayerStats>();
            _experience = GetComponent<Experience>();
            _classKey = def != null ? def.playerKey : string.Empty;

            if (catalog == null) catalog = LoadCatalog();

            _stats.ApplyClassBase(def);
            ApplyClassDefences(def);

            EnsureSkillComponent();
            EnsureSpellComponent();
            WireExperience();

            // The base is seated and every component now exists, so the first push can
            // finally land. Before this call Health.MaxHp was still 0 and the push was
            // correctly refused.
            _stats.ForcePush();

            RebuildLevelLayer(_experience != null ? _experience.Level : 1);
            RebuildSkillLayer();
            RebuildGrimoireLayer();
            SyncSpellBook();
        }

        private static ProgressionCatalog LoadCatalog()
        {
            // Loaded by PATH, never by a serialized reference: this component is
            // AddComponent-ed onto a bare GameObject by EntitySetup, so an inspector slot
            // would have no way to be filled — the exact defect that left ChatSystem's
            // catalog null for the life of the project. The subfolder is mandatory:
            // Resources.LoadAll<T>("") is a full-tree scan of ~7,400 assets.
            var loaded = Resources.Load<ProgressionCatalog>(ProgressionCatalog.ResourcePath);
            if (loaded == null)
            {
                Debug.LogWarning("[PlayerProgression] No ProgressionCatalog at " +
                                 $"Resources/{ProgressionCatalog.ResourcePath}. Levelling will " +
                                 "grant nothing and both trees will be empty. Run " +
                                 "'Valkur > Progression > Seed Progression Content'.");
            }
            return loaded;
        }

        private void ApplyClassDefences(PlayerDefinition def)
        {
            if (def == null) return;

            // Resistances and status immunities live on the components rather than in the
            // stat store because they are per-ELEMENT tables, not scalars, and Health
            // already owns the damage seam that consults them. Until this line existed the
            // player was the only entity in the game that could not resist anything —
            // ConfigureMonster had been doing exactly this for every hostile for months.
            var health = GetComponent<Health>();
            if (health != null && def.resistances != null && def.resistances.Length > 0)
                health.SetResistances(def.resistances);

            var status = GetComponent<Valkur.Gameplay.Combat.StatusEffectManager>();
            if (status != null && def.statusImmunities != null && def.statusImmunities.Length > 0)
                status.SetImmunities(def.statusImmunities);
        }

        private void EnsureSkillComponent()
        {
            _skills = GetComponent<LearnedSkills>();
            if (_skills == null) _skills = gameObject.AddComponent<LearnedSkills>();

            var tree = catalog != null ? catalog.GetSkillTreeForClass(_classKey) : null;
            if (tree == null && catalog != null)
            {
                Debug.LogWarning($"[PlayerProgression] No SkillTree with classKey " +
                                 $"'{_classKey}'. The talent panel will be empty for this class.");
            }
            _skills.SetTree(tree);

            if (catalog != null && catalog.startingSkillPoints > 0)
                _skills.AddPoints(catalog.startingSkillPoints);

            _skills.OnLoadoutChanged -= OnSkillsChanged;
            _skills.OnLoadoutChanged += OnSkillsChanged;
        }

        private void WireExperience()
        {
            if (_experience == null) return;

            if (catalog != null && catalog.xpCurve != null)
                _experience.SetCurve(catalog.xpCurve);

            _experience.OnLevelUp -= OnLevelUp;
            _experience.OnLevelUp += OnLevelUp;
        }

        private void OnDestroy()
        {
            if (_skills != null) _skills.OnLoadoutChanged -= OnSkillsChanged;
            if (_spells != null) _spells.OnLoadoutChanged -= OnGrimoireChanged;
            if (_experience != null) _experience.OnLevelUp -= OnLevelUp;
        }

        // ── Level ───────────────────────────────────────────────────────────────

        private void OnLevelUp(int newLevel)
        {
            GrantLevelCurrencies(newLevel);
            RebuildLevelLayer(newLevel);
        }

        /// <summary>Grants both currencies for reaching <paramref name="newLevel"/>.
        /// Public so the dev console and tests can exercise the policy directly.</summary>
        public void GrantLevelCurrencies(int newLevel)
        {
            if (catalog == null) return;

            if (_skills != null && catalog.skillPointsPerLevel > 0)
                _skills.AddPoints(catalog.skillPointsPerLevel);

            int arcane = catalog.ArcanePointsForLevel(newLevel);
            if (_spells != null && arcane > 0)
                _spells.AddPoints(arcane);
        }

        /// <summary>
        /// Pays out the currencies every level from 2 to <paramref name="level"/> should
        /// have granted. Used only when a save carries no progression document at all — a
        /// character saved before the trees existed, who would otherwise arrive at level 30
        /// with one arcane point and nothing to show for the run.
        ///
        /// It is deliberately NOT called on a normal load: a real document already carries
        /// both balances, spent and unspent, and re-granting on top of it would hand the
        /// player a free point per level on every single load.
        /// </summary>
        public void BackfillCurrenciesForLevel(int level)
        {
            for (int l = 2; l <= level; l++)
                GrantLevelCurrencies(l);
        }

        /// <summary>
        /// Rebuilds the Level stat layer from scratch for the given level.
        ///
        /// Note it is CUMULATIVE and absolute — the curve's per-level delta multiplied by
        /// the levels earned — rather than "add this level's delta". That is what lets the
        /// layer be rebuilt at any moment, which is what loading a save at level 30
        /// requires: there is no sequence of level-ups to replay.
        /// </summary>
        public void RebuildLevelLayer(int level)
        {
            if (_stats == null) return;

            var curve = catalog != null ? catalog.levelStatCurve : null;
            if (curve == null)
            {
                _stats.ClearLayer(StatLayer.Level);
                return;
            }

            int levelsEarned = Mathf.Max(0, level - 1);
            _scratch.Clear();

            int hp = 0, mana = 0;
            for (int l = 2; l <= level; l++)
            {
                hp += curve.HpDelta(l);
                mana += curve.ManaDelta(l);
            }

            if (hp > 0) _scratch.Add(StatModifier.Flat(StatKind.MaxHp, hp));
            if (mana > 0) _scratch.Add(StatModifier.Flat(StatKind.MaxMana, mana));

            foreach (var extra in curve.ModifiersForLevels(levelsEarned))
                _scratch.Add(extra);

            _stats.SetLayer(StatLayer.Level, _scratch);
        }

        // ── Talent layer ────────────────────────────────────────────────────────

        private void OnSkillsChanged()
        {
            RebuildSkillLayer();
            ApplyPendingAuras();
        }

        public void RebuildSkillLayer()
        {
            if (_stats == null) return;
            _scratch.Clear();
            if (_skills != null) _skills.CollectModifiers(_scratch);
            _stats.SetLayer(StatLayer.Skill, _scratch);
        }

        private void ApplyPendingAuras()
        {
            if (_skills == null || _skills.Tree == null) return;

            foreach (var pair in _skills.Ranks)
            {
                if (pair.Value <= 0) continue;
                if (!_skills.Tree.TryGet(pair.Key, out var node) || node == null) continue;
                if (node.passiveAuras == null) continue;

                foreach (var auraId in node.passiveAuras)
                {
                    if (string.IsNullOrWhiteSpace(auraId)) continue;
                    if (!_appliedAuras.Add(auraId)) continue;

                    if (!AuraRegistry.TryApply(auraId, gameObject, pair.Value))
                    {
                        Debug.LogWarning($"[PlayerProgression] Passive aura '{auraId}' on skill " +
                                         $"'{node.skillId}' has no registered handler. Register " +
                                         "one via AuraRegistry.Register at boot.");
                        _appliedAuras.Remove(auraId);
                    }
                }
            }
        }
    }
}
