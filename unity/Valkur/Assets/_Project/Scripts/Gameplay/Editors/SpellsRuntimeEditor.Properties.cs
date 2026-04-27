using System;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Editors.EditorKit;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spells Editor — Properties tab. Builds a <see cref="PropertyForm"/> filled
    /// from the selected <see cref="SpellDefinition"/>.
    ///
    /// PHASE 1: edits are NOT persisted. <c>ValueChanged</c> just logs and updates
    /// the status line so the user sees that the change was captured but not
    /// committed. Phase 2 will mutate the SpellDefinition through an Undo command.
    /// </summary>
    public partial class SpellsRuntimeEditor : SingletonMonoBehaviour<SpellsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        private bool _propsFormSubscribed;

        private void RefreshPropertiesForm()
        {
            var form = _uiRefs.PropsForm;
            if (form == null) return;

            // Subscribe to value changes once.
            if (!_propsFormSubscribed)
            {
                form.ValueChanged += OnPropertyChanged;
                _propsFormSubscribed = true;
            }

            form.Clear();

            // Update Assets / Particles tab refs.
            UpdateAssetsTab();

            if (string.IsNullOrEmpty(_selectedKey) || _catalog == null) return;
            if (!_catalog.TryGet(_selectedKey, out var s) || s == null) return;

            // ── Identity ──
            AddSectionHeader(form, "── Identity ──");
            form.AddText("spellKey",     "Spell Key",    s.spellKey ?? "");
            form.AddText("displayName",  "Display Name", s.displayName ?? "");
            form.AddDropdown("type",     "Type",
                Enum.GetNames(typeof(SpellType)),
                (int)s.type);

            // ── Casting ──
            AddSectionHeader(form, "── Casting ──");
            form.AddInt   ("manaCost",      "Mana Cost",      Mathf.RoundToInt(s.manaCost));
            form.AddInt   ("maxInstances",  "Max Instances",  s.maxInstances);
            form.AddBool  ("allowOverlap",  "Allow Overlap",  s.allowOverlap);
            form.AddBool  ("allowMovement", "Allow Movement", s.allowMovement);
            form.AddBool  ("interruptible", "Interruptible",  s.interruptible);
            form.AddBool  ("automatic",     "Automatic",      s.automatic);

            // ── Timings ──
            AddSectionHeader(form, "── Timings ──");
            form.AddFloat("prepareDuration",  "Prepare (s)",  s.prepareDuration);
            form.AddFloat("channelDuration",  "Channel (s)",  s.channelDuration);
            form.AddFloat("cooldownDuration", "Cooldown (s)", s.cooldownDuration);

            // ── Combat ──
            AddSectionHeader(form, "── Combat ──");
            form.AddFloat("damage",    "Damage",    s.damage);
            form.AddFloat("speed",     "Speed",     s.speed);
            form.AddFloat("range",     "Range",     s.range);
            form.AddFloat("lifetime",  "Lifetime",  s.lifetime);
            form.AddFloat("radius",    "Radius",    s.radius);
            form.AddFloat("knockback", "Knockback", s.knockback);

            // ── Type-specific ──
            switch (s.type)
            {
                case SpellType.Dash:
                    AddSectionHeader(form, "── Dash ──");
                    form.AddFloat("distance",        "Distance",         s.distance);
                    form.AddFloat("collisionDamage", "Collision Damage", s.collisionDamage);
                    break;

                case SpellType.Meteor:
                    AddSectionHeader(form, "── Meteor ──");
                    form.AddInt  ("meteorCount",        "Meteor Count",        s.meteorCount);
                    form.AddFloat("meteorInterval",     "Meteor Interval",     s.meteorInterval);
                    form.AddFloat("meteorAreaRadius",   "Area Radius",         s.meteorAreaRadius);
                    form.AddFloat("meteorImpactRadius", "Impact Radius",       s.meteorImpactRadius);
                    break;

                case SpellType.Mine:
                    AddSectionHeader(form, "── Mine ──");
                    form.AddFloat("armingTime",      "Arming Time",      s.armingTime);
                    form.AddFloat("triggerRadius",   "Trigger Radius",   s.triggerRadius);
                    form.AddFloat("explosionRadius", "Explosion Radius", s.explosionRadius);
                    form.AddFloat("explosionDamage", "Explosion Damage", s.explosionDamage);
                    form.AddFloat("ttl",             "TTL (s)",          s.ttl);
                    break;

                case SpellType.Wall:
                    AddSectionHeader(form, "── Wall ──");
                    form.AddFloat("wallWidth",        "Wall Width",        s.wallWidth);
                    form.AddFloat("wallHeight",       "Wall Height",       s.wallHeight);
                    form.AddFloat("wallHP",           "Wall HP",           s.wallHP);
                    form.AddBool ("blockProjectiles", "Block Projectiles", s.blockProjectiles);
                    form.AddBool ("blockUnits",       "Block Units",       s.blockUnits);
                    break;

                case SpellType.Summon:
                    AddSectionHeader(form, "── Summon ──");
                    form.AddText ("summonTemplate", "Summon Template", s.summonTemplate ?? "");
                    form.AddInt  ("summonCount",    "Summon Count",    s.summonCount);
                    form.AddFloat("summonDuration", "Duration (s)",    s.summonDuration);
                    break;
            }

            // ── DoT / Aura ──
            AddSectionHeader(form, "── DoT / Aura ──");
            form.AddFloat("duration",      "Duration (s)",  s.duration);
            form.AddFloat("damagePerTick", "Damage / Tick", s.damagePerTick);
            form.AddFloat("healPerTick",   "Heal / Tick",   s.healPerTick);
            form.AddFloat("tickPeriod",    "Tick Period",   s.tickPeriod);
            form.AddText ("element",       "Element",       s.element ?? "");

            // ── VFX (header only — full editor in Phase 2) ──
            AddSectionHeader(form, "── VFX ──");
            form.AddText("vfxPreset",    "VFX Preset",    s.vfxPreset ?? "");
            form.AddText("impactPreset", "Impact Preset", s.impactPreset ?? "");
        }

        private void UpdateAssetsTab()
        {
            if (_uiRefs.AssetPreviewImage != null)
            {
                _uiRefs.AssetPreviewImage.sprite  = null;
                _uiRefs.AssetPreviewImage.enabled = false;
            }
            if (_uiRefs.AssetNameTmp != null)
                _uiRefs.AssetNameTmp.text = "(no spell selected)";

            if (string.IsNullOrEmpty(_selectedKey) || _catalog == null) return;
            if (!_catalog.TryGet(_selectedKey, out var s) || s == null) return;

            if (_uiRefs.AssetNameTmp != null)
                _uiRefs.AssetNameTmp.text = string.IsNullOrEmpty(s.displayName) ? s.spellKey : s.displayName;

            if (_uiRefs.AssetPreviewImage != null && s.sprite != null)
            {
                _uiRefs.AssetPreviewImage.sprite  = s.sprite;
                _uiRefs.AssetPreviewImage.enabled = true;
            }
        }

        private void OnPropertyChanged(string key, object val)
        {
            // PHASE 1 stub — DO NOT mutate the SpellDefinition.
            Debug.Log($"[SpellsEditor] Edit captured: {key} = {val} (not yet persisted)");
            SetStatus($"Edited {key} (not yet persisted)");
        }

        private static void AddSectionHeader(PropertyForm form, string text)
        {
            var go = EditorUIHelpers.CreateUI("SecHdr_" + text, form.transform);
            go.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 16f;
            var tmp       = go.AddComponent<TMPro.TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = 11f;
            tmp.fontStyle = TMPro.FontStyles.Bold;
            tmp.alignment = TMPro.TextAlignmentOptions.Left;
            tmp.color     = EditorUIHelpers.ACCENT;
        }
    }
}