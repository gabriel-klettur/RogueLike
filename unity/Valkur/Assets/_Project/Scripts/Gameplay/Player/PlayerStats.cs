using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The single owner of every number that describes the player, composed from
    /// independent LAYERS: the class's base values, the level curve, talents, grimoire
    /// nodes, equipment, timed buffs and auras.
    ///
    /// The rule is the one <c>SpriteTintStack</c> established for sprite colour, and it
    /// exists for the same reason: before it, nine systems each cached the current value,
    /// changed it, and wrote their cache back — correct alone, wrong together. A stat
    /// store fails identically. Unequipping a sword has to remove the sword's +6 melee
    /// damage and nothing else, even if a potion and three talents also touched melee
    /// damage while it was worn, and no amount of care in each individual system produces
    /// that. So: **every source writes only its own layer and never the total.** Removal
    /// is then exact by construction and there is no "restore the original" step to get
    /// wrong.
    ///
    /// Composition is published and fixed (see <see cref="StatOp"/>):
    /// <code>final = clamp((base + Σ Flat) × (1 + Σ PercentAdd) × Π (1 + PercentMult))</code>
    ///
    /// This component is the AUTHORITY and the live components are its OUTPUT: on every
    /// recompute it pushes the resolved numbers into <c>Health</c>, <c>Mana</c>,
    /// <c>MeleeCombat</c> and <c>PlayerController</c>. Those components are untouched by
    /// this design — they still work exactly as before for monsters, which have no
    /// PlayerStats and never will.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class PlayerStats : MonoBehaviour
    {
        private static readonly int StatCount = StatCatalog.All.Length;
        private static readonly int LayerCount =
            Enum.GetValues(typeof(StatLayer)).Length;

        // Base values, indexed by (int)StatKind. Written once by the class definition.
        private readonly float[] _base = new float[StatCount];

        // One modifier list per layer. A layer is replaced wholesale by its owner, which
        // is what makes removal exact.
        private readonly List<StatModifier>[] _layers = BuildLayers();

        // Resolved values, recomputed lazily. Kept as a cache rather than recomputed per
        // read because MeleeCombat, the HUD and every spell cast query these.
        private readonly float[] _resolved = new float[StatCount];
        private bool _dirty = true;

        /// <summary>
        /// Fires after a recompute has been pushed to the live components. Carries no
        /// payload on purpose: a listener that cares about one stat should read it, and
        /// a listener that redraws a sheet needs all of them anyway.
        /// </summary>
        public event Action OnStatsChanged;

        private static List<StatModifier>[] BuildLayers()
        {
            int count = Enum.GetValues(typeof(StatLayer)).Length;
            var layers = new List<StatModifier>[count];
            for (int i = 0; i < count; i++) layers[i] = new List<StatModifier>();
            return layers;
        }

        // ── Base ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Sets one base value. The base is what the character would have with no level,
        /// no talents, no gear and no buffs — i.e. the class definition, and nothing else
        /// should ever write it.
        /// </summary>
        public void SetBase(StatKind stat, float value)
        {
            _base[(int)stat] = value;
            _dirty = true;
        }

        public float GetBase(StatKind stat) => _base[(int)stat];

        /// <summary>
        /// Seeds every base from a class definition. Called once by EntitySetup. Stats the
        /// definition has nothing to say about take their neutral value, so a multiplier
        /// stat rests at 1 rather than at 0 — getting that backwards makes a fresh
        /// character deal no spell damage at all.
        /// </summary>
        public void ApplyClassBase(PlayerDefinition def)
        {
            for (int i = 0; i < StatCount; i++)
                _base[i] = StatCatalog.NeutralBase(StatCatalog.All[i]);

            if (def != null)
            {
                SetBase(StatKind.MaxHp, def.maxStrength);
                SetBase(StatKind.MaxMana, def.maxIntelligence);
                SetBase(StatKind.ManaRegen, def.manaRegenPerSecond);
                SetBase(StatKind.MoveSpeed, def.basicSpeed);
                SetBase(StatKind.MeleeDamage, def.basicAttack);
                // The two historical constants. Until the stat layer existed, every class
                // was melee-initialised with a literal `0.5f, 1.5f` at the call site, so an
                // asset authored before those fields existed must keep exactly that reach
                // and rhythm rather than collapsing to zero.
                SetBase(StatKind.MeleeRange, def.meleeRange > 0f ? def.meleeRange : 1.5f);
                SetBase(StatKind.MeleeCooldown, def.meleeCooldown > 0f ? def.meleeCooldown : 0.5f);
                SetBase(StatKind.Defense, def.basicArmor);
                SetBase(StatKind.CritChance, def.baseCritChance);
                SetBase(StatKind.CritMultiplier, def.baseCritMultiplier);
            }

            _dirty = true;
            Recompute();
        }

        // ── Layers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Replaces everything a source contributes. This is the ONLY way a source should
        /// change its own contribution: read what you own, rebuild it, hand it over. There
        /// is deliberately no "remove one modifier" API, because that reintroduces the
        /// bookkeeping this design exists to delete.
        /// </summary>
        public void SetLayer(StatLayer layer, IEnumerable<StatModifier> modifiers)
        {
            var list = _layers[(int)layer];
            list.Clear();
            if (modifiers != null) list.AddRange(modifiers);
            _dirty = true;
            Recompute();
        }

        /// <summary>Empties a layer. Equivalent to <c>SetLayer(layer, null)</c>, spelled
        /// out because "the potion wore off" reads better than "the potion now contributes
        /// nothing".</summary>
        public void ClearLayer(StatLayer layer) => SetLayer(layer, null);

        /// <summary>
        /// Appends to a layer without disturbing what is already there. Only correct when
        /// the layer has several independent owners that cannot see each other — today
        /// only <see cref="StatLayer.Aura"/>, where each aura arrives and leaves on its own
        /// schedule. Anything with ONE owner must use <see cref="SetLayer"/>.
        /// </summary>
        public void AddToLayer(StatLayer layer, StatModifier modifier)
        {
            _layers[(int)layer].Add(modifier);
            _dirty = true;
            Recompute();
        }

        public IReadOnlyList<StatModifier> GetLayer(StatLayer layer) => _layers[(int)layer];

        // ── Reading ─────────────────────────────────────────────────────────────

        /// <summary>Resolved value of a stat, after every layer and the catalog clamp.</summary>
        public float Get(StatKind stat)
        {
            if (_dirty) Recompute();
            return _resolved[(int)stat];
        }

        public int GetInt(StatKind stat) => Mathf.RoundToInt(Get(stat));

        /// <summary>
        /// What one layer contributes to one stat, expressed as the DIFFERENCE the layer
        /// makes to the final number. Computed by resolving twice — once with the layer and
        /// once without — rather than by summing the layer's own modifiers, because a
        /// percentage's contribution depends on every other layer present. Summing the raw
        /// values would report "+5%" as 0.05, which is not a number the player can add up
        /// to the total they are looking at.
        /// </summary>
        public float GetLayerContribution(StatKind stat, StatLayer layer)
        {
            float with = Resolve(stat, skipLayer: null);
            float without = Resolve(stat, skipLayer: layer);
            return with - without;
        }

        private void Recompute()
        {
            for (int i = 0; i < StatCount; i++)
                _resolved[i] = Resolve(StatCatalog.All[i], skipLayer: null);

            _dirty = false;
            PushToComponents();
            OnStatsChanged?.Invoke();
        }

        /// <summary>Fires <see cref="OnStatsChanged"/> without recomputing. For the
        /// bootstrap's late push, where the numbers are already correct and only the
        /// listeners are behind.</summary>
        private void RaiseStatsChanged()
        {
            OnStatsChanged?.Invoke();
        }

        private float Resolve(StatKind stat, StatLayer? skipLayer)
        {
            float flat = 0f;
            float percentAdd = 0f;
            float percentMult = 1f;

            for (int layerIndex = 0; layerIndex < LayerCount; layerIndex++)
            {
                if (skipLayer.HasValue && (int)skipLayer.Value == layerIndex) continue;

                var list = _layers[layerIndex];
                for (int i = 0; i < list.Count; i++)
                {
                    var m = list[i];
                    if (m.stat != stat) continue;

                    switch (m.op)
                    {
                        case StatOp.Flat:        flat += m.value;                break;
                        case StatOp.PercentAdd:  percentAdd += m.value;          break;
                        case StatOp.PercentMult: percentMult *= (1f + m.value);  break;
                    }
                }
            }

            float value = (_base[(int)stat] + flat) * (1f + percentAdd) * percentMult;
            return StatCatalog.Clamp(stat, value);
        }
    }
}
