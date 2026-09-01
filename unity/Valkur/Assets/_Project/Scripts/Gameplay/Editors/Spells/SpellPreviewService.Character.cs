using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    public sealed partial class SpellPreviewService
    {
        // ── Character overlay ─────────────────────────────────────────────────────

        /// <summary>
        /// Creates or destroys the character sprite child on the synthetic caster
        /// according to the current value of _showCharacter. Called from
        /// SetShowCharacter and RebuildCasterGo so the overlay survives spell switches.
        ///
        /// When the active player has a DirectionalAnimator, its sprite sets are
        /// cloned onto the preview character and the animator is driven into the Cast
        /// state for the current preview direction.
        /// </summary>
        private void ApplyCharacterState()
        {
            if (!_showCharacter)
            {
                DestroyCharacterGo();
                return;
            }

            if (_casterGo == null) return;

            if (_characterGo != null)
            {
                ApplyCharacterDirection();
                return;
            }

            int previewLayer = ResolvePreviewLayer();
            var player = Object.FindObjectOfType<Valkur.Gameplay.PlayerController>();
            var playerAnim = player != null ? player.GetComponent<Valkur.Gameplay.DirectionalAnimator>() : null;

            _characterGo = new GameObject("SpellPreviewCharacter");
            _characterGo.transform.SetParent(_casterGo.transform, false);
            _characterGo.transform.localPosition = Vector3.zero;
            _characterGo.layer = previewLayer;

            var sr2 = _characterGo.AddComponent<SpriteRenderer>();
            sr2.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            sr2.sortingOrder   = 50;
            // The preview camera lives at OFFSCREEN_Y with no Light2D nearby, so any
            // Sprite-Lit-Default material renders as a black silhouette (CLAUDE.md
            // gotcha). Force the shared unlit material so the sprite shows its real albedo.
            ElementalSprites.EnsureAll();
            sr2.sharedMaterial = ElementalSprites.SharedUnlitMaterial;

            if (playerAnim != null)
            {
                _characterAnimator = _characterGo.AddComponent<Valkur.Gameplay.DirectionalAnimator>();
                _characterAnimator.SetSpriteSets(
                    playerAnim.IdleSprites,
                    playerAnim.WalkSprites,
                    playerAnim.ChaseSprites,
                    playerAnim.CastSprites,
                    playerAnim.AttackSprites,
                    playerAnim.DamageSprites,
                    playerAnim.DeathSprites,
                    playerAnim.PrefersCardinalDirectionSampling);
                // The seven base sets are not the whole character: the variants, their spell
                // reservations and their pacing are what decide which animation a given spell
                // actually plays. Copying them is what lets this preview show the pose a spell
                // is pinned to rather than the base cast every time.
                _characterAnimator.CopyVariantsFrom(playerAnim);
                ApplyPreviewLoadout(player);
                ApplyCharacterDirection();
            }
            else
            {
                Sprite sprite = ResolveFallbackCharacterSprite(player);
                if (sprite == null)
                {
                    Debug.LogWarning("[SpellPreviewService] ShowCharacter: no player " +
                                     "DirectionalAnimator/SpriteRenderer found and player_placeholder " +
                                     "resource missing — skipping character overlay.");
                    DestroyCharacterGo();
                    _showCharacter = false;
                    return;
                }
                sr2.sprite = sprite;
            }
        }

        /// <summary>
        /// Drives the character animator into the appropriate state for the current
        /// preview direction. Movement-style spells (Dash) get the Chase pose so the
        /// preview reads as "the player dashing". Everything else uses the Cast pose.
        /// No-op when the character has no DirectionalAnimator.
        /// </summary>
        /// <summary>
        /// Re-binds the preview rig wearing <c>loadoutAnimKey</c>, when the selected spell
        /// names one.
        ///
        /// Needed because a loadout's LOCOMOTION only exists while the loadout is worn: the
        /// armed idle, walk and run are overrides that the base bind never installs, so the
        /// probes for them showed the unarmed art and looked like they were playing the wrong
        /// animation. Everything else previews whatever the live player is currently wearing,
        /// which is the honest default — the preview mirrors the character as they are.
        ///
        /// The config comes from the player's own <c>PlayerLoadoutController</c>: this rig
        /// mirrors a running character rather than being bound from a definition, and that
        /// component is the only route from one back to its <c>EntityAssetConfig</c>.
        /// </summary>
        private void ApplyPreviewLoadout(Valkur.Gameplay.PlayerController player)
        {
            if (_spell == null || string.IsNullOrEmpty(_spell.loadoutAnimKey)) return;
            if (player == null || _characterGo == null) return;

            var loadouts = player.GetComponent<Valkur.Gameplay.PlayerLoadoutController>();
            var config = loadouts != null ? loadouts.Config : null;
            if (config == null || config.FindLoadout(_spell.loadoutAnimKey) == null) return;

            // The full bind rather than a partial overwrite: it is the same path the game
            // uses, so the fallback chain decides what an artless state shows here exactly as
            // it does in play.
            Valkur.Gameplay.EntityAnimationBinder.ApplyLoadout(
                _characterGo, config, _spell.loadoutAnimKey);
            _characterAnimator = _characterGo.GetComponent<Valkur.Gameplay.DirectionalAnimator>();
        }

        private void ApplyCharacterDirection()
        {
            if (_characterAnimator == null) return;
            var dir = _characterAnimator.ResolveDirectionFromVector(_direction);
            var state = ResolvePreviewAnimState(_spell);

            // Resolve the VARIANT too. Without this the preview called the two-argument
            // SetState, which reuses whatever index happened to be active — -1 on a freshly
            // built rig — so every spell previewed the character's BASE cast pose and the
            // whole point of pinning an animation to a spell was invisible in the one screen
            // built for looking at spells.
            int variant = _spell != null
                ? _characterAnimator.VariantForSpell(state, _spell.spellKey)
                : -1;
            _characterAnimator.SetState(state, dir, variant);

            // SetState EARLY-RETURNS when nothing changed, and on a freshly built animator
            // `_currentState` already reads Idle — the enum's zero. So an Idle preview changed
            // no state, no direction and no variant, AdvanceFrame never ran, and because this
            // rig (unlike EntityAnimationBinder) never seeds renderer.sprite, the character
            // rendered nothing at all. Restarting also gives the behaviour the screen wants
            // anyway: picking a spell replays its animation from frame 0 instead of joining
            // the previous one wherever it happened to be.
            _characterAnimator.RestartCurrentState();
        }

        /// <summary>
        /// Which animation state the preview plays. <c>animState</c> wins when the
        /// spell names one — that is the only way to reach idle, walk, chase, damage, death
        /// and recover, whose states are owned by locomotion and by the damage and death
        /// flows rather than by casting, so no gameplay spell ever enters them.
        ///
        /// The old rules remain the fallback, so every spell authored before the field
        /// existed previews exactly as it did.
        /// </summary>
        private static Valkur.Gameplay.DirectionalAnimator.AnimState ResolvePreviewAnimState(SpellDefinition spell)
        {
            if (spell != null && !string.IsNullOrEmpty(spell.animState) &&
                Valkur.Gameplay.PlayerController.TryParseAnimState(spell.animState, out var named))
                return named;

            if (spell != null && spell.type == SpellType.Dash)
                return Valkur.Gameplay.DirectionalAnimator.AnimState.Chase;
            if (spell != null && spell.usesAttackAnimation)
                return Valkur.Gameplay.DirectionalAnimator.AnimState.Attack;
            if (RegularSlashAttack.Matches(spell))
                return Valkur.Gameplay.DirectionalAnimator.AnimState.Attack;
            return Valkur.Gameplay.DirectionalAnimator.AnimState.Cast;
        }

        private static Sprite ResolveFallbackCharacterSprite(Valkur.Gameplay.PlayerController player)
        {
            if (player != null)
            {
                foreach (var sr in player.GetComponentsInChildren<SpriteRenderer>(includeInactive: true))
                {
                    if (sr != null && sr.sprite != null) return sr.sprite;
                }
            }
            var tex = Resources.Load<Texture2D>("Placeholders/player_placeholder");
            if (tex == null) return null;
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                                 new Vector2(0.5f, 0f), pixelsPerUnit: 16f);
        }

        private void DestroyCharacterGo()
        {
            _characterAnimator = null;
            if (_characterGo == null) return;
            SafeDestroy.Of(_characterGo);
            _characterGo = null;
        }
    }
}
