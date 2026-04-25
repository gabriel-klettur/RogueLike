using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Editors.EditorKit;

namespace Valkur.Gameplay.Spells
{
    public partial class SpellsRuntimeEditor : SingletonMonoBehaviour<SpellsRuntimeEditor>, GameEditorManager.IGameEditor
    {

        private void RefreshPicker()
        {
            if (_catalog == null) return;

            // Clear existing
            for (int i = _pickerContent.childCount - 1; i >= 0; i--)
                Destroy(_pickerContent.GetChild(i).gameObject);

            var keys = _catalog.GetAllKeys();
            int shown = 0;
            string filter = _searchFilter?.Trim().ToLowerInvariant() ?? "";
            foreach (var key in keys)
            {
                if (!_catalog.TryGet(key, out var spell)) continue;
                if (filter.Length > 0)
                {
                    string name = (spell.displayName ?? key).ToLowerInvariant();
                    if (!name.Contains(filter) && !key.ToLowerInvariant().Contains(filter))
                        continue;
                }
                var capturedKey = key;
                var (btn, icon, label) = EditorUIHelpers.MakeSlotButton(
                    _pickerContent, spell.displayName ?? key, 72f,
                    () => SelectSpell(capturedKey));

                if (spell.sprite != null)
                {
                    icon.sprite = spell.sprite;
                    icon.enabled = true;
                }
                label.text = TruncateName(spell.displayName ?? key, 9);

                if (key == _selectedKey)
                    btn.GetComponent<Image>().color = EditorUIHelpers.SLOT_SELECTED;
                shown++;
            }
            if (_statusTmp != null)
            {
                _statusTmp.text = filter.Length == 0
                    ? $"{shown} spells"
                    : $"{shown} match '{_searchFilter}'";
            }
        }

        private void SelectSpell(string key)
        {
            _selectedKey = key;
            RefreshPicker();
            RefreshProperties();
        }

        private void RefreshProperties()
        {
            if (string.IsNullOrEmpty(_selectedKey) || _catalog == null)
            {
                _propsTmp.text = "Select a spell to view properties.";
                return;
            }

            if (!_catalog.TryGet(_selectedKey, out var s))
            {
                _propsTmp.text = $"Spell '{_selectedKey}' not found.";
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>Key:</b> {s.spellKey}");
            sb.AppendLine($"<b>Name:</b> {s.displayName}");
            sb.AppendLine($"<b>Type:</b> {s.type}");
            sb.AppendLine();
            sb.AppendLine("<b>── Casting ──</b>");
            sb.AppendLine($"Mana Cost: {s.manaCost}");
            sb.AppendLine($"Max Instances: {s.maxInstances}");
            sb.AppendLine($"Allow Movement: {s.allowMovement}");
            sb.AppendLine($"Interruptible: {s.interruptible}");
            sb.AppendLine($"Automatic: {s.automatic}");
            sb.AppendLine();
            sb.AppendLine("<b>── Timings ──</b>");
            sb.AppendLine($"Prepare: {s.prepareDuration:F2}s");
            sb.AppendLine($"Channel: {s.channelDuration:F2}s");
            sb.AppendLine($"Cooldown: {s.cooldownDuration:F2}s");
            sb.AppendLine();
            sb.AppendLine("<b>── Combat ──</b>");
            sb.AppendLine($"Damage: {s.damage}");
            sb.AppendLine($"Speed: {s.speed}");
            sb.AppendLine($"Range: {s.range}");
            sb.AppendLine($"Lifetime: {s.lifetime:F2}s");
            sb.AppendLine($"Radius: {s.radius}");
            sb.AppendLine($"Knockback: {s.knockback}");
            sb.AppendLine();

            if (s.type == SpellType.Dash)
            {
                sb.AppendLine("<b>── Dash ──</b>");
                sb.AppendLine($"Distance: {s.distance}");
                sb.AppendLine($"Collision Damage: {s.collisionDamage}");
            }
            else if (s.type == SpellType.Meteor)
            {
                sb.AppendLine("<b>── Meteor ──</b>");
                sb.AppendLine($"Count: {s.meteorCount}");
                sb.AppendLine($"Interval: {s.meteorInterval:F2}s");
                sb.AppendLine($"Area Radius: {s.meteorAreaRadius}");
                sb.AppendLine($"Impact Radius: {s.meteorImpactRadius}");
            }
            else if (s.type == SpellType.Mine)
            {
                sb.AppendLine("<b>── Mine ──</b>");
                sb.AppendLine($"Arming Time: {s.armingTime:F2}s");
                sb.AppendLine($"Trigger Radius: {s.triggerRadius}");
                sb.AppendLine($"Explosion Radius: {s.explosionRadius}");
                sb.AppendLine($"Explosion Damage: {s.explosionDamage}");
                sb.AppendLine($"TTL: {s.ttl:F1}s");
            }
            else if (s.type == SpellType.Wall)
            {
                sb.AppendLine("<b>── Wall ──</b>");
                sb.AppendLine($"Width: {s.wallWidth}  Height: {s.wallHeight}");
                sb.AppendLine($"HP: {s.wallHP}");
                sb.AppendLine($"Block Projectiles: {s.blockProjectiles}");
                sb.AppendLine($"Block Units: {s.blockUnits}");
            }
            else if (s.type == SpellType.Summon)
            {
                sb.AppendLine("<b>── Summon ──</b>");
                sb.AppendLine($"Template: {s.summonTemplate}");
                sb.AppendLine($"Count: {s.summonCount}");
                sb.AppendLine($"Duration: {s.summonDuration:F1}s");
            }

            if (s.duration > 0)
            {
                sb.AppendLine();
                sb.AppendLine("<b>── DoT/Aura ──</b>");
                sb.AppendLine($"Duration: {s.duration:F2}s");
                sb.AppendLine($"Dmg/Tick: {s.damagePerTick}");
                sb.AppendLine($"Heal/Tick: {s.healPerTick}");
                sb.AppendLine($"Tick Period: {s.tickPeriod:F2}s");
                sb.AppendLine($"Element: {s.element}");
            }

            if (!string.IsNullOrEmpty(s.vfxPreset))
            {
                sb.AppendLine();
                sb.AppendLine("<b>── VFX ──</b>");
                sb.AppendLine($"Preset: {s.vfxPreset}");
                sb.AppendLine($"Impact: {s.impactPreset}");
            }

            _propsTmp.text = sb.ToString();
            _propsTmp.richText = true;
            _statusTmp.text = $"Selected: {s.displayName ?? s.spellKey}";
        }

        private static string TruncateName(string name, int max)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.Length <= max ? name : name.Substring(0, max - 1) + "…";
        }
    }
}