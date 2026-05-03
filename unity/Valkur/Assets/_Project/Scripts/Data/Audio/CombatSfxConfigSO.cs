using System;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Maps combat archetypes to SFX ID arrays.
    /// Mirrors Python combat_sfx.py: PLAYER_DAMAGE_CHOICES, NPC_DAMAGE_SFX, NPC_ATTACK_SFX.
    /// </summary>
    [CreateAssetMenu(fileName = "CombatSfxConfig", menuName = "Valkur/Audio/Combat SFX Config")]
    public class CombatSfxConfigSO : ScriptableObject
    {
        [Serializable]
        public class ArchetypeSfxMap
        {
            [Tooltip("Archetype key (e.g. 'barbol', 'player')")]
            public string archetype;

            [Tooltip("SFX catalog IDs for damage sounds")]
            public string[] damageSfxIds = Array.Empty<string>();

            [Tooltip("SFX catalog IDs for attack sounds")]
            public string[] attackSfxIds = Array.Empty<string>();
        }

        [Header("Player")]
        [Tooltip("SFX IDs for player taking damage (Python: player_damage_1..22)")]
        [SerializeField] private string[] playerDamageSfxIds = Array.Empty<string>();

        [Header("NPC Archetypes")]
        [Tooltip("Per-archetype SFX mappings")]
        [SerializeField] private ArchetypeSfxMap[] npcArchetypes = Array.Empty<ArchetypeSfxMap>();

        [Header("Melee/Spell SFX")]
        [Tooltip("SFX IDs for sword slash (Python: sword_clash_1..10)")]
        [SerializeField] private string[] slashSfxIds = Array.Empty<string>();

        [Tooltip("SFX ID for fireball cast")]
        [SerializeField] private string fireballSfxId = "fireball";

        [Header("Lifecycle SFX (Wave B audio coverage)")]
        [Tooltip("SFX IDs played when an NPC dies. Random pick if multiple.")]
        [SerializeField] private string[] npcDeathSfxIds = Array.Empty<string>();

        [Tooltip("SFX IDs played when the player dies (e.g. game-over sting).")]
        [SerializeField] private string[] playerDeathSfxIds = Array.Empty<string>();

        [Tooltip("SFX ID played on level-up fanfare.")]
        [SerializeField] private string levelUpSfxId = string.Empty;

        [Tooltip("SFX ID played when the player picks up any item (coin, potion, gear).")]
        [SerializeField] private string itemPickupSfxId = string.Empty;

        // ── Public API ───────────────────────────────────────────────────────

        public string[] PlayerDamageSfxIds => playerDamageSfxIds;
        public string[] SlashSfxIds        => slashSfxIds;
        public string FireballSfxId        => fireballSfxId;
        public string[] NpcDeathSfxIds     => npcDeathSfxIds;
        public string[] PlayerDeathSfxIds  => playerDeathSfxIds;
        public string LevelUpSfxId         => levelUpSfxId;
        public string ItemPickupSfxId      => itemPickupSfxId;

        public string[] GetNpcDamageSfx(string archetype)
        {
            foreach (var m in npcArchetypes)
                if (string.Equals(m.archetype, archetype, StringComparison.OrdinalIgnoreCase))
                    return m.damageSfxIds;
            return Array.Empty<string>();
        }

        public string[] GetNpcAttackSfx(string archetype)
        {
            foreach (var m in npcArchetypes)
                if (string.Equals(m.archetype, archetype, StringComparison.OrdinalIgnoreCase))
                    return m.attackSfxIds;
            return Array.Empty<string>();
        }

#if UNITY_EDITOR
        public void EditorSetPlayerDamage(string[] ids) { playerDamageSfxIds = ids; }
        public void EditorSetNpcArchetypes(ArchetypeSfxMap[] maps) { npcArchetypes = maps; }
        public void EditorSetSlashSfx(string[] ids) { slashSfxIds = ids; }
        public void EditorSetFireballSfx(string id) { fireballSfxId = id; }
        public void EditorSetNpcDeath(string[] ids) { npcDeathSfxIds = ids; }
        public void EditorSetPlayerDeath(string[] ids) { playerDeathSfxIds = ids; }
        public void EditorSetLevelUp(string id) { levelUpSfxId = id; }
        public void EditorSetItemPickup(string id) { itemPickupSfxId = id; }
#endif
    }
}
