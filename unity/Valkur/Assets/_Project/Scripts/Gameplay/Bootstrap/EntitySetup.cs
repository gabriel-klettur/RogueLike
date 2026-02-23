using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.World;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.Spells;
using TMPro;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Configures entity GameObjects from ScriptableObject definitions.
    /// Delegates sprite setup to EntitySpriteHelper and prefab creation to ProjectilePrefabFactory.
    /// </summary>
    public static class EntitySetup
    {
        private static readonly int PlayerLayer = SafeNameToLayer("Player");
        private static readonly int NPCLayer = SafeNameToLayer("NPC");

        private static int SafeNameToLayer(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer == -1)
                Debug.LogWarning($"[EntitySetup] Layer '{layerName}' not found in TagManager! Falling back to Default (0).");
            return layer == -1 ? 0 : layer;
        }

        public static void ConfigurePlayer(GameObject go, PlayerDefinition def)
        {
            go.layer = PlayerLayer;
            go.tag = "Player";

            EntitySpriteHelper.EnsurePlayerSprite(go.GetComponentInChildren<SpriteRenderer>());

            // Python parity: selected class defines player max HP from max_strength.
            InitHealth(go, def.maxStrength);
            InitPlayerMovement(go, def.basicSpeed);
            InitPlayerCombat(go, def);
            InitPlayerSpells(go);
            InitPlayerStats(go, def);
            InitSharedVisuals(go);
            ApplyPlayerClassInitialMarker(go, def.playerKey);

            EnsureInventoryUI();
            EnsureCombatRangeVisualizer();

            EntityRegistry.RegisterPlayer(go);
            Debug.Log($"[EntitySetup] Player configured: key={def.playerKey}, HP={def.maxStrength}, MP={def.maxIntelligence}, ATK={def.basicAttack}, SPD={def.basicSpeed}");
        }

        public static void ConfigureMonster(GameObject go, MonsterDefinition def)
        {
            go.layer = NPCLayer;
            go.tag = "Monster";

            var brain = go.GetComponent<FSMMonsterBrain>();
            if (brain != null) brain.Initialize(def);

            var combat = go.GetComponent<MeleeCombat>();
            if (combat != null)
            {
                combat.SetTargetLayers(1 << PlayerLayer);
                combat.SetSlashVfxColor(new Color(0.2f, 0.9f, 0.3f, 0.8f));
            }

            EntitySpriteHelper.EnsureMonsterSprite(go.GetComponentInChildren<SpriteRenderer>());
            InitHealth(go, def.stats.hp);

            if (go.GetComponent<FloatingDamageSpawner>() == null)
                go.AddComponent<FloatingDamageSpawner>();

            var npcBar = go.GetComponent<WorldHealthBar>();
            if (npcBar == null) npcBar = go.AddComponent<WorldHealthBar>();
            npcBar.SetBarColors(
                new Color(0.9f, 0.25f, 0.2f, 1f),
                new Color(0.95f, 0.15f, 0.1f, 1f));

            var ySort = go.GetComponent<YSortEntity>();
            if (ySort == null) ySort = go.AddComponent<YSortEntity>();
            ySort.ZLayerBase = SortingConfig.Z_ENTITY;

            EntityRegistry.RegisterMonster(go);
            Debug.Log($"[EntitySetup] Monster configured: {def.displayName}, HP={def.stats.hp}");
        }

        // ── Private helpers ──

        private static void InitHealth(GameObject go, int maxHp)
        {
            var health = go.GetComponent<Health>();
            if (health != null) health.Initialize(maxHp);
        }

        private static void InitPlayerMovement(GameObject go, float speed)
        {
            var controller = go.GetComponent<PlayerController>();
            if (controller != null) controller.SetMoveSpeed(speed);
        }

        private static void InitPlayerCombat(GameObject go, PlayerDefinition def)
        {
            var combat = go.GetComponent<MeleeCombat>();
            if (combat != null)
            {
                combat.Initialize(def.basicAttack, 0.5f, 1.5f);
                combat.SetTargetLayers(1 << NPCLayer);
            }

            var dash = go.GetComponent<DashAbility>();
            if (dash != null) dash.SetTargetLayers(1 << NPCLayer);
        }

        private static void InitPlayerSpells(GameObject go)
        {
            var caster = go.GetComponent<SpellCaster>();
            if (caster == null) return;

            caster.SetTargetLayers(1 << NPCLayer);
            caster.SetSpell(0, ProjectilePrefabFactory.GetFireballSpell());
            ProjectilePrefabFactory.EnsureFireballPrefab(caster);
        }

        private static void InitPlayerStats(GameObject go, PlayerDefinition def)
        {
            var inventory = go.GetComponent<Inventory.Inventory>();
            if (inventory != null) inventory.Initialize(20);

            var mana = go.GetComponent<Mana>();
            if (mana == null) mana = go.AddComponent<Mana>();
            // Python parity: max mana from max_intelligence.
            mana.Initialize(def.maxIntelligence, def.manaRegenPerSecond);

            var xp = go.GetComponent<Experience>();
            if (xp == null) go.AddComponent<Experience>();

            if (go.GetComponent<PickupSystem>() == null)
                go.AddComponent<PickupSystem>();
        }

        private static void InitSharedVisuals(GameObject go)
        {
            if (go.GetComponent<FloatingDamageSpawner>() == null)
                go.AddComponent<FloatingDamageSpawner>();

            var playerBar = go.GetComponent<WorldHealthBar>();
            if (playerBar == null) playerBar = go.AddComponent<WorldHealthBar>();
            playerBar.SetBarColors(
                new Color(0.2f, 0.9f, 0.25f, 1f),
                new Color(0.95f, 0.85f, 0.15f, 1f));

            var ySort = go.GetComponent<YSortEntity>();
            if (ySort == null) ySort = go.AddComponent<YSortEntity>();
            ySort.ZLayerBase = SortingConfig.Z_ENTITY;

            if (go.GetComponent<FacingIndicator>() == null)
                go.AddComponent<FacingIndicator>();
        }

        private static void ApplyPlayerClassInitialMarker(GameObject go, string playerKey)
        {
            if (go == null || string.IsNullOrWhiteSpace(playerKey))
                return;

            var markerTransform = go.transform.Find("PlayerClassInitialMarker");
            TextMeshPro markerText;
            if (markerTransform == null)
            {
                var markerGo = new GameObject("PlayerClassInitialMarker");
                markerGo.transform.SetParent(go.transform, false);
                markerGo.transform.localPosition = new Vector3(0f, 0f, 0f);
                markerGo.transform.localRotation = Quaternion.identity;
                markerGo.transform.localScale = Vector3.one * 0.18f;
                markerText = markerGo.AddComponent<TextMeshPro>();
            }
            else
            {
                markerText = markerTransform.GetComponent<TextMeshPro>();
                if (markerText == null)
                    markerText = markerTransform.gameObject.AddComponent<TextMeshPro>();
            }

            markerText.text = char.ToUpperInvariant(playerKey[0]).ToString();
            markerText.alignment = TextAlignmentOptions.Center;
            markerText.enableWordWrapping = false;
            markerText.fontSize = 20f;
            markerText.color = new Color(0.95f, 0.96f, 1f, 0.95f);

            var renderer = markerText.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sortingLayerName = SortingConfig.LAYER_ENTITIES;
                renderer.sortingOrder = SortingConfig.Z_SKY + 20;
            }
        }

        private static void EnsureInventoryUI()
        {
            if (InventoryUI.Instance != null) return;
            var uiGo = new GameObject("InventoryUI");
            uiGo.AddComponent<InventoryUI>();
        }

        private static void EnsureCombatRangeVisualizer()
        {
            if (CombatRangeVisualizer.Instance != null) return;
            var vizGo = new GameObject("CombatRangeVisualizer");
            vizGo.AddComponent<CombatRangeVisualizer>();
        }
    }
}
