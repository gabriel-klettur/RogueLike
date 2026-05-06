using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay.Enemies
{
    /// <summary>
    /// Translates typed <see cref="BossCue"/> events from
    /// <see cref="BossBeatChoreographer"/> into concrete in-game actions on
    /// the boss it sits next to: spell casts, SFX, phase transitions, ad
    /// spawns, animator triggers.
    ///
    /// Lives next to the choreographer (same GameObject) and resolves all
    /// dependencies via <c>GetComponent</c> + <c>ServiceLocator</c>. Catalogs
    /// (spells, monsters) are injected by <c>BossConfigurator</c>.
    ///
    /// <para><b>Timing model.</b> A cue fires AT its beat — there is no
    /// runtime look-ahead. For spells with a non-zero <c>prepareDuration</c>
    /// the actual damage impact lands <c>prepareDuration</c> seconds AFTER
    /// the cue beat. Authors place cues on the beat where the cast STARTS;
    /// the in-game Boss Editor draws an "expected impact" preview marker
    /// based on the spell's prepareDuration so visual alignment to the
    /// musical beat remains transparent during authoring.</para>
    ///
    /// While a chart is active and the current phase asks for it, the
    /// cooldown-based <see cref="NPCAutoCast"/> rotation is paused so the
    /// boss does not double-cast.
    /// </summary>
    public sealed class BossCueDispatcher : MonoBehaviour
    {
        [Tooltip("Optional: restricts spell-key resolution to this catalog. If " +
                 "null at runtime, BossConfigurator injects one.")]
        [SerializeField] private SpellCatalog spellCatalog;

        [Tooltip("Optional: monster catalog used by SpawnAdd cues. If null, " +
                 "SpawnAdd cues log a warning and skip.")]
        [SerializeField] private MonsterCatalog monsterCatalog;

        private BossBeatChoreographer _choreographer;
        private SpellCaster           _caster;
        private BossPhaseController   _phases;
        private NPCAutoCast           _autoCast;
        private MonsterSpawner        _spawner;
        private Animator              _animator;
        private bool _subscribed;
        private bool _autoCastSuspended;

        public SpellCatalog   Catalog        { get => spellCatalog; set => spellCatalog = value; }
        public MonsterCatalog MonsterCatalog { get => monsterCatalog; set => monsterCatalog = value; }

        private void Awake()
        {
            _choreographer = GetComponent<BossBeatChoreographer>();
            _caster        = GetComponent<SpellCaster>();
            _phases        = GetComponent<BossPhaseController>();
            _autoCast      = GetComponent<NPCAutoCast>();
            _animator      = GetComponentInChildren<Animator>();
            _spawner       = FindFirstObjectByType<MonsterSpawner>();
        }

        private void OnEnable()
        {
            if (_choreographer != null && !_subscribed)
            {
                _choreographer.OnTypedCue += HandleCue;
                _subscribed = true;
            }
        }

        private void OnDisable()
        {
            if (_choreographer != null && _subscribed)
            {
                _choreographer.OnTypedCue -= HandleCue;
                _subscribed = false;
            }
            ResumeAutoCastIfSuspended();
        }

        // ── Test seam ───────────────────────────────────────────────────────
        public void InitForTest(BossBeatChoreographer choreographer,
                                SpellCaster caster,
                                BossPhaseController phases,
                                NPCAutoCast autoCast,
                                Animator animator,
                                SpellCatalog spells,
                                MonsterCatalog monsters,
                                MonsterSpawner spawner)
        {
            _choreographer = choreographer;
            _caster        = caster;
            _phases        = phases;
            _autoCast      = autoCast;
            _animator      = animator;
            spellCatalog   = spells;
            monsterCatalog = monsters;
            _spawner       = spawner;
        }

        public void HandleCueForTest(BossCue cue) => HandleCue(cue, 0, 0);

        // ── Auto-cast suspension ────────────────────────────────────────────
        public void SuspendAutoCast()
        {
            if (_autoCast == null || _autoCastSuspended) return;
            _autoCast.SetCastingEnabled(false);
            _autoCastSuspended = true;
        }

        public void ResumeAutoCastIfSuspended()
        {
            if (_autoCast == null || !_autoCastSuspended) return;
            _autoCast.SetCastingEnabled(true);
            _autoCastSuspended = false;
        }

        // ── Core dispatch ───────────────────────────────────────────────────
        private void HandleCue(BossCue cue, int beatInBar, int bar)
        {
            switch (cue.type)
            {
                case BossCueType.CastSpell:    DoCastSpell(cue);    break;
                case BossCueType.PlaySfx:      DoPlaySfx(cue);      break;
                case BossCueType.SwitchPhase:  DoSwitchPhase(cue);  break;
                case BossCueType.SpawnAdd:     DoSpawnAdd(cue);     break;
                case BossCueType.Taunt:        DoAnimTrigger(cue);  break;
                case BossCueType.PlayAnim:     DoAnimTrigger(cue);  break;
            }
        }

        private void DoCastSpell(BossCue cue)
        {
            if (_caster == null) return;
            if (string.IsNullOrEmpty(cue.targetKey)) return;
            if (spellCatalog != null && !spellCatalog.TryGet(cue.targetKey, out _))
            {
                Debug.LogWarning($"[BossCueDispatcher] Cue references unknown spellKey '{cue.targetKey}'.");
                return;
            }
            Vector2 dir = ResolveDirection(cue.targeting);
            _caster.TryCastByKey(cue.targetKey, dir);
        }

        private void DoPlaySfx(BossCue cue)
        {
            if (string.IsNullOrEmpty(cue.targetKey)) return;
            var audio = ServiceLocator.Get<IAudioService>();
            audio?.PlaySfxById(cue.targetKey);
        }

        private void DoSwitchPhase(BossCue cue)
        {
            if (_phases == null || string.IsNullOrEmpty(cue.targetKey)) return;
            _phases.ForcePhase(cue.targetKey);
        }

        private void DoSpawnAdd(BossCue cue)
        {
            if (_spawner == null || string.IsNullOrEmpty(cue.targetKey))
            {
                if (_spawner == null)
                    Debug.LogWarning("[BossCueDispatcher] SpawnAdd cue ignored (no MonsterSpawner in scene).");
                return;
            }
            if (monsterCatalog == null)
            {
                Debug.LogWarning($"[BossCueDispatcher] SpawnAdd cue '{cue.targetKey}' ignored (MonsterCatalog not assigned).");
                return;
            }
            MonsterDefinition def = monsterCatalog.GetByKey(cue.targetKey);
            if (def == null)
            {
                Debug.LogWarning($"[BossCueDispatcher] SpawnAdd cue references unknown monster '{cue.targetKey}'.");
                return;
            }
            float radius = cue.payload > 0 ? cue.payload : 1.5f;
            Vector2 around = transform.position;
            Vector2 jitter = Random.insideUnitCircle * radius;
            _spawner.SpawnEntity(def, around + jitter);
        }

        private void DoAnimTrigger(BossCue cue)
        {
            if (_animator == null || string.IsNullOrEmpty(cue.targetKey)) return;
            _animator.SetTrigger(cue.targetKey);
        }

        // ── Targeting ───────────────────────────────────────────────────────
        private Vector2 _lastDir = Vector2.right;

        private Vector2 ResolveDirection(BossCueTargeting targeting)
        {
            switch (targeting)
            {
                case BossCueTargeting.ToPlayer:
                {
                    Vector2 dir = DirectionToPlayer();
                    if (dir.sqrMagnitude > 0.001f) _lastDir = dir;
                    return _lastDir;
                }
                case BossCueTargeting.Forward:
                    return _lastDir;
                case BossCueTargeting.Random8:
                {
                    int sector = Random.Range(0, 8);
                    float ang = sector * (Mathf.PI * 2f / 8f);
                    Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    _lastDir = d;
                    return d;
                }
                case BossCueTargeting.LastDir:
                default:
                    return _lastDir;
            }
        }

        private Vector2 DirectionToPlayer()
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p == null) return _lastDir;
            Vector2 delta = (Vector2)p.transform.position - (Vector2)transform.position;
            return delta.sqrMagnitude > 0.0001f ? delta.normalized : _lastDir;
        }
    }
}
