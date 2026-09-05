using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Cursed: takes more damage from every source while it lasts.
    ///
    /// <para>WHY IT IS A STATUS EFFECT AND NOT A STAT LAYER. The layered store —
    /// <c>PlayerStats</c> with its seven layers, its published composition rule and its
    /// idempotent push — hangs off the PLAYER. An NPC has <c>EntityStats</c> and no
    /// equivalent, so a debuff aimed at monsters would have to grow a second composition
    /// rule on the monster side, and two composition rules in one project are two rules that
    /// will eventually disagree about the same number. As a status effect it inherits
    /// duration, refresh semantics, the immunity list and the tint layer for free, and it
    /// composes with burns and slows through machinery that already works.</para>
    ///
    /// <para>WHY THE TINT IS WEAK. <c>CurseMarkFX</c> — the sigil this effect attaches over the
    /// target — is what carries the reading; a strongly tinted enemy stops looking like the
    /// creature it is, which costs the player more information than the curse gives them.
    /// That split only works while the sigil actually exists: for as long as this comment
    /// named a rig nobody had written, the curse's entire presence was a 28 % violet wash.</para>
    /// </summary>
    public sealed class VulnerableEffect : StatusEffect
    {
        /// <summary>Extra fraction of damage taken. 0.30 = +30% incoming.</summary>
        private readonly float _extraFraction;
        private Health _health;

        public override StatusEffectKind Kind => StatusEffectKind.Vulnerable;

        public float ExtraFraction => _extraFraction;

        public VulnerableEffect(float duration, float extraFraction = 0.30f, GameObject applier = null)
            : base(duration, applier)
        {
            // Clamped rather than trusted: an author typing 30 instead of 0.30 would
            // otherwise make a target take thirty-one times damage, which is not a tuning
            // mistake anyone would diagnose from the number on screen.
            _extraFraction = Mathf.Clamp(extraFraction, 0f, 3f);
        }

        public override void OnApply(StatusEffectManager target)
        {
            _health = target.GetComponent<Health>();
            _health?.SetVulnerability(_extraFraction);

            var tint = SpriteTintStack.Attach(target);
            if (tint != null)
                target.StartCoroutine(CurseTintRoutine(tint, target));

            // The half that actually reads. It tears itself down off this effect's own
            // expiry, so there is no second lifetime to keep in sync.
            Valkur.Gameplay.Spells.CurseMarkFX.Attach(target.gameObject, this);
        }

        public override void Tick(StatusEffectManager target)
        {
            // Nothing per frame. The multiplier is state on Health, not something that has
            // to be re-asserted -- and re-asserting it every frame is exactly the churn
            // CLAUDE.md records for the cone breath's DoT.
        }

        public override void OnRemove(StatusEffectManager target)
        {
            // Health.SetVulnerability has exactly one owner, so clearing to 0 here is
            // correct and does NOT need the save/restore dance SetInvincible needs. See the
            // field comment on Health._vulnerability.
            _health?.SetVulnerability(0f);
        }

        private System.Collections.IEnumerator CurseTintRoutine(SpriteTintStack tint,
                                                                StatusEffectManager target)
        {
            // Dark violet, held flat. A pulsing body would compete with the sigil, which is
            // the layer doing the actual talking -- the same split RootEffect documents.
            Color curseColor = new Color(0.62f, 0.32f, 0.60f, 1f);

            while (!IsExpired && target != null)
            {
                tint.Set(TintLayer.Vulnerable, Color.Lerp(Color.white, curseColor, 0.28f));
                yield return null;
            }

            if (tint != null) tint.Clear(TintLayer.Vulnerable);
        }
    }
}
