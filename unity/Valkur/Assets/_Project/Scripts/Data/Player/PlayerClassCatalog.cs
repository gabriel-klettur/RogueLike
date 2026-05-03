using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Runtime catalog for playable class presets used by menu selection and gameplay bootstrap.
    /// Values mirror the migrated PlayerDefinition assets in Catalogs/Players.
    /// </summary>
    public static class PlayerClassCatalog
    {
        public readonly struct PlayerClassPreset
        {
            public PlayerClassPreset(
                string playerKey,
                string displayName,
                int maxStrength,
                int maxIntelligence,
                int maxDexterity,
                int initialStrength,
                int initialIntelligence,
                int initialDexterity,
                float basicSpeed,
                int basicAttack,
                int basicArmor,
                float basicDeathTimerDuration,
                float damageStopProbability,
                float manaRegenPerSecond,
                int dashCharges,
                float dragDropRange)
            {
                PlayerKey = playerKey;
                DisplayName = displayName;
                MaxStrength = maxStrength;
                MaxIntelligence = maxIntelligence;
                MaxDexterity = maxDexterity;
                InitialStrength = initialStrength;
                InitialIntelligence = initialIntelligence;
                InitialDexterity = initialDexterity;
                BasicSpeed = basicSpeed;
                BasicAttack = basicAttack;
                BasicArmor = basicArmor;
                BasicDeathTimerDuration = basicDeathTimerDuration;
                DamageStopProbability = damageStopProbability;
                ManaRegenPerSecond = manaRegenPerSecond;
                DashCharges = dashCharges;
                DragDropRange = dragDropRange;
            }

            public string PlayerKey { get; }
            public string DisplayName { get; }
            public int MaxStrength { get; }
            public int MaxIntelligence { get; }
            public int MaxDexterity { get; }
            public int InitialStrength { get; }
            public int InitialIntelligence { get; }
            public int InitialDexterity { get; }
            public float BasicSpeed { get; }
            public int BasicAttack { get; }
            public int BasicArmor { get; }
            public float BasicDeathTimerDuration { get; }
            public float DamageStopProbability { get; }
            public float ManaRegenPerSecond { get; }
            public int DashCharges { get; }
            public float DragDropRange { get; }
        }

        private static readonly PlayerClassPreset[] Presets =
        {
            new PlayerClassPreset("barbarian", "Barbarian", 150, 25, 100, 45, 25, 45, 5f, 2, 2, 8f, 0.25f, 1f, 1, 128f),
            new PlayerClassPreset("elven", "Elven", 70, 55, 100, 45, 45, 45, 6f, 2, 1, 8f, 0.25f, 1f, 1, 128f),
            new PlayerClassPreset("mague", "Mague", 100, 100, 25, 45, 45, 25, 5f, 1, 0, 8f, 0.25f, 1f, 1, 128f),
            new PlayerClassPreset("valkyrie", "Valkyrie", 90, 35, 100, 45, 35, 45, 7f, 2, 1, 8f, 0.25f, 1f, 1, 128f),
            new PlayerClassPreset("dwarf", "Dwarf", 200, 35, 90, 45, 35, 45, 4f, 1, 5, 10f, 0.25f, 1f, 4, 128f)
        };

        public static IReadOnlyList<PlayerClassPreset> AllPresets => Presets;

        public static bool TryGetPreset(string playerKey, out PlayerClassPreset preset)
        {
            if (string.IsNullOrWhiteSpace(playerKey))
            {
                preset = default;
                return false;
            }

            for (int i = 0; i < Presets.Length; i++)
            {
                if (string.Equals(Presets[i].PlayerKey, playerKey, StringComparison.OrdinalIgnoreCase))
                {
                    preset = Presets[i];
                    return true;
                }
            }

            preset = default;
            return false;
        }

        public static PlayerDefinition CreateRuntimeDefinition(string playerKey)
        {
            if (!TryGetPreset(playerKey, out var preset))
                return null;

            var def = ScriptableObject.CreateInstance<PlayerDefinition>();
            ApplyPresetToDefinition(def, preset);
            return def;
        }

        public static void ApplyPresetToDefinition(PlayerDefinition def, PlayerClassPreset preset)
        {
            if (def == null)
                return;

            def.playerKey = preset.PlayerKey;
            def.displayName = preset.DisplayName;

            def.maxStrength = preset.MaxStrength;
            def.maxIntelligence = preset.MaxIntelligence;
            def.maxDexterity = preset.MaxDexterity;

            def.initialStrength = preset.InitialStrength;
            def.initialIntelligence = preset.InitialIntelligence;
            def.initialDexterity = preset.InitialDexterity;

            def.basicSpeed = preset.BasicSpeed;
            def.basicAttack = preset.BasicAttack;
            def.basicArmor = preset.BasicArmor;
            def.basicDeathTimerDuration = preset.BasicDeathTimerDuration;
            def.damageStopProbability = preset.DamageStopProbability;
            def.manaRegenPerSecond = preset.ManaRegenPerSecond;
            def.dashCharges = preset.DashCharges;
            def.dragDropRange = preset.DragDropRange;
        }
    }
}
