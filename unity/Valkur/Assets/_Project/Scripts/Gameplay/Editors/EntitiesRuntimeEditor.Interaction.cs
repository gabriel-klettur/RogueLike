using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// Interaction layer: mode switching, picker refresh, structured property
    /// rendering, map click-handling. UI-only phase — spawn/delete/persist
    /// remain stubs but emit status messages so the workflow can be exercised.
    /// </summary>
    public partial class EntitiesRuntimeEditor : SingletonMonoBehaviour<EntitiesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // ── Category & mode highlighting ───────────────────────────────────────

        private void SelectCategory(EntityCategory cat)
        {
            _category = cat;
            RefreshCategoryTabs();
            RefreshPicker();
        }

        private void RefreshCategoryTabs()
        {
            void Apply(Image img, TextMeshProUGUI tmp, bool on)
            {
                if (img != null) img.color = on ? EditorUIHelpers.SLOT_SELECTED : EditorUIHelpers.BTN_NORMAL;
                if (tmp != null) tmp.color = on ? ACCENT                       : TEXT_PRIMARY;
            }
            Apply(_ui.HostilesTabImg, _ui.HostilesTabTmp, _category == EntityCategory.Hostiles);
            Apply(_ui.NeutralsTabImg, _ui.NeutralsTabTmp, _category == EntityCategory.Neutrals);
            Apply(_ui.SpecialsTabImg, _ui.SpecialsTabTmp, _category == EntityCategory.Specials);
            Apply(_ui.PlayersTabImg,  _ui.PlayersTabTmp,  _category == EntityCategory.Players);
        }

        private void SetMode(EditorMode mode)
        {
            _mode = mode;
            RefreshModeButtons();
            SetStatus(_mode switch
            {
                EditorMode.Select      => "Select mode. Click an entity on the map.",
                EditorMode.Spawn       => string.IsNullOrEmpty(_selectedKey)
                    ? "Spawn mode: select an entity in the Picker first."
                    : $"Spawn mode: click on map to place '{_selectedKey}'.",
                EditorMode.Delete      => "Delete mode: click entity to remove.",
                EditorMode.AddOnSystem => "Add-On-System: define new entity (use Confirm to persist).",
                _                      => ""
            });
        }

        private void RefreshModeButtons()
        {
            void Apply(Image img, TextMeshProUGUI tmp, bool on, bool danger = false)
            {
                if (img != null)
                {
                    img.color = on
                        ? (danger ? EditorUIHelpers.DANGER : EditorUIHelpers.BTN_ACTIVE)
                        : EditorUIHelpers.BTN_NORMAL;
                }
                if (tmp != null) tmp.color = on ? ACCENT : TEXT_PRIMARY;
            }
            Apply(_ui.AddBtnImg,         _ui.AddBtnTmp,         _mode == EditorMode.Spawn);
            Apply(_ui.RemoveBtnImg,      _ui.RemoveBtnTmp,      _mode == EditorMode.Delete, danger: true);
            Apply(_ui.AddOnSystemBtnImg, _ui.AddOnSystemBtnTmp, _mode == EditorMode.AddOnSystem);
            // Confirm is an action button, not a mode — keep neutral.
            Apply(_ui.ConfirmBtnImg,     _ui.ConfirmBtnTmp,     false);
        }

        // ── Picker ──────────────────────────────────────────────────────────────

        private void RefreshPicker()
        {
            if (_ui.PickerContent == null) return;

            for (int i = _ui.PickerContent.childCount - 1; i >= 0; i--)
                Destroy(_ui.PickerContent.GetChild(i).gameObject);

            string filter = _searchFilter?.Trim().ToLowerInvariant() ?? "";
            int shown = 0;

            if (_category == EntityCategory.Players)
            {
                foreach (var preset in PlayerClassCatalog.AllPresets)
                {
                    string key  = preset.PlayerKey;
                    string name = preset.DisplayName ?? key;
                    if (!PassesFilter(name, key, filter)) continue;
                    shown++;
                    Sprite playerIcon = ResolvePlayerSouthSprite(key);
                    AddPickerSlot(name, key, isPlayer: true, sprite: playerIcon, tint: Color.white);
                }
            }
            else if (_monsterCatalog != null)
            {
                foreach (var def in _monsterCatalog.Definitions)
                {
                    if (!MatchesCategory(def, _category)) continue;

                    string key  = def.monsterKey;
                    string name = def.displayName ?? key;
                    if (!PassesFilter(name, key, filter)) continue;
                    shown++;

                    Sprite icon = null;
                    if (def.assetConfig != null && def.assetConfig.idle.south != null)
                        icon = def.assetConfig.idle.south;
                    Color tint = def.assetConfig != null
                        ? NormalizeTint(def.assetConfig.scaleConfig.tint)
                        : Color.white;
                    AddPickerSlot(name, key, isPlayer: false, sprite: icon, tint: tint);
                }
            }

            string label = _category switch
            {
                EntityCategory.Hostiles => "hostiles",
                EntityCategory.Neutrals => "neutrals",
                EntityCategory.Specials => "specials",
                EntityCategory.Players  => "players",
                _                       => "entities"
            };
            SetStatus(filter.Length == 0
                ? $"{shown} {label}"
                : $"{shown} {label} match '{_searchFilter}'");
        }

        private static bool PassesFilter(string name, string key, string filter)
        {
            if (filter.Length == 0) return true;
            return (name ?? "").ToLowerInvariant().Contains(filter)
                || (key  ?? "").ToLowerInvariant().Contains(filter);
        }

        private static bool MatchesCategory(MonsterDefinition def, EntityCategory cat)
        {
            // Heuristic until Python neutrals/specials JSONs are imported.
            string k = (def.monsterKey ?? "").ToLowerInvariant();
            bool isSpecial  = k.Contains("boss") || k.Contains("special");
            bool isNeutral  = k.Contains("neutral")
                           || k.Contains("merchant") || k.Contains("vendor")
                           || k.Contains("guard")    || k.Contains("civilian");
            return cat switch
            {
                EntityCategory.Specials => isSpecial,
                EntityCategory.Neutrals => isNeutral && !isSpecial,
                EntityCategory.Hostiles => !isSpecial && !isNeutral,
                _                       => false
            };
        }

        private void AddPickerSlot(string name, string key, bool isPlayer, Sprite sprite, Color tint)
        {
            var (btn, icon, label) = EditorUIHelpers.MakeSlotButton(
                _ui.PickerContent, name, 72f,
                () => { if (isPlayer) SelectPlayerClass(key); else SelectEntity(key); });

            if (sprite != null) { icon.sprite = sprite; icon.enabled = true; }
            icon.color = tint;
            label.text = TruncateName(name, 9);

            if (key == _selectedKey)
                btn.GetComponent<Image>().color = EditorUIHelpers.SLOT_SELECTED;

            // Drag-from-picker (Buildings parity): LMB-pressing the slot starts a
            // drag; releasing over the map spawns the entity at that point.
            string capturedKey      = key;
            bool   capturedIsPlayer = isPlayer;
            Sprite capturedSprite   = sprite;
            Color  capturedTint     = tint;
            var et  = btn.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var pde = new UnityEngine.EventSystems.EventTrigger.Entry {
                eventID = UnityEngine.EventSystems.EventTriggerType.PointerDown
            };
            pde.callback.AddListener(_ =>
                OnPickerSlotPointerDown(capturedKey, capturedIsPlayer, capturedSprite, capturedTint));
            et.triggers.Add(pde);
        }

        /// <summary>
        /// Mirrors Pygame's <c>BLEND_RGB_MULT</c> tint behaviour. A fully transparent
        /// black (the default value of an uninitialized struct) means "no tint
        /// configured" and is promoted to white so the icon renders normally. Alpha
        /// is always forced to 1 because the icon must remain fully opaque.
        /// </summary>
        private static Color NormalizeTint(Color c)
        {
            if (c.a <= 0f && c.r == 0f && c.g == 0f && c.b == 0f)
                return Color.white;
            return new Color(c.r, c.g, c.b, 1f);
        }

        // ── Player icons ────────────────────────────────────────────────────────
        //
        // The picker shows player classes using their south-facing idle sprite
        // (Python parity: ClassSelectorManager renders the down/idle frame).
        // PlayerDefinition assets live under Assets/_Project/Data/Catalogs/Players
        // and use the spritesheet path (`idleSheets`) where frames are arranged in
        // 8 contiguous direction buckets of 5 frames; bucket 0 == South.

        private Dictionary<string, Sprite> _playerIconCache;
        private PlayerDefinition[] _allPlayerDefsCache;

        private Sprite ResolvePlayerSouthSprite(string playerKey)
        {
            if (string.IsNullOrEmpty(playerKey)) return null;

            _playerIconCache ??= new Dictionary<string, Sprite>(System.StringComparer.OrdinalIgnoreCase);
            if (_playerIconCache.TryGetValue(playerKey, out var cached) && cached != null)
                return cached;

            var def = FindPlayerDefinition(playerKey);
            if (def == null || def.assetConfig == null) return null;

            // Prefer explicit directional sprite if configured.
            Sprite icon = def.assetConfig.idle.south;

            // Fallback: first frame of the idle spritesheet (south-facing in the
            // 8-direction × 5-frame layout used by all migrated player sheets).
            if (icon == null && def.assetConfig.idleSheets != null)
            {
                for (int i = 0; i < def.assetConfig.idleSheets.Count; i++)
                {
                    if (def.assetConfig.idleSheets[i] != null)
                    {
                        icon = def.assetConfig.idleSheets[i];
                        break;
                    }
                }
            }

            _playerIconCache[playerKey] = icon;
            return icon;
        }

        private PlayerDefinition FindPlayerDefinition(string playerKey)
        {
            if (_allPlayerDefsCache == null || _allPlayerDefsCache.Length == 0)
            {
                // Resources.FindObjectsOfTypeAll picks up SOs already loaded into
                // memory (the bootstrap loads at least the default + selected def).
                var loaded = Resources.FindObjectsOfTypeAll<PlayerDefinition>();
#if UNITY_EDITOR
                if (loaded == null || loaded.Length < PlayerClassCatalog.AllPresets.Count)
                {
                    var all = new List<PlayerDefinition>(loaded ?? System.Array.Empty<PlayerDefinition>());
                    var guids = UnityEditor.AssetDatabase.FindAssets(
                        "t:PlayerDefinition",
                        new[] { "Assets/_Project/Data/Catalogs/Players" });
                    foreach (var guid in guids)
                    {
                        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                        var def = UnityEditor.AssetDatabase.LoadAssetAtPath<PlayerDefinition>(path);
                        if (def != null && !all.Contains(def)) all.Add(def);
                    }
                    loaded = all.ToArray();
                }
#endif
                _allPlayerDefsCache = loaded ?? System.Array.Empty<PlayerDefinition>();
            }

            for (int i = 0; i < _allPlayerDefsCache.Length; i++)
            {
                var def = _allPlayerDefsCache[i];
                if (def != null && string.Equals(def.playerKey, playerKey,
                        System.StringComparison.OrdinalIgnoreCase))
                    return def;
            }
            return null;
        }

        // ── Selection → properties ──────────────────────────────────────────────

        private void SelectEntity(string key)
        {
            _selectedKey      = key;
            _selectedIsPlayer = false;
            RefreshPicker();
            ShowMonsterProperties(key);
        }

        private void SelectPlayerClass(string key)
        {
            _selectedKey      = key;
            _selectedIsPlayer = true;
            RefreshPicker();
            ShowPlayerProperties(key);
        }

        private void ShowMonsterProperties(string key)
        {
            ClearPropsSections();
            if (_monsterCatalog == null) { ShowPropsHint("Monster catalog not assigned."); return; }

            var def = _monsterCatalog.GetByKey(key);
            if (def == null) { ShowPropsHint($"Entity '{key}' not found."); return; }

            HidePropsHint();
            var s = def.stats;

            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsIdentitySection, "Key",  def.monsterKey);
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsIdentitySection, "Name", def.displayName);

            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsStatsSection, "HP",            s.hp.ToString());
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsStatsSection, "Speed",         $"{s.speed} / chase {s.chasingSpeed}");
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsStatsSection, "Defense",       s.defense.ToString());
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsStatsSection, "Power",         s.power.ToString());
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsStatsSection, "Melee Dmg",     s.meleeDamage.ToString());
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsStatsSection, "Melee Range",   s.meleeRange.ToString());
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsStatsSection, "Melee CD",      $"{s.meleeCooldown:F2}s");
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsStatsSection, "Aggro Range",   s.aggroRange.ToString());
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsStatsSection, "Atk Windup",    $"{s.attackWindupSeconds:F2}s");

            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsAISection, "FSM Set",   def.fsmSet);
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsAISection, "Patrol",    def.patrolType.ToString());
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsAISection, "Telegraph", def.useAttackTelegraph ? "yes" : "no");

            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsSpawnSection, "Count",   s.spawnCount.ToString());
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsSpawnSection, "Padding", s.spawnPadding.ToString());
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsSpawnSection, "Margin",  s.spawnMargin.ToString());
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsSpawnSection, "Faction", s.faction);

            if (def.autoCast && def.autoCastList != null && def.autoCastList.Length > 0)
            {
                int i = 0;
                foreach (var spell in def.autoCastList)
                    EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsAutoCastSection, $"#{++i}", spell);
            }
            else
            {
                EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsAutoCastSection, "—", "no auto-cast");
            }

            string idle = (def.assetConfig != null && def.assetConfig.idle.south != null)
                ? def.assetConfig.idle.south.name : "—";
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsAssetsSection, "Idle Sprite", idle);

            SetStatus($"Selected: {def.displayName ?? key}");
        }

        private void ShowPlayerProperties(string key)
        {
            ClearPropsSections();
            if (!PlayerClassCatalog.TryGetPreset(key, out var p))
            {
                ShowPropsHint($"Player class '{key}' not found."); return;
            }
            HidePropsHint();

            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsIdentitySection, "Key",  p.PlayerKey);
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsIdentitySection, "Name", p.DisplayName);

            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsStatsSection, "Strength",     $"{p.InitialStrength} / {p.MaxStrength}");
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsStatsSection, "Intelligence", $"{p.InitialIntelligence} / {p.MaxIntelligence}");
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsStatsSection, "Dexterity",    $"{p.InitialDexterity} / {p.MaxDexterity}");
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsStatsSection, "Attack",       p.BasicAttack.ToString());
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsStatsSection, "Armor",        p.BasicArmor.ToString());
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsStatsSection, "Speed",        p.BasicSpeed.ToString());
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsStatsSection, "Mana Regen",   $"{p.ManaRegenPerSecond}/s");

            SetStatus($"Selected: {p.DisplayName ?? key}");
        }

        private void ClearPropsSections()
        {
            EntitiesEditorUIBuilder.ClearSection(_ui.PropsIdentitySection);
            EntitiesEditorUIBuilder.ClearSection(_ui.PropsStatsSection);
            EntitiesEditorUIBuilder.ClearSection(_ui.PropsAISection);
            EntitiesEditorUIBuilder.ClearSection(_ui.PropsSpawnSection);
            EntitiesEditorUIBuilder.ClearSection(_ui.PropsAutoCastSection);
            EntitiesEditorUIBuilder.ClearSection(_ui.PropsAssetsSection);
        }

        private void ShowPropsHint(string msg)
        {
            if (_ui.PropsHintText == null) return;
            _ui.PropsHintText.text       = msg;
            _ui.PropsHintText.gameObject.SetActive(true);
        }

        private void HidePropsHint()
        {
            if (_ui.PropsHintText == null) return;
            _ui.PropsHintText.gameObject.SetActive(false);
        }

        // ── Map Interaction ─────────────────────────────────────────────────────

        private void HandleMapInteraction()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

            var cam = Camera.main;
            if (cam == null) return;

            var worldPos = cam.ScreenToWorldPoint(mouse.position.ReadValue());
            worldPos.z = 0;

            switch (_mode)
            {
                case EditorMode.Spawn       when !string.IsNullOrEmpty(_selectedKey): SpawnEntityAtPosition(worldPos);  break;
                case EditorMode.Delete:                                                DeleteEntityAtPosition(worldPos); break;
                case EditorMode.Select:                                                SelectEntityAtPosition(worldPos); break;
                case EditorMode.AddOnSystem when !string.IsNullOrEmpty(_selectedKey): SetStatus($"Add-On-System: ({worldPos.x:F1}, {worldPos.y:F1}) — Confirm to persist."); break;
            }
        }

        private void SpawnEntityAtPosition(Vector3 worldPos)
        {
            // UI-only stub — spawn pipeline integration is the next phase.
            SetStatus($"Spawn '{_selectedKey}' at ({worldPos.x:F1}, {worldPos.y:F1})  [stub]");
            Debug.Log($"[EntitiesEditor] Spawn {_selectedKey} at {worldPos}");
        }

        private void DeleteEntityAtPosition(Vector3 worldPos)
        {
            var hit = Physics2D.OverlapCircle(worldPos, 0.5f, LayerMask.GetMask("NPC"));
            if (hit != null)
            {
                SetStatus($"Deleted {hit.gameObject.name}");
                Debug.Log($"[EntitiesEditor] Deleted {hit.gameObject.name}");
                Destroy(hit.gameObject);
            }
            else
            {
                SetStatus("No entity under cursor.");
            }
        }

        private void SelectEntityAtPosition(Vector3 worldPos)
        {
            var hit = Physics2D.OverlapCircle(worldPos, 0.5f, LayerMask.GetMask("NPC"));
            if (hit != null)
            {
                var brain = hit.GetComponent<Valkur.Gameplay.FSM.FSMMonsterBrain>();
                SetStatus(brain != null
                    ? $"Selected: {hit.gameObject.name}"
                    : $"Hit: {hit.gameObject.name} (no brain)");
            }
            else
            {
                SetStatus("Nothing under cursor.");
            }
        }

        private static string TruncateName(string name, int max)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.Length <= max ? name : name.Substring(0, max - 1) + "\u2026";
        }
    }
}
