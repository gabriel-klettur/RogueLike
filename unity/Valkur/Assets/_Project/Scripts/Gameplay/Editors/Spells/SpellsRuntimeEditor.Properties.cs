using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

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
        private bool _applyingProperty;
        private bool _assetsTabBuilt;
        private Button _browseSpriteBtn;

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

            // Build the Browse... button once.
            if (!_assetsTabBuilt && _uiRefs.PropsAssetsRoot != null)
            {
                _browseSpriteBtn = EditorUIHelpers.MakeButton(
                    _uiRefs.PropsAssetsRoot, "Browse…", OpenSpriteBrowser, 28f);
                _assetsTabBuilt = true;
            }

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

        // ── Property mutation (reflection + Undo) ──

        private void OnPropertyChanged(string key, object val)
        {
            if (_applyingProperty) return;
            if (string.IsNullOrEmpty(key)) return;
            if (_catalog == null || string.IsNullOrEmpty(_selectedKey))
            {
                Toast("No spell selected.");
                return;
            }
            if (!_catalog.TryGet(_selectedKey, out var s) || s == null)
            {
                Toast("No spell selected.");
                return;
            }

            var fi = typeof(SpellDefinition).GetField(key,
                BindingFlags.Public | BindingFlags.Instance);
            if (fi == null)
            {
                Debug.LogWarning($"[SpellsEditor] Field '{key}' not found on SpellDefinition.");
                return;
            }

            object oldValue = fi.GetValue(s);
            object newValue;
            try { newValue = ConvertValue(val, fi.FieldType); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpellsEditor] Convert failed for '{key}': {ex.Message}");
                return;
            }

            if (object.Equals(oldValue, newValue)) return;

            // Special validation for spellKey rename (uniqueness).
            if (key == "spellKey")
            {
                var newKey = (newValue as string ?? "").Trim();
                if (string.IsNullOrEmpty(newKey))
                {
                    _applyingProperty = true;
                    try { _uiRefs.PropsForm?.SetValue(key, oldValue); }
                    finally { _applyingProperty = false; }
                    UIModal.Message(_canvas.transform, "Invalid key", "Spell key cannot be empty.");
                    return;
                }
                if (!string.Equals(newKey, (string)oldValue, StringComparison.OrdinalIgnoreCase)
                    && _catalog.TryGet(newKey, out var existing) && existing != null && existing != s)
                {
                    _applyingProperty = true;
                    try { _uiRefs.PropsForm?.SetValue(key, oldValue); }
                    finally { _applyingProperty = false; }
                    UIModal.Message(_canvas.transform, "Duplicate key",
                        $"A spell with key '{newKey}' already exists.");
                    return;
                }
                newValue = newKey;
            }

            ApplyFieldChange(s, fi, oldValue, newValue, key);
        }

        private void ApplyFieldChange(SpellDefinition s, FieldInfo fi,
            object oldValue, object newValue, string key)
        {
            // Apply the change immediately.
            _applyingProperty = true;
            try { fi.SetValue(s, newValue); }
            finally { _applyingProperty = false; }

            string labelKey = key;
            // Capture target identity for undo (in case spellKey changed).
            var targetSpell = s;

            _undo.Do(new UndoStack.LambdaCommand(
                $"Edit {labelKey}",
                doAction: () =>
                {
                    _applyingProperty = true;
                    try { fi.SetValue(targetSpell, newValue); }
                    finally { _applyingProperty = false; }
                    if (key == "spellKey") _selectedKey = newValue as string;
                    RefreshAfterMutation();
                },
                undoAction: () =>
                {
                    _applyingProperty = true;
                    try { fi.SetValue(targetSpell, oldValue); }
                    finally { _applyingProperty = false; }
                    if (key == "spellKey") _selectedKey = oldValue as string;
                    RefreshAfterMutation();
                }));

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(s);
#endif

            // Side effects per key.
            if (key == "spellKey")
            {
                _selectedKey = newValue as string;
                RefreshAfterMutation();
            }
            else if (key == "type")
            {
                _catalog.SetSpellsRuntime(_catalog.AllSpells);
                RefreshPropertiesForm();
            }
            else if (key == "displayName")
            {
                _catalog.SetSpellsRuntime(_catalog.AllSpells);
                RefreshActivePicker();
            }
            else
            {
                _catalog.SetSpellsRuntime(_catalog.AllSpells);
            }

            Toast($"Edited {key} = {FormatVal(newValue)}");
        }

        internal void RefreshAfterMutation()
        {
            if (_catalog != null) _catalog.SetSpellsRuntime(_catalog.AllSpells);
            RefreshActivePicker();
            RefreshPropertiesForm();
        }

        private static string FormatVal(object v)
        {
            if (v == null) return "<null>";
            if (v is UnityEngine.Object u) return u.name;
            return v.ToString();
        }

        // ── Type conversion (form widget → SpellDefinition field) ──

        private static object ConvertValue(object val, Type targetType)
        {
            if (targetType.IsEnum)
            {
                if (val is int i) return Enum.ToObject(targetType, i);
                if (val is string s) return Enum.Parse(targetType, s);
                return Enum.ToObject(targetType, System.Convert.ToInt32(val));
            }
            if (targetType == typeof(float))
            {
                if (val is float f) return f;
                if (val is int i) return (float)i;
                if (val is double d) return (float)d;
                if (val is string s) { float.TryParse(s, out var p); return p; }
                return System.Convert.ToSingle(val);
            }
            if (targetType == typeof(int))
            {
                if (val is int i) return i;
                if (val is float f) return Mathf.RoundToInt(f);
                if (val is string s) { int.TryParse(s, out var p); return p; }
                return System.Convert.ToInt32(val);
            }
            if (targetType == typeof(bool))
            {
                if (val is bool b) return b;
                if (val is string s) { bool.TryParse(s, out var p); return p; }
                return System.Convert.ToBoolean(val);
            }
            if (targetType == typeof(string))
            {
                return val?.ToString() ?? string.Empty;
            }
            // Reference types (e.g. Sprite) — assign as-is.
            return val;
        }

        // ── Sprite browser modal ──

        private void OpenSpriteBrowser()
        {
            if (_catalog == null) return;
            if (string.IsNullOrEmpty(_selectedKey))
            {
                Toast("Browse: select a spell first.");
                return;
            }
            if (!_catalog.TryGet(_selectedKey, out var s) || s == null) return;

            var modal = EditorUIHelpers.CreateUI("__SpellSpriteBrowser", _canvas.transform);
            var mr = modal.GetComponent<RectTransform>();
            mr.anchorMin = Vector2.zero; mr.anchorMax = Vector2.one;
            mr.offsetMin = Vector2.zero; mr.offsetMax = Vector2.zero;
            var shade = modal.AddComponent<Image>();
            shade.color = new Color(0f, 0f, 0f, 0.65f);
            shade.raycastTarget = true;

            var card = EditorUIHelpers.MakePanel("Card", modal.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(560f, 480f));
            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f; vlg.padding = new RectOffset(12, 12, 10, 10);
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true; vlg.childControlHeight = true;

            EditorUIHelpers.MakeTitleBar(card.transform, "Select Sprite", 28f);

            var gridHost = EditorUIHelpers.CreateUI("GridHost", card.transform);
            var gle = gridHost.AddComponent<LayoutElement>();
            gle.preferredHeight = 380f; gle.flexibleHeight = 1f; gle.flexibleWidth = 1f;
            var grid = AssetThumbnailGrid.Create(gridHost.transform, "Grid", 5, 72f);

            var entries = new List<AssetThumbnailGrid.Entry>();
            var seen = new HashSet<Sprite>();
            foreach (var sp in _catalog.AllSpells)
            {
                if (sp != null && sp.sprite != null && seen.Add(sp.sprite))
                    entries.Add(new AssetThumbnailGrid.Entry
                    { Id = sp.sprite.name, Label = sp.sprite.name, Thumb = sp.sprite, Data = sp.sprite });
            }
            var loaded = Resources.LoadAll<Sprite>("Spells");
            if (loaded != null)
            {
                foreach (var sp in loaded)
                {
                    if (sp != null && seen.Add(sp))
                        entries.Add(new AssetThumbnailGrid.Entry
                        { Id = sp.name, Label = sp.name, Thumb = sp, Data = sp });
                }
            }
            grid.SetEntries(entries);
            if (s.sprite != null) grid.SelectById(s.sprite.name);

            var rowGo = EditorUIHelpers.CreateUI("Row", card.transform);
            rowGo.AddComponent<LayoutElement>().preferredHeight = 32f;
            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f; hlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeButton(rowGo.transform, "Cancel", () =>
            {
                if (modal != null) Destroy(modal);
            });

            grid.SelectionChanged += entry =>
            {
                var newSprite = entry?.Data as Sprite;
                if (modal != null) Destroy(modal);

                var fi = typeof(SpellDefinition).GetField("sprite",
                    BindingFlags.Public | BindingFlags.Instance);
                if (fi == null) return;
                var oldSprite = s.sprite;
                if (oldSprite == newSprite) return;
                ApplyFieldChange(s, fi, oldSprite, newSprite, "sprite");
                UpdateAssetsTab();
            };
        }

        private static void AddSectionHeader(PropertyForm form, string text)
        {
            // Mirror Buildings/Tiles "BuildSeparator + bold-uppercase label" pattern
            // so spells properties read with the same visual hierarchy as the rest
            // of the runtime editors.
            //
            // Strip the legacy "── X ──" decoration so we can re-render the X with
            // letter-spacing + a thin rule above (matches BuildSeparator in TileEditorUIHelpers).
            string clean = text;
            if (!string.IsNullOrEmpty(clean))
                clean = clean.Replace("─", "").Trim();

            // Top spacer
            var spacer = EditorUIHelpers.CreateUI("SecGap_" + clean, form.transform);
            spacer.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 6f;

            // Thin separator rule
            var sep = EditorUIHelpers.CreateUI("SecSep_" + clean, form.transform);
            sep.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 1f;
            sep.AddComponent<UnityEngine.UI.Image>().color = EditorUIHelpers.SEPARATOR;

            // Bold spaced-out section title (uppercase for hierarchy)
            var go = EditorUIHelpers.CreateUI("SecHdr_" + clean, form.transform);
            go.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 18f;
            var tmp              = go.AddComponent<TMPro.TextMeshProUGUI>();
            tmp.text             = (clean ?? "").ToUpperInvariant();
            tmp.fontSize         = 11f;
            tmp.fontStyle        = TMPro.FontStyles.Bold;
            tmp.alignment        = TMPro.TextAlignmentOptions.Left;
            tmp.color            = EditorUIHelpers.ACCENT;
            tmp.characterSpacing = 4f;
            tmp.margin           = new Vector4(2f, 0f, 0f, 0f);
        }
    }
}