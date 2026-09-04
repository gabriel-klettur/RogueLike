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

            // The Gather tab reads the same selection and the same catalog, so it refreshes
            // on the same beat. Kept as one call rather than a second subscription because a
            // selection change that repainted one tab and not the other would leave the panel
            // describing two different spells.
            RefreshGatherForm();

            if (string.IsNullOrEmpty(_selectedKey) || _catalog == null) return;
            if (!_catalog.TryGet(_selectedKey, out var s) || s == null) return;

            // ── Identity ──
            AddSectionHeader(form, "── Identity ──");
            form.AddText("spellKey",     "Spell Key",    s.spellKey ?? "");
            form.AddText("displayName",  "Display Name", s.displayName ?? "");
            form.AddDropdown("type",     "Type",
                Enum.GetNames(typeof(SpellType)),
                (int)s.type);
            form.AddDropdown("audience", "Audience", new[]
            {
                "Unassigned",
                "Player",
                "NPC",
                "Player + NPC",
                "Boss",
                "Player + Boss",
                "NPC + Boss",
                "Player + NPC + Boss",
            }, (int)s.audience);

            // ── Casting ──
            AddSectionHeader(form, "── Casting ──");
            form.AddInt   ("manaCost",      "Mana Cost",      Mathf.RoundToInt(s.manaCost));
            form.AddInt   ("maxInstances",  "Max Instances",  s.maxInstances);
            form.AddBool  ("allowOverlap",  "Allow Overlap",  s.allowOverlap);
            form.AddBool  ("allowMovement", "Allow Movement", s.allowMovement);
            form.AddBool  ("interruptible", "Interruptible",  s.interruptible);
            form.AddBool  ("automatic",     "Automatic",      s.automatic);
            form.AddFloat ("automaticCastPunish", "Auto-cast Punish", s.automaticCastPunish);
            form.AddBool  ("lockCastDirection",   "Lock Direction",   s.lockCastDirection);

            // ── Telegraph ──
            // Drawn by the caster rather than by any executor, so it applies to every spell.
            AddSectionHeader(form, "── Telegraph ──");
            form.AddColor("telegraphColor", "Telegraph Color", s.telegraphColor);
            form.AddFloat("telegraphAlpha", "Telegraph Alpha", s.telegraphAlpha);

            // ── Cast Origin ──
            // Where the effect is born on the caster's body, and how far in front.
            // The anchor is a fraction of the caster's height, so one setting reads
            // the same on a rat and on a boss.
            AddSectionHeader(form, "── Cast Origin ──");
            form.AddDropdown("castAnchor", "Anchor",
                Enum.GetNames(typeof(SpellCastAnchor)),
                (int)s.castAnchor);
            form.AddFloat("castForwardOffset", "Forward Offset", s.castForwardOffset);

            // ── Timings ──
            AddSection(form, s, "── Timings ──",
                ("prepareDuration",  () => form.AddFloat("prepareDuration",  "Prepare (s)",  s.prepareDuration)),
                ("channelDuration",  () => form.AddFloat("channelDuration",  "Channel (s)",  s.channelDuration)),
                ("cooldownDuration", () => form.AddFloat("cooldownDuration", "Cooldown (s)", s.cooldownDuration)));

            // ── Combat ──
            // radius and hitRadius are two different authored shapes and most spells read
            // only one of them, so the filter is what keeps a designer from tuning the
            // dead one and concluding the spell ignores its own numbers.
            AddSection(form, s, "── Combat ──",
                ("damage",           () => form.AddFloat("damage",           "Damage",           s.damage)),
                ("speed",            () => form.AddFloat("speed",            "Speed",            s.speed)),
                ("range",            () => form.AddFloat("range",            "Range",            s.range)),
                ("lifetime",         () => form.AddFloat("lifetime",         "Lifetime",         s.lifetime)),
                ("radius",           () => form.AddFloat("radius",           "Radius",           s.radius)),
                ("hitRadius",        () => form.AddFloat("hitRadius",        "Hit Radius",       s.hitRadius)),
                ("arcRangeDegrees",  () => form.AddFloat("arcRangeDegrees",  "Arc (deg)",        s.arcRangeDegrees)),
                ("knockback",        () => form.AddFloat("knockback",        "Knockback",        s.knockback)),
                ("collisionDamage",  () => form.AddFloat("collisionDamage",  "Collision Damage", s.collisionDamage)),
                ("distance",         () => form.AddFloat("distance",         "Distance",         s.distance)));

            // ── Type-specific ──
            AddSection(form, s, "── Meteor ──",
                ("meteorCount",        () => form.AddInt  ("meteorCount",        "Meteor Count",  s.meteorCount)),
                ("meteorInterval",     () => form.AddFloat("meteorInterval",     "Interval",      s.meteorInterval)),
                ("meteorAreaRadius",   () => form.AddFloat("meteorAreaRadius",   "Area Radius",   s.meteorAreaRadius)),
                ("meteorImpactRadius", () => form.AddFloat("meteorImpactRadius", "Impact Radius", s.meteorImpactRadius)));

            AddSection(form, s, "── Mine ──",
                ("armingTime",      () => form.AddFloat("armingTime",      "Arming Time",      s.armingTime)),
                ("triggerRadius",   () => form.AddFloat("triggerRadius",   "Trigger Radius",   s.triggerRadius)),
                ("explosionRadius", () => form.AddFloat("explosionRadius", "Explosion Radius", s.explosionRadius)),
                ("explosionDamage", () => form.AddFloat("explosionDamage", "Explosion Damage", s.explosionDamage)),
                ("ttl",             () => form.AddFloat("ttl",             "TTL (s)",          s.ttl)));

            AddSection(form, s, "── Wall ──",
                ("wallWidth",        () => form.AddFloat("wallWidth",        "Wall Width",        s.wallWidth)),
                ("wallHeight",       () => form.AddFloat("wallHeight",       "Wall Height",       s.wallHeight)),
                ("wallHP",           () => form.AddFloat("wallHP",           "Wall HP",           s.wallHP)),
                ("blockProjectiles", () => form.AddBool ("blockProjectiles", "Block Projectiles", s.blockProjectiles)),
                ("blockUnits",       () => form.AddBool ("blockUnits",       "Block Units",       s.blockUnits)));

            AddSection(form, s, "── Summon ──",
                ("summonTemplate", () => form.AddText ("summonTemplate", "Summon Template", s.summonTemplate ?? "")),
                ("summonCount",    () => form.AddInt  ("summonCount",    "Summon Count",    s.summonCount)),
                ("summonDuration", () => form.AddFloat("summonDuration", "Duration (s)",    s.summonDuration)));

            AddSection(form, s, "── Cone ──",
                ("coneArc",    () => form.AddFloat("coneArc",    "Cone Arc (deg)", s.coneArc)),
                ("coneLength", () => form.AddFloat("coneLength", "Cone Length",    s.coneLength)));

            AddSection(form, s, "── Force ──",
                ("force",        () => form.AddFloat("force",        "Force",         s.force)),
                ("forceMode",    () => form.AddText ("forceMode",    "Force Mode",    s.forceMode ?? "")),
                ("followCaster", () => form.AddBool ("followCaster", "Follow Caster", s.followCaster)),
                ("totemKind",    () => form.AddText ("totemKind",    "Totem Kind",    s.totemKind ?? "")));

            // ── DoT / Aura ──
            AddSection(form, s, "── DoT / Aura ──",
                ("duration",      () => form.AddFloat("duration",      "Duration (s)",  s.duration)),
                ("infinite",      () => form.AddBool ("infinite",      "Never Expires", s.infinite)),
                ("damagePerTick", () => form.AddFloat("damagePerTick", "Damage / Tick", s.damagePerTick)),
                ("healPerTick",   () => form.AddFloat("healPerTick",   "Heal / Tick",   s.healPerTick)),
                ("tickPeriod",    () => form.AddFloat("tickPeriod",    "Tick Period",   s.tickPeriod)),
                ("element",       () => form.AddText ("element",       "Element",       s.element ?? "")));

            // ── Animation ──
            // Which animation the caster plays, and which loadout the spell swaps to. All
            // three are narrow — one spell type each — so AddSection hides them everywhere
            // else rather than showing three inert rows on every spell in the game.
            AddSection(form, s, "── Animation ──",
                ("animState",      () => form.AddText("animState",      "Anim State",      s.animState ?? "")),
                ("loadoutKey",     () => form.AddText("loadoutKey",     "Loadout Key",     s.loadoutKey ?? "")),
                ("loadoutAnimKey", () => form.AddText("loadoutAnimKey", "Loadout Anim",    s.loadoutAnimKey ?? "")));

            // ── Placement / VFX ──
            // particleColor is the spell's own swatch. It reaches further than its name
            // suggests: SpellCastFlourishFX retints the whole element palette through it, so
            // it decides what colour the CAST looks, not just the trail. It was relevant for
            // 24 of the 28 types and had no row at all — the half of that fix that widened
            // SpellFieldRelevance landed, the half that shows a control did not.
            AddSection(form, s, "── VFX ──",
                ("spawnAtMouse",  () => form.AddBool ("spawnAtMouse",  "Spawn At Mouse", s.spawnAtMouse)),
                ("particleColor", () => form.AddColor("particleColor", "Particle Color", s.particleColor)),
                ("scale",         () => form.AddFloat("scale",         "Sprite Scale",   s.scale)),
                ("vfxPreset",     () => form.AddText ("vfxPreset",     "VFX Preset",     s.vfxPreset ?? "")),
                ("impactPreset",  () => form.AddText ("impactPreset",  "Impact Preset",  s.impactPreset ?? "")));

            // ── Projectile mechanics ──
            // Three independent behaviours on the same executor rather than three new spell
            // types, which is what lets any of the twelve existing projectiles be given one
            // later without new code. AddSection hides the whole block from every other type.
            AddSection(form, s, "── Projectile Mechanics ──",
                ("pierceCount",         () => form.AddInt  ("pierceCount",         "Pierce Count",      s.pierceCount)),
                ("pierceDamageFalloff", () => form.AddFloat("pierceDamageFalloff", "Pierce Falloff",    s.pierceDamageFalloff)),
                ("homingStrength",      () => form.AddFloat("homingStrength",      "Homing (deg/s)",    s.homingStrength)),
                ("homingRange",         () => form.AddFloat("homingRange",         "Homing Range",      s.homingRange)),
                ("projectileCount",     () => form.AddInt  ("projectileCount",     "Shots Per Cast",    s.projectileCount)),
                ("spreadDegrees",       () => form.AddFloat("spreadDegrees",       "Spread (deg)",      s.spreadDegrees)));

            // ── Charge ──
            // chargeMaxSeconds is the discriminator: 0 means the spell is not chargeable and
            // fires the instant it is cast, which is how every other spell behaves.
            AddSection(form, s, "── Charge ──",
                ("chargeMaxSeconds",       () => form.AddFloat("chargeMaxSeconds",       "Full Charge (s)",  s.chargeMaxSeconds)),
                ("chargeMinFraction",      () => form.AddFloat("chargeMinFraction",      "Snap Fraction",    s.chargeMinFraction)),
                ("chargeDamageMultiplier", () => form.AddFloat("chargeDamageMultiplier", "Damage x at Full", s.chargeDamageMultiplier)),
                ("chargeScaleMultiplier",  () => form.AddFloat("chargeScaleMultiplier",  "Size x at Full",   s.chargeScaleMultiplier)));

            // ── Buff ──
            // The array itself is a block of its own below; this is the refresh key beside it.
            AddSection(form, s, "── Buff ──",
                ("buffKey", () => form.AddText("buffKey", "Buff Key (refresh)", s.buffKey ?? "")));

            AddStatModifierRows(form, s);
            AddStatusApplicationRows(form, s);
        }

        /// <summary>
        /// Emits a section, skipping every row whose field does nothing for this spell,
        /// and skipping the header too when that leaves the section empty. A header over
        /// no rows reads as a broken panel.
        /// </summary>
        private void AddSection(PropertyForm form, SpellDefinition spell, string header,
                                params (string field, System.Action emit)[] rows)
        {
            bool headerWritten = false;
            for (int i = 0; i < rows.Length; i++)
            {
                if (!SpellFieldRelevance.Applies(spell, rows[i].field)) continue;
                if (!headerWritten) { AddSectionHeader(form, header); headerWritten = true; }
                rows[i].emit();
            }
        }

        private void UpdateAssetsTab()
        {
            if (_uiRefs.AssetPreviewImage != null)
            {
                _uiRefs.AssetPreviewImage.sprite  = null;
                _uiRefs.AssetPreviewImage.enabled = false;
                EditorUIHelpers.HideIconBackdrop(_uiRefs.AssetPreviewImage);
            }
            if (_uiRefs.AssetNameTmp != null)
                _uiRefs.AssetNameTmp.text = "(no spell selected)";

            // Build the Browse... button once.
            if (!_assetsTabBuilt && _uiRefs.PropsAssetsRoot != null)
            {
                // Two targets, one browser. iconSprite drives the spell bar, the
                // drag-preview and the picker; it was previewed here and assignable
                // nowhere, so the only way to change a spell's HUD icon was the Inspector.
                _browseSpriteBtn = EditorUIHelpers.MakeButton(
                    _uiRefs.PropsAssetsRoot, "Browse world sprite…",
                    () => OpenSpriteBrowser("sprite"), 28f);
                EditorUIHelpers.MakeButton(
                    _uiRefs.PropsAssetsRoot, "Browse HUD icon…",
                    () => OpenSpriteBrowser("iconSprite"), 28f);
                _assetsTabBuilt = true;
            }

            if (string.IsNullOrEmpty(_selectedKey) || _catalog == null) return;
            if (!_catalog.TryGet(_selectedKey, out var s) || s == null) return;

            if (_uiRefs.AssetNameTmp != null)
                _uiRefs.AssetNameTmp.text = string.IsNullOrEmpty(s.displayName) ? s.spellKey : s.displayName;

            // Prefer the HUD icon (transparent PNG) over the legacy in-world
            // sprite. With a transparent PNG selected, paint a solid black
            // backdrop behind the preview so it reads against the panel.
            Sprite previewSprite = s.iconSprite != null ? s.iconSprite : s.sprite;
            if (_uiRefs.AssetPreviewImage != null && previewSprite != null)
            {
                _uiRefs.AssetPreviewImage.sprite  = previewSprite;
                _uiRefs.AssetPreviewImage.enabled = true;
                EditorUIHelpers.EnsureIconBackdrop(_uiRefs.AssetPreviewImage);
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

            // The status block addresses array ELEMENTS, so its keys are not field names and
            // must be taken before the reflection lookup below rejects them.
            if (IsStatusKey(key)) { OnStatusValueChanged(s, key, val); return; }
            // Same reason as the line above: these keys address array ELEMENTS, so they are
            // not field names and the reflection lookup below would reject them.
            if (IsStatModKey(key)) { OnStatModValueChanged(s, key, val); return; }

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
            else if (key == "audience")
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
            // PropertyForm.AddColor emits the normalised "#RRGGBBAA" string, so without this
            // branch a colour row falls through to the reference-type case below, hands a
            // string to a Color field and throws — caught upstream and reduced to a warning,
            // which reads as a control that is there, accepts an edit and writes nothing.
            if (targetType == typeof(Color))
            {
                if (val is Color c) return c;
                if (val is string s)
                {
                    string hex = s.StartsWith("#", StringComparison.Ordinal) ? s : "#" + s;
                    if (ColorUtility.TryParseHtmlString(hex, out var parsed)) return parsed;
                    throw new FormatException($"'{s}' is not a colour.");
                }
            }
            // Reference types (e.g. Sprite) — assign as-is.
            return val;
        }

        // ── Sprite browser modal ──

        private void OpenSpriteBrowser(string fieldName)
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
            var current = fieldName == "iconSprite" ? s.iconSprite : s.sprite;
            if (current != null) grid.SelectById(current.name);

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

                var fi = typeof(SpellDefinition).GetField(fieldName,
                    BindingFlags.Public | BindingFlags.Instance);
                if (fi == null) return;
                var oldSprite = fi.GetValue(s) as Sprite;
                if (oldSprite == newSprite) return;
                ApplyFieldChange(s, fi, oldSprite, newSprite, fieldName);
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
