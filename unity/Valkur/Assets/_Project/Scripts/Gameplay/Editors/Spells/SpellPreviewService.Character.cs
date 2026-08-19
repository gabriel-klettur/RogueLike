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
        private void ApplyCharacterDirection()
        {
            if (_characterAnimator == null) return;
            var dir = _characterAnimator.ResolveDirectionFromVector(_direction);
            var state = ResolvePreviewAnimState(_spell);
            _characterAnimator.SetState(state, dir);
        }

        private static Valkur.Gameplay.DirectionalAnimator.AnimState ResolvePreviewAnimState(SpellDefinition spell)
        {
            if (spell != null && spell.type == SpellType.Dash)
                return Valkur.Gameplay.DirectionalAnimator.AnimState.Chase;
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
