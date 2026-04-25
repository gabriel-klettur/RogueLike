using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Editors.EditorKit;

namespace Valkur.Gameplay.Entities
{
    public partial class EntitiesRuntimeEditor : SingletonMonoBehaviour<EntitiesRuntimeEditor>, GameEditorManager.IGameEditor
    {

        private void SetMode(EditorMode mode)
        {
            _mode = mode;
            RefreshModeButtons();
            _statusTmp.text = _mode switch
            {
                EditorMode.Select => "Select mode. Click entity on map.",
                EditorMode.Spawn => _selectedKey != null ? $"Spawn mode: click to place {_selectedKey}" : "Select an entity first.",
                EditorMode.Delete => "Delete mode: click entity to remove.",
                _ => ""
            };
        }

        private void RefreshModeButtons()
        {
            if (_selectBtnImg) _selectBtnImg.color = _mode == EditorMode.Select ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_spawnBtnImg) _spawnBtnImg.color = _mode == EditorMode.Spawn ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_deleteBtnImg) _deleteBtnImg.color = _mode == EditorMode.Delete ? EditorUIHelpers.DANGER : new Color(0.55f, 0.15f, 0.15f, 1f);
        }

        // ── Picker ──

        private void RefreshPicker()
        {
            for (int i = _pickerContent.childCount - 1; i >= 0; i--)
                Destroy(_pickerContent.GetChild(i).gameObject);

            string filter = _searchFilter?.Trim().ToLowerInvariant() ?? "";
            int shown = 0;

            if (_category == EntityCategory.Hostiles && _monsterCatalog != null)
            {
                foreach (var def in _monsterCatalog.Definitions)
                {
                    var key = def.monsterKey;
                    if (filter.Length > 0)
                    {
                        string n = (def.displayName ?? key ?? "").ToLowerInvariant();
                        if (!n.Contains(filter) && !(key ?? "").ToLowerInvariant().Contains(filter)) continue;
                    }
                    shown++;
                    var (btn, icon, label) = EditorUIHelpers.MakeSlotButton(
                        _pickerContent, def.displayName ?? key, 72f,
                        () => SelectEntity(key));

                    if (def.assetConfig != null && def.assetConfig.idle.south != null)
                    {
                        icon.sprite = def.assetConfig.idle.south;
                        icon.enabled = true;
                    }
                    label.text = TruncateName(def.displayName ?? key, 9);

                    if (key == _selectedKey)
                        btn.GetComponent<Image>().color = EditorUIHelpers.SLOT_SELECTED;
                }
            }
            else if (_category == EntityCategory.Players)
            {
                foreach (var preset in PlayerClassCatalog.AllPresets)
                {
                    var key = preset.PlayerKey;
                    if (filter.Length > 0)
                    {
                        string n = (preset.DisplayName ?? key ?? "").ToLowerInvariant();
                        if (!n.Contains(filter) && !(key ?? "").ToLowerInvariant().Contains(filter)) continue;
                    }
                    shown++;
                    var (btn, icon, label) = EditorUIHelpers.MakeSlotButton(
                        _pickerContent, preset.DisplayName ?? key, 72f,
                        () => SelectPlayerClass(key));
                    label.text = TruncateName(preset.DisplayName ?? key, 9);
                    if (key == _selectedKey)
                        btn.GetComponent<Image>().color = EditorUIHelpers.SLOT_SELECTED;
                }
            }
            if (_statusTmp != null)
                _statusTmp.text = filter.Length == 0 ? $"{shown} entities" : $"{shown} match '{_searchFilter}'";
        }

        private void SelectEntity(string key)
        {
            _selectedKey = key;
            RefreshPicker();
            ShowMonsterProperties(key);
        }

        private void SelectPlayerClass(string key)
        {
            _selectedKey = key;
            RefreshPicker();
            ShowPlayerProperties(key);
        }

        private void ShowMonsterProperties(string key)
        {
            if (_monsterCatalog == null) return;
            var def = _monsterCatalog.GetByKey(key);
            if (def == null) { _propsTmp.text = "Not found."; return; }

            var s = def.stats;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>Key:</b> {def.monsterKey}");
            sb.AppendLine($"<b>Name:</b> {def.displayName}");
            sb.AppendLine();
            sb.AppendLine("<b>── Stats ──</b>");
            sb.AppendLine($"HP: {s.hp}");
            sb.AppendLine($"Speed: {s.speed}  Chase: {s.chasingSpeed}");
            sb.AppendLine($"Defense: {s.defense}  Power: {s.power}");
            sb.AppendLine($"Melee Dmg: {s.meleeDamage}  Range: {s.meleeRange}");
            sb.AppendLine($"Melee CD: {s.meleeCooldown:F2}s");
            sb.AppendLine($"Aggro Range: {s.aggroRange}");
            sb.AppendLine($"Attack Windup: {s.attackWindupSeconds:F2}s");
            sb.AppendLine();
            sb.AppendLine("<b>── AI ──</b>");
            sb.AppendLine($"FSM Set: {def.fsmSet}");
            sb.AppendLine($"Patrol: {def.patrolType}");
            sb.AppendLine($"Telegraph: {def.useAttackTelegraph}");
            sb.AppendLine();
            sb.AppendLine("<b>── Spawn ──</b>");
            sb.AppendLine($"Count: {s.spawnCount}  Padding: {s.spawnPadding}");
            sb.AppendLine($"Margin: {s.spawnMargin}");
            sb.AppendLine($"Faction: {s.faction}");

            if (def.autoCast && def.autoCastList != null)
            {
                sb.AppendLine();
                sb.AppendLine("<b>── Auto Cast ──</b>");
                foreach (var spell in def.autoCastList)
                    sb.AppendLine($"  • {spell}");
            }

            _propsTmp.text = sb.ToString();
            _propsTmp.richText = true;
            _statusTmp.text = $"Selected: {def.displayName ?? key}";
        }

        private void ShowPlayerProperties(string key)
        {
            if (!PlayerClassCatalog.TryGetPreset(key, out var p))
            {
                _propsTmp.text = "Not found."; return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>Key:</b> {p.PlayerKey}");
            sb.AppendLine($"<b>Name:</b> {p.DisplayName}");
            sb.AppendLine();
            sb.AppendLine("<b>── Attributes ──</b>");
            sb.AppendLine($"Str: {p.InitialStrength}/{p.MaxStrength}  Int: {p.InitialIntelligence}/{p.MaxIntelligence}  Dex: {p.InitialDexterity}/{p.MaxDexterity}");
            sb.AppendLine();
            sb.AppendLine("<b>── Combat ──</b>");
            sb.AppendLine($"Attack: {p.BasicAttack}  Armor: {p.BasicArmor}");
            sb.AppendLine($"Speed: {p.BasicSpeed}");
            sb.AppendLine($"Mana Regen: {p.ManaRegenPerSecond}/s");
            _propsTmp.text = sb.ToString();
            _propsTmp.richText = true;
            _statusTmp.text = $"Selected: {p.DisplayName ?? key}";
        }

        // ── Map Interaction ──

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

            if (_mode == EditorMode.Spawn && !string.IsNullOrEmpty(_selectedKey))
            {
                SpawnEntityAtPosition(worldPos);
            }
            else if (_mode == EditorMode.Delete)
            {
                DeleteEntityAtPosition(worldPos);
            }
            else if (_mode == EditorMode.Select)
            {
                SelectEntityAtPosition(worldPos);
            }
        }

        private void SpawnEntityAtPosition(Vector3 worldPos)
        {
            // Stub: integrate with entity spawning system
            _statusTmp.text = $"Spawned {_selectedKey} at ({worldPos.x:F1}, {worldPos.y:F1})";
            Debug.Log($"[EntitiesEditor] Spawn {_selectedKey} at {worldPos}");
        }

        private void DeleteEntityAtPosition(Vector3 worldPos)
        {
            var hit = Physics2D.OverlapCircle(worldPos, 0.5f, LayerMask.GetMask("NPC"));
            if (hit != null)
            {
                _statusTmp.text = $"Deleted {hit.gameObject.name}";
                Debug.Log($"[EntitiesEditor] Deleted {hit.gameObject.name}");
                Destroy(hit.gameObject);
            }
            else
            {
                _statusTmp.text = "No entity under cursor.";
            }
        }

        private void SelectEntityAtPosition(Vector3 worldPos)
        {
            var hit = Physics2D.OverlapCircle(worldPos, 0.5f, LayerMask.GetMask("NPC"));
            if (hit != null)
            {
                var brain = hit.GetComponent<Valkur.Gameplay.FSM.FSMMonsterBrain>();
                if (brain != null)
                {
                    _statusTmp.text = $"Selected: {hit.gameObject.name}";
                }
            }
        }

        private static string TruncateName(string name, int max)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.Length <= max ? name : name.Substring(0, max - 1) + "…";
        }
    }
}