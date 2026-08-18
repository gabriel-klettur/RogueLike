using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Enemies;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Runtime glue for <see cref="BossDefinition"/>. Reads the SO,
    /// configures the boss's <see cref="BossPhaseController"/> phases,
    /// and rewires <see cref="NPCAutoCast"/> when the phase changes so
    /// the boss's spell rotation matches the active phase.
    ///
    /// Lifecycle:
    ///   1. Awake reads the BossDefinition.
    ///   2. ConfigurePhases populates BossPhaseController from
    ///      definition.phases (HP thresholds + labels).
    ///   3. ConfigureRotation(0) fires once for the entry phase so the
    ///      boss starts casting immediately.
    ///   4. OnPhaseChanged listener rewires the rotation each time the
    ///      controller advances.
    ///
    /// Phase 0 is always the entry phase (HP fraction 1.0). The
    /// configurator does NOT change the boss's stats per phase — that's
    /// the FSMMonsterBrain's domain. Only spell rotation and audio cues
    /// are phase-driven here.
    /// </summary>
    [RequireComponent(typeof(BossPhaseController))]
    public class BossConfigurator : MonoBehaviour
    {
        [Tooltip("Which boss this entity is. Required.")]
        [SerializeField] private BossDefinition definition;

        [Tooltip("Spell catalog used to resolve phase autoCastList entries to " +
                 "SpellDefinition assets.")]
        [SerializeField] private SpellCatalog spellCatalog;

        [Tooltip("Optional: monster catalog used by SpawnAdd chart cues.")]
        [SerializeField] private MonsterCatalog monsterCatalog;

        private BossPhaseController   _phases;
        private NPCAutoCast           _autoCast;
        private SpellCaster           _caster;
        private BossBeatChoreographer _choreographer;
        private BossCueDispatcher     _cueDispatcher;
        private BossPhaseAudio        _phaseAudio;

        public BossDefinition Definition => definition;
        public BossDefinition.Phase CurrentPhaseData
            => definition != null && _phases != null && _phases.CurrentPhase < definition.phases.Length
                 ? definition.phases[_phases.CurrentPhase] : null;

        public void SetDefinition(BossDefinition def, SpellCatalog catalog = null)
        {
            definition = def;
            if (catalog != null) spellCatalog = catalog;
        }

        private void Awake()
        {
            _phases        = GetComponent<BossPhaseController>();
            _autoCast      = GetComponent<NPCAutoCast>();
            _caster        = GetComponent<SpellCaster>();
            _choreographer = GetComponent<BossBeatChoreographer>();
            _cueDispatcher = GetComponent<BossCueDispatcher>();
            _phaseAudio    = GetComponent<BossPhaseAudio>();
        }

        private void OnEnable()
        {
            if (_phases != null) _phases.OnPhaseChanged += OnPhaseChanged;
        }

        private void OnDisable()
        {
            if (_phases != null) _phases.OnPhaseChanged -= OnPhaseChanged;
        }

        // Bind the entry-phase chart once the scene is fully built. Phase 0
        // never goes through OnPhaseChanged (it is the initial state, not a
        // transition target), so the chart binding has to be primed here.
        // ConfigureRotation is left untouched — that path was already wired
        // by the existing inspector authoring of the boss prefab.
        private void Start()
        {
            if (definition == null || definition.phases == null || definition.phases.Length == 0) return;
            // Music first: ResolveChart picks the chart whose musicTrackId
            // matches the ACTIVE song, so the entry theme has to be playing
            // before the chart is bound or the boss silently falls back to
            // the cooldown rotation on its own opening phase.
            ApplyPhaseMusic(definition.phases[0]);
            ConfigureChart(definition.phases[0]);
        }

        // Test seam — EditMode tests can drive ConfigurePhases / ConfigureRotation
        // without spinning up a full scene. EditMode's AddComponent does not run
        // OnEnable, so the phase subscription is made here too; that lets a test
        // exercise the real transition path via BossPhaseController.EvaluateAt
        // instead of poking private methods with reflection.
        public void InitForTest(BossPhaseController phases, NPCAutoCast autoCast,
                                SpellCaster caster, SpellCatalog catalog)
        {
            if (_subscribedForTest && _phases != null)
            {
                _phases.OnPhaseChanged -= OnPhaseChanged;
                _subscribedForTest = false;
            }
            _phases = phases;
            _autoCast = autoCast;
            _caster = caster;
            spellCatalog = catalog;
            if (_phases != null)
            {
                _phases.OnPhaseChanged += OnPhaseChanged;
                _subscribedForTest = true;
            }
        }

        // Tracks the InitForTest subscription separately from the OnEnable one
        // so re-initialising a fixture never double-subscribes.
        private bool _subscribedForTest;

        public void ConfigurePhasesFromDefinition()
        {
            if (definition == null || _phases == null) return;
            // Reflect phases array onto the controller. BossPhaseController
            // exposes inspector-only authoring; for runtime configuration we
            // rebuild its private list via reflection (the alternative is a
            // public SetPhases method on the controller, but that surface
            // would invite gameplay code to mutate phases mid-fight which
            // is the exact thing we want to avoid).
            var listField = typeof(BossPhaseController).GetField("phases",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (listField == null) return;

            var list = new System.Collections.Generic.List<BossPhaseController.PhaseBreakpoint>();
            foreach (var p in definition.phases)
            {
                list.Add(new BossPhaseController.PhaseBreakpoint
                {
                    hpFraction = p.hpThreshold,
                    label      = string.IsNullOrEmpty(p.label) ? $"Phase {list.Count}" : p.label,
                });
            }
            listField.SetValue(_phases, list);

            // Re-init the controller so it sorts the new list and resets
            // CurrentPhase to 0.
            var health = _phases.GetComponent<Health>();
            _phases.InitForTest(health);
        }

        public void ConfigureRotation(int phaseIndex)
        {
            if (definition == null || _autoCast == null || _caster == null) return;
            if (phaseIndex < 0 || phaseIndex >= definition.phases.Length) return;

            var phase = definition.phases[phaseIndex];

            _autoCast.Clear();
            if (phase.autoCastList == null || phase.autoCastList.Length == 0) return;

            int registered = 0;
            int slotCount  = _caster.SlotCount;
            for (int i = 0; i < phase.autoCastList.Length; i++)
            {
                string key = phase.autoCastList[i];
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (spellCatalog == null || !spellCatalog.TryGet(key, out var spell) || spell == null)
                {
                    Debug.LogWarning($"[BossConfigurator] Phase {phaseIndex} of " +
                                     $"'{(definition.baseMonster != null ? definition.baseMonster.monsterKey : definition.name)}' " +
                                     $"references unknown spell '{key}'. Skipping.");
                    continue;
                }

                _caster.RegisterSpell(spell.spellKey, spell);
                if (registered < slotCount)
                {
                    _caster.SetSpell(registered, spell);
                    float period = phase.autoCastPeriod > 0 ? phase.autoCastPeriod : 3f;
                    _autoCast.AddEntry(registered, periodSeconds: period, jitter: 0.5f);
                    registered++;
                }
            }
        }

        private void OnPhaseChanged(int oldPhase, int newPhase)
        {
            if (definition == null || newPhase < 0 || newPhase >= definition.phases.Length) return;

            var phase = definition.phases[newPhase];

            ConfigureRotation(newPhase);
            ApplyPhaseMusic(phase);
            ConfigureChart(phase);

            // Activation SFX (if any) — fired through the existing audio service.
            if (!string.IsNullOrEmpty(phase.activationSfxId))
            {
                var audio = ServiceLocator.Get<IAudioService>();
                audio?.PlaySfxById(phase.activationSfxId);
            }
        }

        /// <summary>
        /// Crossfades the game music to the phase's authored track. No-op when
        /// the phase leaves <c>musicTrackId</c> empty (the previous track keeps
        /// playing), when no audio service is registered, or when the boss
        /// carries a <see cref="BossPhaseAudio"/> component — that component is
        /// the inspector-authored alternative and owns the music swap on its
        /// own, so running both would fire two crossfades per transition.
        /// </summary>
        private void ApplyPhaseMusic(BossDefinition.Phase phase)
        {
            if (phase == null || string.IsNullOrEmpty(phase.musicTrackId)) return;
            // Resolved lazily as well as in Awake: a boss assembled at runtime
            // (or in an EditMode fixture, where Awake never runs) can gain the
            // component after this configurator was constructed.
            if (_phaseAudio == null) _phaseAudio = GetComponent<BossPhaseAudio>();
            if (_phaseAudio != null && _phaseAudio.isActiveAndEnabled) return;

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio == null) return;
            if (string.Equals(audio.CurrentTrackId, phase.musicTrackId, System.StringComparison.Ordinal))
                return;  // already playing — a redundant crossfade would restart the song mid-chart.

            audio.PlayMusicByTrackId(phase.musicTrackId, phase.musicCrossfadeSec);
        }

        /// <summary>
        /// Picks the chart whose <c>musicTrackId</c> matches the active song
        /// (or the first one as fallback) and binds it to the choreographer.
        /// Suspends NPCAutoCast if the phase asks for it.
        /// </summary>
        public void ConfigureChart(BossDefinition.Phase phase)
        {
            if (_choreographer == null) return;

            BossChart picked = ResolveChart(phase);
            _choreographer.Chart = picked;

            if (_cueDispatcher != null)
            {
                _cueDispatcher.Catalog        = spellCatalog;
                _cueDispatcher.MonsterCatalog = monsterCatalog;
                if (picked != null && phase.suppressAutoCastWhenChartActive)
                    _cueDispatcher.SuspendAutoCast();
                else
                    _cueDispatcher.ResumeAutoCastIfSuspended();
            }
        }

        private BossChart ResolveChart(BossDefinition.Phase phase)
        {
            if (phase.charts == null || phase.charts.Length == 0) return null;
            var audio = ServiceLocator.Get<IAudioService>();
            string current = audio != null ? audio.CurrentTrackId : string.Empty;

            // Prefer chart matching the active track id; otherwise null (no chart for this song).
            for (int i = 0; i < phase.charts.Length; i++)
            {
                var c = phase.charts[i];
                if (c == null) continue;
                if (string.IsNullOrEmpty(c.musicTrackId)) return c; // unbound chart wins as fallback
                if (string.Equals(c.musicTrackId, current, System.StringComparison.OrdinalIgnoreCase))
                    return c;
            }
            return null;
        }
    }
}
