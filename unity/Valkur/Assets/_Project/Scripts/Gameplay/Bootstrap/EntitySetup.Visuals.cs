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
    public static partial class EntitySetup
    {

        private static void InitPlayerStats(GameObject go, PlayerDefinition def)
        {
            var inventory = go.GetComponent<Inventory.Inventory>();
            if (inventory != null) inventory.Initialize(Inventory.Inventory.DefaultBagCapacity);

            var mana = go.GetComponent<Mana>();
            if (mana == null) mana = go.AddComponent<Mana>();
            // Python parity: max mana from max_intelligence.
            mana.Initialize(def.maxIntelligence, def.manaRegenPerSecond);

            // Visual feedback for active mana regeneration. The component
            // resolves its preset lazily from VFXManager so we don't need
            // to coordinate bootstrap order with EnsureVFXManager.
            if (go.GetComponent<ManaRegenAura>() == null)
                go.AddComponent<ManaRegenAura>();

            var xp = go.GetComponent<Experience>();
            if (xp == null) go.AddComponent<Experience>();

            if (go.GetComponent<PickupSystem>() == null)
                go.AddComponent<PickupSystem>();

            // World-drop interactor: hover/select/drag items in the world
            // outside the F7 editor, bounded by the player's interaction range.
            // Mirrors Python's drop_drag_system (drag_drop_range = 128 px).
            if (go.GetComponent<WorldDropInteractor>() == null)
                go.AddComponent<WorldDropInteractor>();

            // Currency wallet (Python: gold field on player entity)
            if (go.GetComponent<CurrencyWallet>() == null)
                go.AddComponent<CurrencyWallet>();

            // Item consumer (Python: ConsumeSystem)
            if (go.GetComponent<Inventory.ItemConsumer>() == null)
                go.AddComponent<Inventory.ItemConsumer>();
        }

        private static void InitSharedVisuals(GameObject go)
        {
            if (go.GetComponent<FloatingDamageSpawner>() == null)
                go.AddComponent<FloatingDamageSpawner>();

            if (go.GetComponent<StatusEffectManager>() == null)
                go.AddComponent<StatusEffectManager>();

            // Combo counter: only on player (tag is set before this call)
            if (go.CompareTag("Player") && go.GetComponent<ComboCounter>() == null)
                go.AddComponent<ComboCounter>();

            // Minimap dot — uses reflection to avoid Gameplay→UI circular dependency
            if (go.CompareTag("Player"))
                ConfigureMinimapDot(go, "Player", new Color(0.2f, 0.95f, 0.3f, 1f));

            var playerBar = go.GetComponent<WorldHealthBar>();
            if (playerBar == null) playerBar = go.AddComponent<WorldHealthBar>();
            playerBar.SetBarColors(
                new Color(0.2f, 0.9f, 0.25f, 1f),
                new Color(0.95f, 0.85f, 0.15f, 1f));
            playerBar.SetHideAtFullHp(false); // Python always shows player health bar

            // World-space dash bar (above health bar) — Python parity
            if (go.GetComponent<WorldDashBar>() == null)
                go.AddComponent<WorldDashBar>();

            // World-space mana bar (above dash bar) — Python parity
            if (go.GetComponent<WorldManaBar>() == null)
                go.AddComponent<WorldManaBar>();

            var ySort = go.GetComponent<YSortEntity>();
            if (ySort == null) ySort = go.AddComponent<YSortEntity>();
            ySort.ZLayerBase = SortingConfig.Z_ENTITY;

            if (go.GetComponent<FacingIndicator>() == null)
                go.AddComponent<FacingIndicator>();

            // Mana-regen silhouette halo: blue rim around the player's body
            // sprite that fades in only while Mana.IsRegenerating is true,
            // matching the trigger used by ManaRegenAura's particles.
            if (go.CompareTag("Player") && go.GetComponent<ManaRegenSilhouette>() == null)
                go.AddComponent<ManaRegenSilhouette>();
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
            var container = GameObject.Find("[UI]");
            if (container != null) uiGo.transform.SetParent(container.transform, false);
        }

        private static void EnsureSpellBarHUD()
        {
            if (Valkur.Gameplay.UI.SpellBarHUD.Instance != null) return;
            var go = new GameObject("SpellBarHUD");
            go.AddComponent<Valkur.Gameplay.UI.SpellBarHUD>();
            var container = GameObject.Find("[UI]");
            if (container != null) go.transform.SetParent(container.transform, false);
        }

        private static void EnsureHUDIconBar()
        {
            if (Valkur.UIKit.HUDIconBar.Instance != null) return;
            var go = new GameObject("HUDIconBar");
            go.AddComponent<Valkur.UIKit.HUDIconBar>();
            var container = GameObject.Find("[UI]");
            if (container != null) go.transform.SetParent(container.transform, false);
        }

        private static void EnsureCombatRangeVisualizer()
        {
            if (CombatRangeVisualizer.Instance != null) return;
            var vizGo = new GameObject("CombatRangeVisualizer");
            vizGo.AddComponent<CombatRangeVisualizer>();
            var container = GameObject.Find("[Debug]");
            if (container != null) vizGo.transform.SetParent(container.transform, false);
        }

        // ── Minimap dot helper (reflection to avoid Gameplay→UI circular dep) ──

        private static System.Type _minimapDotType;
        private static System.Type _minimapDotEnumType;
        private static System.Reflection.MethodInfo _configureMethod;
        private static bool _minimapReflectionFailed;

        private static void ConfigureMinimapDot(GameObject go, string dotTypeName, Color color)
        {
            if (_minimapReflectionFailed) return;

            if (_minimapDotType == null)
            {
                _minimapDotType = System.Type.GetType("Valkur.UI.HUD.MinimapDot, Valkur.UI");
                _minimapDotEnumType = System.Type.GetType("Valkur.UI.HUD.MinimapDotType, Valkur.UI");
                if (_minimapDotType == null || _minimapDotEnumType == null)
                {
                    _minimapReflectionFailed = true;
                    Debug.LogWarning("[EntitySetup] MinimapDot type not found — minimap dots skipped.");
                    return;
                }
                _configureMethod = _minimapDotType.GetMethod("Configure",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            }

            var dot = go.GetComponent(_minimapDotType);
            if (dot == null) dot = go.AddComponent(_minimapDotType);

            if (_configureMethod != null)
            {
                var enumVal = System.Enum.Parse(_minimapDotEnumType, dotTypeName);
                _configureMethod.Invoke(dot, new object[] { enumVal, color });
            }
        }
    }
}