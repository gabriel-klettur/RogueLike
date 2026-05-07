using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Spells.UI;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spells Editor — picker grid (search + 4-col thumbnail catalog) and
    /// filtered list shared with the Table view.
    /// Phase 1 functionality: select-only. Mutate operations are stubs in
    /// <see cref="SpellsRuntimeEditor"/>.Modes.cs.
    /// </summary>
    public partial class SpellsRuntimeEditor : SingletonMonoBehaviour<SpellsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // ── Filtered list (shared by Grid and Table views) ────────────────────

        /// <summary>
        /// The current filter result — populated by <see cref="ApplySpellFilter"/>.
        /// Both the Grid and the Table view read from this list so the filter
        /// is always applied consistently regardless of which view is active.
        /// </summary>
        private readonly List<SpellDefinition> _filtered = new List<SpellDefinition>();

        /// <summary>
        /// Populate <see cref="_filtered"/> from the catalog, applying
        /// <see cref="_searchFilter"/> (case-insensitive substring on key and displayName).
        /// </summary>
        private void ApplySpellFilter()
        {
            _filtered.Clear();
            if (_catalog == null) return;

            string filter = _searchFilter?.Trim().ToLowerInvariant() ?? "";
            foreach (var key in _catalog.GetAllKeys())
            {
                if (!_catalog.TryGet(key, out var spell) || spell == null) continue;
                if (filter.Length > 0)
                {
                    string name = (spell.displayName ?? key).ToLowerInvariant();
                    if (!name.Contains(filter) && !key.ToLowerInvariant().Contains(filter))
                        continue;
                }
                _filtered.Add(spell);
            }
        }

        /// <summary>
        /// Dispatch to the active view (Grid or Table). Call this from any
        /// code path that wants to refresh the picker without caring about
        /// which view is currently shown — search, add/remove, activate.
        /// </summary>
        private void RefreshActivePicker()
        {
            ApplySpellFilter();
            RefreshPicker();
            RefreshTable();
        }

        // ── Grid view ─────────────────────────────────────────────────────────

        private void RefreshPicker()
        {
            var content = _uiRefs.PickerContent;
            if (content == null) return;
            if (_catalog == null)
            {
                SetStatus("(no SpellCatalog assigned)");
                return;
            }

            // Recompute filter so _filtered is always up-to-date before building
            // slots. (RefreshActivePicker calls ApplySpellFilter first, but direct
            // callers like SelectSpell skip that so we re-apply here as a safety net.)
            ApplySpellFilter();

            // Clear existing slots.
            for (int i = content.childCount - 1; i >= 0; i--)
                Valkur.Core.SafeDestroy.Of(content.GetChild(i).gameObject);

            int shown = 0;
            foreach (var spell in _filtered)
            {
                var key = spell.spellKey;
                if (string.IsNullOrEmpty(key)) continue;

                var capturedKey = key;
                var (btn, icon, label) = EditorUIHelpers.MakeSlotButton(
                    content, spell.displayName ?? key, 64f,
                    () => SelectSpell(capturedKey));

                Color preview = ResolvePreviewColor(spell);
                Color secondary = ResolveSecondaryColor(spell, preview);
                if (spell.sprite != null)
                {
                    icon.sprite  = spell.sprite;
                    icon.color   = Color.white;
                    icon.enabled = true;
                }
                else
                {
                    // No sprite — paint a procedural "particle preview" using the
                    // preset kind / spell type. Mirrors Python's ParticlePreviewManager
                    // intent (live preview per spell) but rendered statically.
                    icon.enabled = false;
                    string kind = ResolvePreviewKind(spell);
                    AddProceduralPreview(icon.transform.parent, kind, preview, secondary);
                }
                label.text = TruncateName(spell.displayName ?? key, 9);

                // Faint preview-tint on the slot bg so the catalog reads as a colour-coded grid.
                var bgImg = btn.GetComponent<Image>();
                if (bgImg != null)
                {
                    if (key == _selectedKey)
                    {
                        bgImg.color = EditorUIHelpers.SLOT_SELECTED;
                    }
                    else
                    {
                        var tint = preview; tint.a = 0.18f;
                        bgImg.color = tint;
                    }
                }

                // Add drag-drop support: make this spell draggable to the HUD
                var draggable = btn.gameObject.AddComponent<DraggableSpellItem>();
                draggable.Configure(spell, icon, SpellDragOrigin.Picker);

                shown++;
            }

            string filterTrim = (_searchFilter ?? "").Trim();
            SetStatus(filterTrim.Length == 0
                ? $"{shown} spells"
                : $"{shown} match '{_searchFilter}'");
        }

        private void SelectSpell(string key)
        {
            _selectedKey = key;
            RefreshPicker();
            RefreshPropertiesForm();
            // Live-preview: if the View panel is open, update the looped cast.
            NotifyPreviewSelectionChanged();
        }

        private static string TruncateName(string name, int max)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.Length <= max ? name : name.Substring(0, max - 1) + "…";
        }

        // ── Preview helpers ────────────────────────────────────────────────

        /// <summary>
        /// Pick a representative colour for a spell, in priority order:
        ///   1. ParticlePreset.colors[0] / .color  (when vfxPreset resolves)
        ///   2. SpellDefinition.particleColor      (when explicitly set ≠ white)
        ///   3. Per-SpellType fallback             (default rainbow by category)
        /// </summary>
        private Color ResolvePreviewColor(SpellDefinition s)
        {
            if (s == null) return TypeColor(SpellType.Projectile);

            if (!string.IsNullOrEmpty(s.vfxPreset) && _particleCatalog != null)
            {
                var preset = _particleCatalog.GetById(s.vfxPreset);
                if (preset != null && preset.vfx != null)
                {
                    if (preset.vfx.colors != null && preset.vfx.colors.Length > 0)
                    {
                        var c = preset.vfx.colors[0]; c.a = 1f; return c;
                    }
                    var pc = preset.vfx.color; pc.a = 1f; return pc;
                }
            }

            // particleColor: only honour if the user set something other than the default white.
            var p = s.particleColor;
            if (p.r + p.g + p.b < 2.95f) { p.a = 1f; return p; }

            return TypeColor(s.type);
        }

        private static Color TypeColor(SpellType t)
        {
            switch (t)
            {
                case SpellType.Projectile:        return new Color(0.95f, 0.55f, 0.20f); // orange (fireball-like)
                case SpellType.Slash:             return new Color(0.85f, 0.85f, 0.90f); // steel
                case SpellType.Area:              return new Color(0.85f, 0.30f, 0.30f); // red
                case SpellType.Dash:              return new Color(0.30f, 0.85f, 0.95f); // cyan
                case SpellType.Teleport:          return new Color(0.55f, 0.30f, 0.85f); // violet
                case SpellType.Beam:              return new Color(0.95f, 0.85f, 0.25f); // gold
                case SpellType.Smoke:             return new Color(0.45f, 0.45f, 0.45f); // grey
                case SpellType.Wall:              return new Color(0.55f, 0.40f, 0.20f); // brown
                case SpellType.Trap:              return new Color(0.65f, 0.55f, 0.25f); // ochre
                case SpellType.Shield:            return new Color(0.30f, 0.60f, 0.95f); // azure
                case SpellType.Boomerang:         return new Color(0.80f, 0.65f, 0.30f); // tan
                case SpellType.Meteor:            return new Color(0.95f, 0.40f, 0.10f); // ember
                case SpellType.Lightning:         return new Color(0.85f, 0.90f, 1.00f); // pale blue
                case SpellType.ChainLightning:    return new Color(0.55f, 0.75f, 1.00f); // electric
                case SpellType.Aura:              return new Color(0.50f, 0.95f, 0.55f); // green
                case SpellType.ArcaneFlame:       return new Color(0.70f, 0.40f, 0.95f); // arcane purple
                case SpellType.FireworkLaunch:    return new Color(1.00f, 0.55f, 0.85f); // pink
                case SpellType.SmokeEmitter:      return new Color(0.55f, 0.55f, 0.60f);
                case SpellType.SphereMagicShield: return new Color(0.40f, 0.85f, 0.95f);
                case SpellType.Puddle:            return new Color(0.30f, 0.55f, 0.85f); // water
                case SpellType.Mine:              return new Color(0.75f, 0.30f, 0.20f); // dark red
                case SpellType.VortexField:       return new Color(0.30f, 0.20f, 0.55f); // dark vortex
                case SpellType.ConeBreath:        return new Color(0.95f, 0.60f, 0.30f);
                case SpellType.Summon:            return new Color(0.65f, 0.40f, 0.85f);
                case SpellType.Totem:             return new Color(0.45f, 0.85f, 0.65f);
                default:                          return new Color(0.65f, 0.65f, 0.65f);
            }
        }

        private static string TypeAbbr(SpellType t)
        {
            switch (t)
            {
                case SpellType.Projectile:        return "PROJ";
                case SpellType.Slash:             return "SLASH";
                case SpellType.Area:              return "AOE";
                case SpellType.Dash:              return "DASH";
                case SpellType.Teleport:          return "TP";
                case SpellType.Beam:              return "BEAM";
                case SpellType.Smoke:             return "SMK";
                case SpellType.Wall:              return "WALL";
                case SpellType.Trap:              return "TRAP";
                case SpellType.Shield:            return "SHLD";
                case SpellType.Boomerang:         return "BMRG";
                case SpellType.Meteor:            return "MTR";
                case SpellType.Lightning:         return "LTNG";
                case SpellType.ChainLightning:    return "CHN";
                case SpellType.Aura:              return "AURA";
                case SpellType.ArcaneFlame:       return "ARCN";
                case SpellType.FireworkLaunch:    return "FWK";
                case SpellType.SmokeEmitter:      return "SMKE";
                case SpellType.SphereMagicShield: return "ORB";
                case SpellType.Puddle:            return "PDL";
                case SpellType.Mine:              return "MINE";
                case SpellType.VortexField:       return "VRTX";
                case SpellType.ConeBreath:        return "CONE";
                case SpellType.Summon:            return "SMN";
                case SpellType.Totem:             return "TOTM";
                default:                          return "";
            }
        }

        private static void AddTypeGlyph(Transform iconParent, SpellType type)
        {
            var go = new GameObject("TypeGlyph", typeof(RectTransform));
            go.transform.SetParent(iconParent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text          = TypeAbbr(type);
            tmp.alignment     = TextAlignmentOptions.Center;
            tmp.fontSize      = 11f;
            tmp.fontStyle     = FontStyles.Bold;
            tmp.color         = new Color(0f, 0f, 0f, 0.85f);
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
        }

        // ── Procedural particle preview ────────────────────────────────────

        private Color ResolveSecondaryColor(SpellDefinition s, Color fallback)
        {
            if (s != null && !string.IsNullOrEmpty(s.vfxPreset) && _particleCatalog != null)
            {
                var preset = _particleCatalog.GetById(s.vfxPreset);
                if (preset != null && preset.vfx != null && preset.vfx.colors != null && preset.vfx.colors.Length > 1)
                {
                    var c = preset.vfx.colors[1]; c.a = 1f; return c;
                }
            }
            return fallback;
        }

        /// <summary>
        /// Map a spell to a draw-kind for SpellPreviewGraphic. Prefers the
        /// resolved ParticlePresetDefinition.vfx.kind; falls back to a string
        /// derived from SpellType.
        /// </summary>
        private string ResolvePreviewKind(SpellDefinition s)
        {
            if (s != null && !string.IsNullOrEmpty(s.vfxPreset) && _particleCatalog != null)
            {
                var preset = _particleCatalog.GetById(s.vfxPreset);
                if (preset != null && preset.vfx != null && !string.IsNullOrEmpty(preset.vfx.kind))
                    return preset.vfx.kind;
            }
            if (s == null) return "explosion";
            switch (s.type)
            {
                case SpellType.Projectile:        return "projectile";
                case SpellType.Slash:             return "slash";
                case SpellType.Area:              return "explosion";
                case SpellType.Dash:              return "dash";
                case SpellType.Teleport:          return "teleport";
                case SpellType.Beam:              return "beam";
                case SpellType.Smoke:             return "smoke";
                case SpellType.Wall:              return "wall";
                case SpellType.Trap:              return "trap";
                case SpellType.Shield:            return "shield";
                case SpellType.Boomerang:         return "boomerang";
                case SpellType.Meteor:            return "meteor";
                case SpellType.Lightning:         return "lightning";
                case SpellType.ChainLightning:    return "chain_lightning";
                case SpellType.Aura:              return "aura";
                case SpellType.ArcaneFlame:       return "arcane_flame";
                case SpellType.FireworkLaunch:    return "firework_launch";
                case SpellType.SmokeEmitter:      return "smoke_emitter";
                case SpellType.SphereMagicShield: return "sphere_magic_shield";
                case SpellType.Puddle:            return "puddle";
                case SpellType.Mine:              return "mine";
                case SpellType.VortexField:       return "vortex_field";
                case SpellType.ConeBreath:        return "cone_breath";
                case SpellType.Summon:            return "summon";
                case SpellType.Totem:             return "totem";
                default:                          return "explosion";
            }
        }

        /// <summary>
        /// Adds a stretched <see cref="SpellPreviewGraphic"/> child to the slot,
        /// occupying the same area as the (now disabled) icon Image.
        /// </summary>
        private static void AddProceduralPreview(Transform slotRoot, string kind, Color primary, Color secondary)
        {
            var go = new GameObject("Preview", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(slotRoot, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.10f, 0.20f);
            rt.anchorMax = new Vector2(0.90f, 0.90f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var gfx = go.AddComponent<SpellPreviewGraphic>();
            gfx.raycastTarget = false;
            gfx.Configure(kind, primary, secondary);
        }
    }
}
