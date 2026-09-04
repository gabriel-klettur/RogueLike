using System;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat.Death;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Generic health component for any damageable entity.
    /// Maps to Python's hp/max_hp in entity stats.
    /// </summary>
    public class Health : MonoBehaviour
    {
        [SerializeField] private int maxHp = 100;
        [SerializeField] private int currentHp;
        private bool _invincible;

        // Extra fraction of damage this entity takes, owned by VulnerableEffect. 0 is the
        // neutral value every entity holds for its whole life unless something curses it.
        //
        // SINGLE OWNER, unlike _invincible. That flag has three independent writers (the dev
        // console's god mode, the F4 editor's test invulnerability and the shield), which is
        // why it has to be saved and restored rather than cleared -- a defect that shipped
        // twice. This one is written by exactly one status effect, and StatusEffectManager
        // replaces an effect of the same type before applying a new one, so the pairing of
        // OnApply and OnRemove is exact by construction. Adding a second writer would
        // reintroduce the save/restore problem and must not be done casually.
        private float _vulnerability;
        private PlayerSpiritState _spiritState;

        [Header("Mitigation")]
        [SerializeField]
        [Tooltip("Flat damage reduction applied to every attributable hit before HP is spent " +
                 "(see MitigateDamage). Wired from MonsterDefinition.stats.defense via " +
                 "SetDefense; 0 — the default, and what every Health had before this field " +
                 "existed — reproduces the old raw-subtraction behaviour exactly.")]
        private int defense;

        [SerializeField]
        [Tooltip("Per-element damage multipliers (see SpellElement). An element with no " +
                 "entry here defaults to a multiplier of 1.0. Empty — the default — means " +
                 "every element deals full damage, exactly as before this field existed. " +
                 "Wired from MonsterDefinition.stats.resistances via SetResistances.")]
        private ElementResistance[] resistances = Array.Empty<ElementResistance>();

        [Header("Post-Hit Grace")]
        [SerializeField]
        [Tooltip("Seconds after an attributable hit before another attributable hit can " +
                 "register on this entity. Stops several independent attackers landing in " +
                 "the same frame/burst from each dealing a full hit (e.g. 5 monsters at " +
                 "meleeCooldown 1 all swinging on the same tick). 0 — the default, and what " +
                 "every Health had before this field existed — never blocks a hit, so a fresh " +
                 "Health behaves exactly as before until something opts it in. Wired to " +
                 "RecommendedGraceSeconds (0.1s) by EntitySetup via SetPostHitGrace: that " +
                 "value sits below every shipped attack interval — the player's melee is 0.5s " +
                 "and the fastest spell cooldown in the catalog is fireball's 0.4s — so a " +
                 "single attacker's DPS is unaffected; the window only ever fires when a " +
                 "SECOND, independent source lands within it. DoT/zone ticks (TakeDotDamage) " +
                 "ignore this window entirely regardless of its value — see that method's doc.")]
        private float postHitGraceSeconds;

        /// <summary>
        /// The value EntitySetup wires onto monster (and player) Health via
        /// <see cref="SetPostHitGrace"/> — not applied automatically, so every Health
        /// created directly (tests, editor previews, anything that never calls the setter)
        /// keeps the inert 0-second default above.
        /// </summary>
        public const float RecommendedGraceSeconds = 0.1f;

        private float _nextHitAllowedTime = float.NegativeInfinity;

        /// <summary>
        /// Floor a mitigated hit can never drop below, once it has already survived the
        /// elemental-multiplier step with damage remaining. Integer damage has no clean way
        /// to express "reduced by 30%" without a rounding policy, so flat subtraction is the
        /// natural formula for defense; the floor is what keeps stacking defense from turning
        /// a landed hit into a complete no-op (an elemental multiplier of exactly 0 can still
        /// produce true zero-damage immunity — that is the intentional difference between an
        /// element you resist and one you shrug off entirely).
        /// </summary>
        private const int MinDamageAfterDefense = 1;

        public int MaxHp => maxHp;
        public int MaxHealth => maxHp;
        public int CurrentHp => currentHp;
        public bool IsDead => currentHp <= 0;
        public bool IsInvincible => _invincible;
        public float NormalizedHp => maxHp > 0 ? (float)currentHp / maxHp : 0f;
        public int Defense => defense;

        public event Action<int, int> OnHpChanged;
        public event Action OnDeath;
        public event Action<int> OnDamaged;

        /// <summary>
        /// A real hit was refused because this entity is invincible. Carries what WOULD have
        /// been dealt (before mitigation — nothing was mitigated, nothing was dealt) and who
        /// threw it, which may be null.
        ///
        /// <para>This exists because the refusal used to be completely silent: <c>ApplyDamage</c>
        /// returned on the invincibility check and no system downstream could tell the
        /// difference between a blow that was stopped and a blow that never happened. A shield
        /// that cannot react to being struck is an aura, so the one moment the spell exists for
        /// produced nothing at all on screen.</para>
        ///
        /// <para>NOT fired for a hit that was going to do nothing anyway — a zero-damage call,
        /// or one against something already dead. "Blocked" has to mean a blow was turned
        /// away, or a listener flashing on it flashes at nothing.</para>
        /// </summary>
        public event Action<int, GameObject> OnDamageBlocked;

        private void Awake()
        {
            currentHp = maxHp;
        }

        public void Initialize(int max)
        {
            maxHp = max;
            currentHp = max;
            OnHpChanged?.Invoke(currentHp, maxHp);
        }

        /// <summary>
        /// Overload that lets callers set the current HP independently of the
        /// max — used by save/load to restore a damaged pool without going
        /// through <see cref="TakeDamage(int, GameObject, SpellElement?)"/>. Going through
        /// TakeDamage would fire <see cref="OnDamaged"/> +
        /// <c>GameEvents.FireEntityDamaged</c>, which the combat audio + feedback systems
        /// treat as a real hit and play the damage SFX / hit-flash on game boot — the
        /// canonical "player loses HP and you hear the hurt sound the instant the run
        /// starts" bug. This path only fires <see cref="OnHpChanged"/> so the HUD updates
        /// without faking a damage event.
        /// </summary>
        public void Initialize(int max, int current)
        {
            maxHp = max;
            currentHp = Mathf.Clamp(current, 0, max);
            OnHpChanged?.Invoke(currentHp, maxHp);
        }

        /// <summary>
        /// Damage from an unattributed source — a burn tick, hunger, a console command.
        /// Anything with a location should use the overload: without an attacker the damage
        /// events carry no direction, and every system downstream that wants to point at
        /// what hit you is left guessing.
        /// </summary>
        public void TakeDamage(int amount) => TakeDamage(amount, null, null);

        public void TakeDamage(int amount, GameObject attacker) => TakeDamage(amount, attacker, null);

        /// <summary>
        /// Damage from a discrete, attributable hit — a melee swing, a projectile, a slash.
        /// Gated by the post-hit grace window (<see cref="postHitGraceSeconds"/>) and
        /// mitigated by <see cref="defense"/> plus the elemental multiplier for
        /// <paramref name="element"/> (see <see cref="MitigateDamage"/>). Use
        /// <see cref="TakeDotDamage"/> instead for a periodic status effect or zone tick —
        /// routing a DoT through here would let a melee swing landing in the same frame as a
        /// scheduled Burn tick silently eat the tick.
        /// </summary>
        public void TakeDamage(int amount, GameObject attacker, SpellElement? element)
        {
            ApplyDamage(amount, attacker, element, respectGrace: true);
        }

        /// <summary>
        /// Damage from a periodic status effect or zone tick (Burn, Poison, a puddle / cone
        /// breath / laser beam / arcane flame tick). Still mitigated by defense and elemental
        /// resistance — armor and fire-proofing both still apply to a DoT — but NEVER gated
        /// by the post-hit grace window: that window exists to stop several independent
        /// ATTACKERS from stacking hits in one instant, and a DoT tick is not a new attacker,
        /// it is the same already-applied effect continuing to exist. Gating it too would
        /// mean "get hit by anything and your burn stops ticking for a tenth of a second",
        /// which is not a behaviour anyone authored.
        /// </summary>
        public void TakeDotDamage(int amount, GameObject attacker = null, SpellElement? element = null)
        {
            ApplyDamage(amount, attacker, element, respectGrace: false);
        }

        private void ApplyDamage(int amount, GameObject attacker, SpellElement? element, bool respectGrace)
        {
            if (IsDead || amount <= 0) return;

            if (_invincible)
            {
                OnDamageBlocked?.Invoke(amount, attacker);
                return;
            }

            // Spirit-form players are intangible: they have IsDead==false because
            // we don't actually keep them at HP=0 (we re-init HP on revive), but
            // until then the controller sets a flag we honour here.
            if (IsPlayerSpirit()) return;

            if (respectGrace && Time.time < _nextHitAllowedTime) return;

            int mitigated = MitigateDamage(amount, element);
            if (mitigated <= 0) return;

            if (respectGrace) _nextHitAllowedTime = Time.time + postHitGraceSeconds;

            currentHp = Mathf.Max(0, currentHp - mitigated);
            OnDamaged?.Invoke(mitigated);
            OnHpChanged?.Invoke(currentHp, maxHp);

            GameEvents.FireEntityDamaged(gameObject, attacker, mitigated);
            if (gameObject.CompareTag("Player"))
                GameEvents.FirePlayerDamaged(mitigated, currentHp, maxHp);

            if (currentHp <= 0)
            {
                OnDeath?.Invoke();
                GameEvents.FireEntityDied(gameObject, attacker);
                if (gameObject.CompareTag("Player"))
                    GameEvents.FirePlayerDied();
            }
        }

        /// <summary>
        /// amount -&gt; elemental multiplier -&gt; flat defense, floored. Order matters: the
        /// element step models "this attack's damage type doesn't suit me" (can genuinely
        /// zero it out — a real immunity), and only what survives that step is then reduced
        /// by armor (which can never zero a landed hit, only shave it down to
        /// <see cref="MinDamageAfterDefense"/>).
        /// </summary>
        private int MitigateDamage(int amount, SpellElement? element)
        {
            float multiplier = ResolveElementMultiplier(element);
            int elementAdjusted = Mathf.RoundToInt(amount * multiplier);
            if (elementAdjusted <= 0) return 0;

            int afterDefense = Mathf.Max(MinDamageAfterDefense, elementAdjusted - defense);
            if (_vulnerability <= 0f) return afterDefense;

            // Vulnerability amplifies what SURVIVED the armor, deliberately, and it is the
            // last step for that reason. Applying it before defense would let armor eat most
            // of the amplification, so a curse worth +30% would read as +30% against a soft
            // target and near-nothing against the armored one it exists to open up -- the
            // exact opposite of what a player buys it for.
            return Mathf.Max(MinDamageAfterDefense,
                             Mathf.RoundToInt(afterDefense * (1f + _vulnerability)));
        }

        private float ResolveElementMultiplier(SpellElement? element)
        {
            if (element == null || resistances == null) return 1f;
            for (int i = 0; i < resistances.Length; i++)
                if (resistances[i].element == element.Value) return resistances[i].multiplier;
            return 1f;
        }

        /// <summary>Wired from MonsterDefinition.stats.defense by EntitySetup.</summary>
        public void SetDefense(int value) => defense = Mathf.Max(0, value);

        /// <summary>
        /// Extra fraction of damage this entity takes while cursed (0.30 = +30% incoming).
        /// Owned solely by <c>VulnerableEffect</c> -- see the field's comment for why a
        /// second writer would be a regression rather than a feature.
        /// </summary>
        public void SetVulnerability(float extraFraction)
            => _vulnerability = Mathf.Max(0f, extraFraction);

        /// <summary>Current extra-damage fraction, 0 when nothing is cursing this entity.
        /// Read by the hit feedback so a landed blow can say the curse is doing something,
        /// which is otherwise only visible as a number the player has to compare.</summary>
        public float Vulnerability => _vulnerability;

        /// <summary>Wired from MonsterDefinition.stats.resistances by EntitySetup.</summary>
        public void SetResistances(ElementResistance[] value)
            => resistances = value ?? Array.Empty<ElementResistance>();

        /// <summary>
        /// Sets the post-hit grace window (see <see cref="postHitGraceSeconds"/>). Negative
        /// values clamp to 0 (disabled) rather than being rejected outright, since "turn it
        /// off" is a legitimate call a designer or a test can make on purpose.
        /// </summary>
        public void SetPostHitGrace(float seconds) => postHitGraceSeconds = Mathf.Max(0f, seconds);

        /// <summary>Seconds remaining on the post-hit grace window, for UI/telegraph use.</summary>
        public float GraceRemaining => Mathf.Max(0f, _nextHitAllowedTime - Time.time);

        public void Heal(int amount)
        {
            if (IsDead || amount <= 0) return;

            currentHp = Mathf.Min(maxHp, currentHp + amount);
            OnHpChanged?.Invoke(currentHp, maxHp);
        }

        /// <summary>
        /// Permanently increase the max HP cap and grant the matching amount
        /// of current HP. Used by skill-tree stat boosts and item upgrades
        /// that shouldn't simultaneously heal the entity to full (which is
        /// what <see cref="Initialize(int, int)"/> would do). Negative deltas are
        /// rejected to keep this call site distinct from a debuff path.
        /// </summary>
        public void IncreaseMaxHp(int delta)
        {
            if (delta <= 0) return;
            maxHp += delta;
            currentHp += delta;
            OnHpChanged?.Invoke(currentHp, maxHp);
        }

        /// <summary>
        /// Sets the max HP to an ABSOLUTE value, granting the difference when it rises and
        /// clamping current HP when it falls.
        ///
        /// This is the seam <see cref="Valkur.Gameplay.PlayerStats"/> pushes through, and it
        /// exists because <see cref="IncreaseMaxHp"/> cannot be called from a recompute:
        /// that one takes a DELTA, so re-resolving the same layers twice — which happens
        /// every time any unrelated buff expires — would grant the bonus again and heal the
        /// player a little each time. An absolute setter is idempotent by construction, and
        /// idempotence is the whole contract a layered stat store rests on.
        ///
        /// Dead entities are refused: raising the cap on a corpse would resurrect it as far
        /// as <see cref="IsDead"/> is concerned while every death system has already run.
        /// </summary>
        public void SetMaxHp(int newMax)
        {
            newMax = Mathf.Max(1, newMax);
            if (newMax == maxHp) return;
            if (IsDead) { maxHp = newMax; return; }

            int delta = newMax - maxHp;
            maxHp = newMax;
            currentHp = delta > 0
                ? currentHp + delta            // a bigger pool arrives full of the new room
                : Mathf.Min(currentHp, maxHp);  // a smaller one just clips what no longer fits

            OnHpChanged?.Invoke(currentHp, maxHp);
        }

        public void SetInvincible(bool invincible)
        {
            _invincible = invincible;
        }

        private bool IsPlayerSpirit()
        {
            // Lazy lookup. We can't cache "checked-and-missing" because EntitySetup
            // adds PlayerSpiritState AFTER Health.Awake on the player prefab, so a
            // sticky-null cache would freeze the answer at false for the life of
            // the run. Re-querying GetComponent until we find one is cheap (this
            // only runs on damage events, never per-frame).
            if (_spiritState == null) _spiritState = GetComponent<PlayerSpiritState>();
            return _spiritState != null && _spiritState.IsSpirit;
        }
    }
}
