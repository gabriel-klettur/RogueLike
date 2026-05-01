using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.FSM;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Centralized combat audio system.
    /// Listens to GameEvents and plays appropriate SFX via IAudioService.
    /// Mirrors Python damage_sfx_system.py + combat_sfx.py behavior.
    /// </summary>
    public class CombatAudioSystem : MonoBehaviour
    {
        private CombatSfxConfigSO _config;
        private IAudioService _audio;

        public void Initialize(CombatSfxConfigSO config)
        {
            _config = config;
        }

        private void OnEnable()
        {
            GameEvents.OnEntityDamaged += OnEntityDamaged;
            GameEvents.OnHitDealt += OnHitDealt;
        }

        private void OnDisable()
        {
            GameEvents.OnEntityDamaged -= OnEntityDamaged;
            GameEvents.OnHitDealt -= OnHitDealt;
        }

        private IAudioService ResolveAudio()
        {
            if (_audio == null)
                _audio = ServiceLocator.Get<IAudioService>();
            return _audio;
        }

        /// <summary>
        /// When any entity takes damage, play their damage vocalization.
        /// Player → player_damage_N, NPC → archetype damage SFX.
        /// </summary>
        private void OnEntityDamaged(GameObject victim, GameObject attacker, int amount)
        {
            if (_config == null || victim == null) return;
            var audio = ResolveAudio();
            if (audio == null) return;

            if (victim.CompareTag("Player"))
            {
                if (_config.PlayerDamageSfxIds != null && _config.PlayerDamageSfxIds.Length > 0)
                    audio.PlaySfxRandom(_config.PlayerDamageSfxIds);
            }
            else
            {
                var brain = victim.GetComponent<FSMMonsterBrain>();
                if (brain != null && brain.Definition != null)
                {
                    string archetype = brain.Definition.monsterKey;
                    var damageSfx = _config.GetNpcDamageSfx(archetype);
                    if (damageSfx != null && damageSfx.Length > 0)
                        audio.PlaySfxRandom(damageSfx);
                }
            }
        }

        /// <summary>
        /// When an entity hits another, play the attacker's weapon/attack SFX.
        /// Player → slash SFX, NPC → archetype attack SFX.
        /// </summary>
        private void OnHitDealt(GameObject attacker, GameObject victim, int damage)
        {
            if (_config == null || attacker == null) return;
            var audio = ResolveAudio();
            if (audio == null) return;

            if (attacker.CompareTag("Player"))
            {
                if (_config.SlashSfxIds != null && _config.SlashSfxIds.Length > 0)
                    audio.PlaySfxRandom(_config.SlashSfxIds);
            }
            else
            {
                var brain = attacker.GetComponent<FSMMonsterBrain>();
                if (brain != null && brain.Definition != null)
                {
                    string archetype = brain.Definition.monsterKey;
                    var attackSfx = _config.GetNpcAttackSfx(archetype);
                    if (attackSfx != null && attackSfx.Length > 0)
                        audio.PlaySfxRandom(attackSfx);
                }
            }
        }
    }
}
