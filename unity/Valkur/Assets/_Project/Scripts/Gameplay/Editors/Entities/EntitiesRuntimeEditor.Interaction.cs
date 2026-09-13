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
    /// rendering, map click-handling. Spawn and delete route through the real placement path
    /// (<c>EntitiesRuntimeEditor.PickerDrag.cs</c>); persistence lives in
    /// <c>EntitiesRuntimeEditor.Persistence.cs</c>. Every mutation still emits a status message
    /// through <see cref="SetStatus"/> so the workflow reads clearly either way.
    /// </summary>
    public partial class EntitiesRuntimeEditor : SingletonMonoBehaviour<EntitiesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // â”€â”€ Category & mode highlighting â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
            Apply(_ui.AllTabImg,      _ui.AllTabTmp,      _category == EntityCategory.All);
            Apply(_ui.HostilesTabImg, _ui.HostilesTabTmp, _category == EntityCategory.Hostiles);
            Apply(_ui.NeutralsTabImg, _ui.NeutralsTabTmp, _category == EntityCategory.Neutrals);
            Apply(_ui.SpecialsTabImg, _ui.SpecialsTabTmp, _category == EntityCategory.Specials);
            Apply(_ui.PlayersTabImg,  _ui.PlayersTabTmp,  _category == EntityCategory.Players);
        }

        private void SetMode(EditorMode mode)
        {
            _mode = mode;
            // An arming that describes the previous gesture is worse than no warning at all.
            DisarmRename();
            RefreshModeButtons();
            // The hint under the mode buttons tracks the mode. It used to be one static line
            // ("Select a mode then click on the map") that was true of every mode and useful
            // for none.
            if (_ui.AddRemoveHintText != null)
                _ui.AddRemoveHintText.text = _mode switch
                {
                    EditorMode.Select      => "Click an NPC on the map to select it. Right-drag to move it.",
                    EditorMode.Spawn       => string.IsNullOrEmpty(_selectedKey)
                        ? "Pick an entity first, then click the map to place it."
                        : $"Click the map to place '{_selectedKey}'. Or drag it from the Picker.",
                    EditorMode.Delete      => "Click an NPC on the map to remove it. Ctrl+Z brings it back.",
                    EditorMode.AddOnSystem => "Type a key above, then press Confirm to create a new definition.",
                    _                      => "Select a mode then click on the map.",
                };

            SetStatus(_mode switch
            {
                EditorMode.Select      => "Select mode. Click an entity on the map.",
                EditorMode.Spawn       => string.IsNullOrEmpty(_selectedKey)
                    ? "Spawn mode: select an entity in the Picker first."
                    : $"Spawn mode: click on map to place '{_selectedKey}'.",
                EditorMode.Delete      => "Delete mode: click entity to remove. Undoable.",
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
            // Confirm is an action button, not a mode -- keep neutral.
            Apply(_ui.ConfirmBtnImg,     _ui.ConfirmBtnTmp,     false);
        }

        // â”€â”€ Picker â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void RefreshPicker()
        {
            if (_ui.PickerContent == null) return;

            ResolveMonsterCatalogFallback();

            for (int i = _ui.PickerContent.childCount - 1; i >= 0; i--)
            {
                // Same guard EntitiesEditorUIBuilder.ClearSection already uses two files
                // over: Object.Destroy is deferred and, outside Play Mode, Unity answers it
                // with an error. The picker is refreshed by Create/Duplicate/Rename, which
                // are Editor-time operations, so this path genuinely runs in both modes.
                var child = _ui.PickerContent.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else                       DestroyImmediate(child);
            }

            string filter = _searchFilter?.Trim().ToLowerInvariant() ?? "";
            int shown = 0;

            bool listPlayers = _category == EntityCategory.Players || _category == EntityCategory.All;
            bool listMonsters = _category != EntityCategory.Players;

            if (listPlayers)
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
            if (listMonsters && _monsterCatalog != null)
            {
                foreach (var def in _monsterCatalog.Definitions)
                {
                    if (!MatchesCategory(def, _category)) continue;

                    string key  = def.monsterKey;
                    string name = def.displayName ?? key;
                    if (!PassesFilter(name, key, filter)) continue;
                    shown++;

                    Sprite icon = ResolveMonsterIconSprite(def);
                    Color tint = def.assetConfig != null
                        ? NormalizeTint(def.assetConfig.scaleConfig.tint)
                        : Color.white;
                    AddPickerSlot(name, key, isPlayer: false, sprite: icon, tint: tint);
                }
            }

            string label = _category switch
            {
                EntityCategory.All      => "entities",
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

        /// <summary>
        /// Which tab an entity belongs to, from its AUTHORED DATA.
        ///
        /// <para><b>It used to be a string search over <c>monsterKey</c></b> — for "boss",
        /// "vendor", "guard", "civilian" — with a comment calling itself provisional. Measured
        /// over the 28 shipped definitions it misfiled one:
        /// <c>barbol_brother_felipondor</c> authors <c>faction: NEUTRAL</c> and landed in
        /// Hostiles because his key contains none of the words. The tab said he was something
        /// the game does not think he is.</para>
        ///
        /// <para><c>stats.faction</c> is no longer inert: since the AI audit, <c>EntityFaction</c>
        /// reads it to decide who fights whom, who drops loot and who is untouchable. A tab that
        /// sorts by the NAME while the game sorts by the FIELD is two answers to one question,
        /// and the one the player experiences is the field.</para>
        ///
        /// <para><c>bossDefinition</c> — an object reference, not a substring — is what makes
        /// something Special, and it outranks the faction: a boss is a boss whichever side it is
        /// on, and that is the tab an author goes looking for it in.</para>
        /// </summary>
        private static bool MatchesCategory(MonsterDefinition def, EntityCategory cat)
        {
            if (def == null) return false;
            if (cat == EntityCategory.All) return true;

            bool isSpecial = def.bossDefinition != null;
            bool isNeutral = !isSpecial && IsNeutralFaction(def.stats.faction);

            return cat switch
            {
                EntityCategory.Specials => isSpecial,
                EntityCategory.Neutrals => isNeutral,
                EntityCategory.Hostiles => !isSpecial && !isNeutral,
                _                       => false
            };
        }

        /// <summary>
        /// Whether an authored faction string means "does not fight".
        ///
        /// <para>Compared case-insensitively and against the two spellings the shipped data uses,
        /// because the field is free text that was imported from Python: an unrecognised value
        /// reads as hostile, which is the failure that is VISIBLE (a vendor in the wrong tab)
        /// rather than the one that hides a monster from every tab.</para>
        /// </summary>
        private static bool IsNeutralFaction(string faction)
        {
            if (string.IsNullOrWhiteSpace(faction)) return false;
            string f = faction.Trim().ToLowerInvariant();
            return f == "neutral" || f == "friendly" || f == "ally" || f == "allied";
        }

        private void AddPickerSlot(string name, string key, bool isPlayer, Sprite sprite, Color tint)
        {
            var (btn, icon, label) = EditorUIHelpers.MakeSlotButton(
                _ui.PickerContent, name, 72f,
                () => { if (isPlayer) SelectPlayerClass(key); else SelectEntity(key); });

            if (sprite != null) { icon.sprite = sprite; icon.enabled = true; }
            icon.color = tint;

            // THE WIDGET DECIDES HOW MUCH FITS, not a character count written here.
            //
            // This used to be TruncateName(name, 9), dropping to 7 when the slot also carried
            // its placed count -- and measured against the shipped catalogue that is not a
            // shortening, it is a COLLISION: 20 of the 28 names are longer than 9, and at 7 the
            // eleven barbols all render as 'Barbol...'. Four pairs were pixel-identical on
            // screen at once (Baby/Boss, Gigante/Gris, Morado/Musgo, Cyan/Coloso).
            //
            // And the room was already there: the label rect measures 66 px and TMP reports
            // 'Barbol Gigante' at 60. Ellipsis-on-overflow spends exactly the width available,
            // so widening the panel -- which its resize grip now allows -- reveals more of the
            // name instead of changing nothing.
            label.text               = name;
            label.enableWordWrapping = false;
            label.overflowMode       = TextOverflowModes.Ellipsis;
            label.enableAutoSizing   = true;
            label.fontSizeMin        = 7f;
            label.fontSizeMax        = 9f;

            // How many of this one are ALREADY on the map. The panel that chooses what to put
            // down said nothing about what is down -- so an author placing a second boss, or
            // hunting the one they placed an hour ago, had the map and only the map to go on.
            int placed = isPlayer ? 0 : CountPlacedInstances(key);
            if (placed > 0)
                EntitiesEditorUIBuilder.MakeSlotCountBadge(btn.transform).text = "x" + placed;

            // Through SetSlotTint, never btn.GetComponent<Image>().color. A slot is a
            // Selectable on ColorTint: its CanvasRenderer colour MULTIPLIES with the Graphic's,
            // and Unity rewrites it from colors.normalColor on every transition -- a pointer
            // entering or leaving, an enable, any CanvasGroup change up the panel. Written
            // directly, the selection rendered as written x normalColor (darker than either)
            // and reverted to flat SLOT_BG seconds later. UIButton.SetTint documents the
            // measured arithmetic; this call site was the last one in the editor still
            // bypassing it.
            if (key == _selectedKey)
                EditorUIHelpers.SetSlotTint(btn, EditorUIHelpers.SLOT_SELECTED);
            else if (placed > 0)
                // Dimmer than the selection and brighter than a resting slot: "this exists in
                // the world" is a weaker statement than "this is what you are editing", and a
                // marker as loud as the selection makes the selection unreadable.
                EditorUIHelpers.SetSlotTint(btn, UITheme.SLOT_HOVER);

            // The full name, the key, and what is placed -- in the status line, restored on
            // exit. An ellipsised label needs SOMEWHERE to say the rest, and this editor had no
            // tooltip of any kind; UIHoverText already handles the case that makes a naive one
            // wrong here, which is a row destroyed while hovered (the picker rebuilds its slots
            // on every keystroke of the search box, so that is the normal case, not an edge).
            UIHoverText.Attach(btn.gameObject, _ui.StatusText,
                placed > 0 ? $"{name}  ({key})  —  {placed} on the map"
                           : $"{name}  ({key})");

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
        /// <summary>
        /// The tint a picker icon is drawn with — which is NOT the tint the entity wears.
        ///
        /// <para><b>A picker slot identifies, it does not preview.</b> Seven shipped entities
        /// author a tint dark enough to be invisible on this panel: the six Dark twins at
        /// exactly (0,0,0) and <c>barbol_oscuro</c> at 0.12, against a surface of 0.13. Drawn
        /// faithfully they are seven black squares with a truncated label under them, which is
        /// the one thing a picker may not be — and it is not a rendering bug, it is the roster
        /// being a silhouette roster and the panel repeating it.</para>
        ///
        /// <para>So a too-dark tint is lifted in HSV, keeping hue and saturation: a red-tinted
        /// monster still reads red, and a pure black one becomes grey — the honest answer for
        /// art whose whole identity is "no colour". Value is raised rather than the channels
        /// multiplied up, because multiplying a near-black colour keeps it near-black and
        /// scaling the channels independently drifts the hue.</para>
        ///
        /// <para>Lifting to a fixed VALUE is the version that looks right and is not: value and
        /// luminance are not the same thing, so a saturated dark hue comes back still unreadable
        /// while a grey clears easily. It solves for the value that reaches the luminance.</para>
        /// </summary>
        private static Color NormalizeTint(Color c)
        {
            // (0,0,0,0) is the "nobody authored one" sentinel, the same shape scaleConfig.tint
            // carries everywhere else. An alpha-zero black is an UNTINTED entity, not a black one.
            if (c.a <= 0f && c.r == 0f && c.g == 0f && c.b == 0f)
                return Color.white;

            var opaque = new Color(c.r, c.g, c.b, 1f);

            // Rec. 709, the same weighting the world bars' contrast check uses, so "too dark"
            // means the same thing in both places.
            float luminance = 0.2126f * opaque.r + 0.7152f * opaque.g + 0.0722f * opaque.b;
            if (luminance >= PICKER_ICON_MIN_LUMINANCE) return opaque;

            Color.RGBToHSV(opaque, out float h, out float sat, out float _);

            // Lifted until the LUMINANCE reaches the floor, not until VALUE reaches a constant.
            // HSVToRGB scales linearly with V, so the luminance at full value says exactly how
            // far it has to go -- and a fixed V is wrong for a saturated hue: a dark red at
            // V = 0.62 is still luminance 0.13, right back on the surface it had to clear.
            Color full = Color.HSVToRGB(h, sat, 1f);
            float fullLuminance = 0.2126f * full.r + 0.7152f * full.g + 0.0722f * full.b;
            float value = fullLuminance > 0.0001f
                ? Mathf.Clamp01(PICKER_ICON_MIN_LUMINANCE / fullLuminance)
                : 1f;

            var lifted = Color.HSVToRGB(h, sat, value);
            lifted.a = 1f;
            return lifted;
        }

        /// <summary>
        /// Below this an icon stops being readable on the panel's own surface (0.13).
        ///
        /// <para>Rec. 709, the same weighting the world bars' contrast check uses, so "too dark"
        /// means the same thing in both places.</para>
        /// </summary>
        private const float PICKER_ICON_MIN_LUMINANCE = 0.25f;

        /// <summary>
        /// Total frames across every "Sprite Sheet Mode" list on a config. All EIGHT lists
        /// are counted, including chase and recover: a character can be authored entirely
        /// through the sheet path, and omitting a list would under-report exactly the
        /// characters that use it. Null lists are an unauthored state, not zero frames of
        /// something - they simply contribute nothing.
        /// </summary>
        private static int CountSheetFrames(EntityAssetConfig ac)
        {
            if (ac == null) return 0;
            return (ac.idleSheets?.Count    ?? 0)
                 + (ac.walkSheets?.Count    ?? 0)
                 + (ac.chaseSheets?.Count   ?? 0)
                 + (ac.castSheets?.Count    ?? 0)
                 + (ac.attackSheets?.Count  ?? 0)
                 + (ac.damageSheets?.Count  ?? 0)
                 + (ac.deathSheets?.Count   ?? 0)
                 + (ac.recoverSheets?.Count ?? 0);
        }

        // â”€â”€ Player icons â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        //
        // The picker shows player classes using their south-facing idle sprite
        // (Python parity: ClassSelectorManager renders the down/idle frame).
        // PlayerDefinition assets live under Assets/_Project/Data/Catalogs/Players
        // and use the spritesheet path (`idleSheets`) where frames are arranged in
        // 8 contiguous direction buckets of 5 frames; bucket 0 == South.

        private Dictionary<string, Sprite> _playerIconCache;
        private PlayerDefinition[] _allPlayerDefsCache;

        /// <summary>
        /// The sprite that stands for <paramref name="def"/> -- in the picker grid and in the
        /// Assets row, which ask the same question and must not answer it differently.
        ///
        /// The sheet fallback is not cosmetic. Every monster built by the frame-sheet pipeline
        /// (the six dark twins, wave13's barbols and the dragon) leaves <c>idle.south</c> null
        /// and keeps its art in <c>idleSheets</c>, whose first frame IS the south-facing one:
        /// <see cref="DirectionalAnimator.CreateSetFromLinearFrames"/> cuts that list into eight
        /// contiguous buckets starting at South. Without it those entities showed no icon at all
        /// and described themselves as a dash, which reads as "this monster has no art"
        /// rather than "this monster is animated".
        /// <see cref="ResolvePlayerSouthSprite"/> already answered this for players.
        /// </summary>
        private static Sprite ResolveMonsterIconSprite(MonsterDefinition def)
        {
            var config = def != null ? def.assetConfig : null;
            if (config == null) return null;
            if (config.idle.south != null) return config.idle.south;
            if (config.idleSheets == null) return null;

            for (int i = 0; i < config.idleSheets.Count; i++)
            {
                if (config.idleSheets[i] != null) return config.idleSheets[i];
            }
            return null;
        }

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
            // 8-direction x 5-frame layout used by all migrated player sheets).
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

        // â”€â”€ Selection â†’ properties â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void SelectEntity(string key)
        {
            _selectedKey      = key;
            _selectedIsPlayer = false;
            DisarmRename();
            RefreshPicker();
            ShowMonsterProperties(key);
            NotifyAnimationSelectionChanged();
        }

        private void SelectPlayerClass(string key)
        {
            _selectedKey      = key;
            _selectedIsPlayer = true;
            RefreshPicker();
            ShowPlayerProperties(key);
            NotifyAnimationSelectionChanged();
        }

        // ── Editable stat rows ──────────────────────────────────────────────────
        //
        // A committed row does three things, in this order:
        //   1. parse + clamp, refusing garbage rather than writing a broken value
        //   2. write the field on the MonsterDefinition and mark the asset dirty
        //   3. re-apply the definition to every LIVE monster of that key
        //
        // Step 3 is what makes this a tuning loop instead of a form. It reuses
        // EntitySetup.ConfigureMonster — the same idempotent path the DevConsole
        // `reconfig` command uses — so positions are preserved. Note that it also
        // re-initialises Health, so a monster being tuned mid-fight comes back to
        // full HP; that is `reconfig`'s documented behaviour, not a new quirk.

        private void AddIntStat(RectTransform section, string label, int current, int min,
                                System.Action<int> apply, MonsterDefinition def)
        {
            EntitiesEditorUIBuilder.AddEditableRow(section, label, current.ToString(), raw =>
            {
                if (!int.TryParse(raw, out int parsed))
                {
                    SetStatus($"'{raw}' is not a whole number — {label} unchanged.");
                    ShowMonsterProperties(def.monsterKey);
                    return;
                }
                apply(Mathf.Max(min, parsed));
                CommitDefinitionEdit(def, label);
            }, TMPro.TMP_InputField.ContentType.IntegerNumber);
        }

        private void AddFloatStat(RectTransform section, string label, float current, float min,
                                  System.Action<float> apply, MonsterDefinition def)
        {
            EntitiesEditorUIBuilder.AddEditableRow(section, label, current.ToString("0.###"), raw =>
            {
                if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                {
                    SetStatus($"'{raw}' is not a number — {label} unchanged.");
                    ShowMonsterProperties(def.monsterKey);
                    return;
                }
                apply(Mathf.Max(min, parsed));
                CommitDefinitionEdit(def, label);
            });
        }

        /// <summary>
        /// Boolean counterpart to <see cref="AddIntStat"/>/<see cref="AddFloatStat"/> — added
        /// alongside them for <c>MonsterDefinition.autoCast</c>, the flag that used to have no
        /// editable widget in any editor (the F5 properties panel only ever rendered it as a
        /// read-only label).
        /// </summary>
        private void AddBoolStat(RectTransform section, string label, bool current,
                                 System.Action<bool> apply, MonsterDefinition def)
        {
            EntitiesEditorUIBuilder.AddToggleRow(section, label, current, v =>
            {
                apply(v);
                CommitDefinitionEdit(def, label);
            });
        }

        // ── Auto-Cast list editing ──────────────────────────────────────────────
        //
        // MonsterDefinition.autoCast / autoCastList are consumed at spawn time by
        // EntitySetup.ConfigureMonsterAutoCast, but until now nothing could WRITE them —
        // all 19 shipped monsters shipped autoCast=false with an empty list, and there was
        // no working example to copy. Entries are edited through a dropdown of catalog
        // keys rather than free text: the widget itself is the validation, so a mistyped
        // key can't be authored the way it could through a text field.

        /// <summary>
        /// Validates <paramref name="spellKey"/> against the injected SpellCatalog before
        /// appending it to <see cref="MonsterDefinition.autoCastList"/>. Refuses (and reports
        /// through <see cref="SetStatus"/>) an unknown key or a duplicate — appending either
        /// would either be silently skipped by <c>ConfigureMonsterAutoCast</c> at spawn time
        /// (unknown key) or waste a spell-caster slot on a repeat (duplicate).
        /// </summary>
        private bool TryAddAutoCastSpell(MonsterDefinition def, string spellKey)
        {
            if (def == null || string.IsNullOrWhiteSpace(spellKey)) return false;

            if (_spellCatalog == null || !_spellCatalog.TryGet(spellKey, out _))
            {
                SetStatus($"'{spellKey}' is not a known spell — autoCastList unchanged.");
                return false;
            }

            var existing = def.autoCastList ?? System.Array.Empty<string>();
            foreach (var s in existing)
            {
                if (string.Equals(s, spellKey, System.StringComparison.OrdinalIgnoreCase))
                {
                    SetStatus($"'{spellKey}' is already in {def.monsterKey}'s auto-cast list.");
                    return false;
                }
            }

            var list = new List<string>(existing) { spellKey };
            def.autoCastList = list.ToArray();
            CommitDefinitionEdit(def, "Auto-Cast");
            return true;
        }

        /// <summary>Swaps the spell at <paramref name="index"/>, re-validating against the catalog.</summary>
        private bool TrySetAutoCastSpellAt(MonsterDefinition def, int index, string spellKey)
        {
            if (def == null || def.autoCastList == null) return false;
            if (index < 0 || index >= def.autoCastList.Length) return false;

            if (_spellCatalog == null || !_spellCatalog.TryGet(spellKey, out _))
            {
                SetStatus($"'{spellKey}' is not a known spell — autoCastList unchanged.");
                return false;
            }

            def.autoCastList[index] = spellKey;
            CommitDefinitionEdit(def, "Auto-Cast");
            return true;
        }

        /// <summary>Removes the entry at <paramref name="index"/> from <c>autoCastList</c>.</summary>
        private void RemoveAutoCastSpellAt(MonsterDefinition def, int index)
        {
            if (def == null || def.autoCastList == null) return;
            if (index < 0 || index >= def.autoCastList.Length) return;

            var list = new List<string>(def.autoCastList);
            list.RemoveAt(index);
            def.autoCastList = list.ToArray();
            CommitDefinitionEdit(def, "Auto-Cast");
        }

        /// <summary>
        /// Persists the edited definition and pushes it onto everything already alive.
        /// </summary>
        private void CommitDefinitionEdit(MonsterDefinition def, string label)
        {
            if (def == null) return;

            // SetDirty alone, never Undo.RecordObject: a bulk editor that records to the GLOBAL
            // undo stack is what silently reverted 193 building templates in memory the first
            // time anything popped it. MarkDirty also remembers WHICH assets this editor
            // touched, so Save writes those and not the whole project.
            MarkDirty(def);

            // One seam, so every mutation in this editor is undoable: stats, the auto-cast
            // list, the timeline, the spell muzzle, the rename. Before this, Record was called
            // from exactly one place and Ctrl+Z gave back the last entity DRAG.
            RecordDefinitionUndo(def, label);

            int live = ReapplyToLiveMonsters(def);
            SetStatus(live > 0
                ? $"{label} updated — {live} live {def.monsterKey} reconfigured. Save to write the asset."
                : $"{label} updated. Save to write the asset.");
            RefreshPicker();
        }

        /// <summary>
        /// Re-runs the shipped configure path on every spawned monster sharing this
        /// definition. Returns how many were touched.
        /// </summary>
        private int ReapplyToLiveMonsters(MonsterDefinition def)
        {
            int count = 0;
            var monsters = new List<GameObject>(EntityRegistry.Monsters);
            foreach (var go in monsters)
            {
                if (go == null) continue;
                var brain = go.GetComponent<FSM.FSMMonsterBrain>();
                if (brain == null || brain.Definition != def) continue;
                EntitySetup.ConfigureMonster(go, def);
                count++;
            }
            return count;
        }

        private void ShowMonsterProperties(string key)
        {
            ClearPropsSections();
            if (_monsterCatalog == null) { ShowPropsHint("Monster catalog not assigned."); return; }

            var def = _monsterCatalog.GetByKey(key);
            if (def == null) { ShowPropsHint($"Entity '{key}' not found."); return; }

            // Seeded HERE, where a definition is resolved for DISPLAY, because that is the last
            // moment before an edit becomes possible. Seeding in CurrentEditableMonster was not
            // enough and it failed exactly as this file's own comment warned: the stat rows
            // capture `def` directly and never call it, so the FIRST change to each definition
            // recorded nothing. Measured -- three edits, two undo steps, and the HP never came
            // back.
            SeedDefinitionSnapshot(def);

            ResolveSpellCatalogFallback();
            HidePropsHint();
            var s = def.stats;

            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsIdentitySection, "Key",  def.monsterKey);
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsIdentitySection, "Name", def.displayName);
            FillPlacementSection();

            // The stats a designer actually tunes are editable and write straight back
            // to the .asset; the rest stay labels. `power` is deliberately NOT editable —
            // no runtime code reads it beyond an XP fallback, and an input box that
            // silently changes nothing is worse than a label that admits it.
            AddIntStat(_ui.PropsStatsSection,   "HP",          s.hp,          1,     v => def.stats.hp = v,          def);
            AddFloatStat(_ui.PropsStatsSection, "Speed",       s.speed,       0f,    v => def.stats.speed = v,       def);
            AddFloatStat(_ui.PropsStatsSection, "Chase Speed", s.chasingSpeed, 0f,   v => def.stats.chasingSpeed = v, def);
            // Defense mitigates as of the damage-model pass — Health.MitigateDamage
            // subtracts it with a floor of 1 — so it is a real knob now, not a label.
            AddIntStat(_ui.PropsStatsSection,   "Defense",     s.defense,     0,     v => def.stats.defense = v,     def);
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsStatsSection, "Power",    $"{s.power}  (xp fallback only)");
            AddIntStat(_ui.PropsStatsSection,   "Melee Dmg",   s.meleeDamage, 0,     v => def.stats.meleeDamage = v, def);
            // meleeRange is a float now: "knife range" was not expressible while it was an
            // int, and the shipped values were 0, 2, 3 and 7 with nothing in between.
            AddFloatStat(_ui.PropsStatsSection, "Melee Range", s.meleeRange,  0f,    v => def.stats.meleeRange = v,  def);
            AddFloatStat(_ui.PropsStatsSection, "Melee CD",    s.meleeCooldown, 0.01f, v => def.stats.meleeCooldown = v, def);
            AddFloatStat(_ui.PropsStatsSection, "Aggro Range", s.aggroRange,  0f,    v => def.stats.aggroRange = v,  def);
            AddFloatStat(_ui.PropsStatsSection, "Atk Windup",  s.attackWindupSeconds, 0f,
                                                                              v => def.stats.attackWindupSeconds = v, def);

            // Both are plain strings and both are null on a definition that was just
            // created rather than loaded — the F5 "Create" button mints exactly that, and
            // .ToString() on the null one took the whole properties panel down with it.
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsAISection, "FSM Set",
                string.IsNullOrEmpty(def.fsmSet) ? "(none)" : def.fsmSet);
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsAISection, "Patrol",
                string.IsNullOrEmpty(def.patrolType) ? "(none)" : def.patrolType);
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsAISection, "Telegraph", def.useAttackTelegraph ? "yes" : "no");

            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsSpawnSection, "Count",   s.spawnCount.ToString());
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsSpawnSection, "Padding", s.spawnPadding.ToString());
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsSpawnSection, "Margin",  s.spawnMargin.ToString());
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsSpawnSection, "Faction", s.faction);

            // Enabled toggle + a dropdown-per-entry list validated against the SpellCatalog —
            // see the "Auto-Cast list editing" region above for the write path. The dropdown
            // itself is the validation, so a mistyped key can never be authored here.
            AddBoolStat(_ui.PropsAutoCastSection, "Enabled", def.autoCast, v => def.autoCast = v, def);

            string[] spellKeys = _spellCatalog != null ? _spellCatalog.GetAllKeys() : System.Array.Empty<string>();
            System.Array.Sort(spellKeys, System.StringComparer.OrdinalIgnoreCase);

            var autoCastList = def.autoCastList ?? System.Array.Empty<string>();
            for (int i = 0; i < autoCastList.Length; i++)
            {
                int idx = i; // capture per-iteration for the closures below
                EntitiesEditorUIBuilder.AddSpellListRow(_ui.PropsAutoCastSection, $"#{idx + 1}",
                    spellKeys, autoCastList[idx],
                    newKey =>
                    {
                        if (TrySetAutoCastSpellAt(def, idx, newKey)) ShowMonsterProperties(def.monsterKey);
                    },
                    () =>
                    {
                        RemoveAutoCastSpellAt(def, idx);
                        ShowMonsterProperties(def.monsterKey);
                    });
            }

            if (spellKeys.Length == 0)
            {
                EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsAutoCastSection, "-",
                    _spellCatalog == null ? "spell catalog not available" : "no spells in catalog");
            }
            else
            {
                EntitiesEditorUIBuilder.AddSpellAddRow(_ui.PropsAutoCastSection, spellKeys, newKey =>
                {
                    if (TryAddAutoCastSpell(def, newKey)) ShowMonsterProperties(def.monsterKey);
                });
            }

            // Resolved through the SAME helper the picker grid uses, so the icon and this row
            // can never name different sprites.
            Sprite idleSprite = ResolveMonsterIconSprite(def);
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsAssetsSection, "Idle Sprite",
                idleSprite != null ? idleSprite.name : "\u2014");

            // The three blocks the panel used not to have. Filled last so they sit under the
            // ones an author reaches for most, and so the existing order does not move.
            FillExtraStats(def);
            FillAITuningSection(def);
            FillRewardSection(def);

            // Show "Open Boss Editor →" button when the monster has a BossDefinition.
            UpdateBossHandoffButton(key);

            ApplyPropsFilter();
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

            // ── Assets section ──────────────────────────────────────────────────────────
            // Read off the PlayerDefinition's EntityAssetConfig, resolved through the SAME
            // lookup the picker uses for its icons, so a class listed there always has its
            // assets described here. Each value is reported the way its CONSUMER reads it.
            var def = FindPlayerDefinition(key);
            if (def != null && def.assetConfig != null)
            {
                var ac = def.assetConfig;

                string idleSprite = ac.idle.south != null ? ac.idle.south.name : "\u2014";
                EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsAssetsSection, "Idle Sprite", idleSprite);

                // scaleIdle <= 0 is not missing data: EntityAnimationBinder.ApplyEntityScale
                // skips the resize entirely, which is what every player def relies on.
                EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsAssetsSection, "Scale",
                    ac.scaleConfig.scaleIdle > 0f
                        ? ac.scaleConfig.scaleIdle.ToString("0.##")
                        : "1 (unscaled)");

                // NormalizeTint promotes the unset (0,0,0,0) colour to white \- the reading
                // EntityAnimationBinder and the picker icons both apply. ToHtmlStringRGB
                // never returns null, so there is no empty case to guard.
                EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsAssetsSection, "Tint",
                    $"#{ColorUtility.ToHtmlStringRGB(NormalizeTint(ac.scaleConfig.tint))}");

                int sheetFrames = CountSheetFrames(ac);
                EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsAssetsSection, "Sheet Frames",
                    sheetFrames > 0 ? sheetFrames.ToString() : "\u2014");

                // <= 0 is treated as 1 by DirectionalAnimator.SetAnimationSpeedMultiplier \-
                // which is what every asset predating the field deserializes to.
                float animSpeed = ac.scaleConfig.animationSpeedMultiplier;
                EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsAssetsSection, "Anim Speed",
                    animSpeed > 0f ? $"{animSpeed:0.##}x" : "1x (default)");
            }
            else
            {
                EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsAssetsSection, "Assets",
                    def == null ? "PlayerDefinition not found" : "no asset config");
            }

            if (_ui.BossHandoffBtnGo != null) _ui.BossHandoffBtnGo.SetActive(false);

            ApplyPropsFilter();
            SetStatus($"Selected: {p.DisplayName ?? key}");
        }

        private void ClearPropsSections()
        {
            EntitiesEditorUIBuilder.ClearSection(_ui.PropsIdentitySection);
            EntitiesEditorUIBuilder.ClearSection(_ui.PropsStatsSection);
            EntitiesEditorUIBuilder.ClearSection(_ui.PropsAISection);
            EntitiesEditorUIBuilder.ClearSection(_ui.PropsSpawnSection);
            EntitiesEditorUIBuilder.ClearSection(_ui.PropsAutoCastSection);
            EntitiesEditorUIBuilder.ClearSection(_ui.PropsAITuningSection);
            EntitiesEditorUIBuilder.ClearSection(_ui.PropsRewardSection);
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

        // â”€â”€ Map Interaction â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void HandleMapInteraction()
        {
            // Don't bail when Mouse.current is null — MouseInputManager wraps the new
            // InputSystem AND the legacy backend, and the legacy half is the one that still
            // works during the recurring Unity 2022.3 Editor event-drop bug this project
            // exists to survive. The `mouse` local here was read for the null check and
            // never used again, so the gate did nothing but disable F5's map clicks in
            // exactly the situation the fallback was built for. Buildings and Items removed
            // the same gate.
            if (!Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonPressedThisFrame()) return;
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

            var cam = Camera.main;
            if (cam == null) return;

            var worldPos = cam.ScreenToWorldPoint(Valkur.Core.Input.MouseInputManager.GetScreenMousePosition());
            worldPos.z = 0;

            switch (_mode)
            {
                case EditorMode.Spawn       when !string.IsNullOrEmpty(_selectedKey): SpawnEntityAtPosition(worldPos);  break;
                case EditorMode.Delete:                                                DeleteEntityAtPosition(worldPos); break;
                case EditorMode.Select:                                                SelectEntityAtPosition(worldPos); break;
                case EditorMode.AddOnSystem when !string.IsNullOrEmpty(_selectedKey): SetStatus($"Add-On-System: ({worldPos.x:F1}, {worldPos.y:F1}) \u2014 Confirm to persist."); break;
            }
        }

        /// <summary>
        /// Add-mode click-to-spawn.
        ///
        /// This used to be a status-string stub while the only working spawn was the
        /// undiscoverable drag-from-picker gesture, so a designer picked a monster,
        /// pressed Add, clicked the map, read a confirmation, and nothing appeared.
        /// Both gestures now land on the same path.
        /// </summary>
        private void SpawnEntityAtPosition(Vector3 worldPos)
        {
            PlaceEntityFromDrag(_selectedKey, _selectedIsPlayer, worldPos);
        }

        private void DeleteEntityAtPosition(Vector3 worldPos)
        {
            var hit = Physics2D.OverlapCircle(worldPos, 0.5f, LayerMask.GetMask("NPC"));
            if (hit != null)
            {
                // Recorded BEFORE the destroy: afterwards there is no object left to read the
                // key and the position off, and those two are the whole of what a restore needs.
                var placement = hit.GetComponent<PersistedEntityInstance>();
                if (placement != null && placement.IsDefeated) placement = null;   // a corpse
                if (placement != null) RecordPlacementDeleted(placement);

                SetStatus(placement != null
                    ? $"Deleted {hit.gameObject.name} — Ctrl+Z to bring it back."
                    : $"Deleted {hit.gameObject.name}");

                // A placement goes through the service, which takes it out of the saved file
                // too; otherwise it would come back on the next Play. Anything else (a spawner's
                // monster, a corpse) is just a GameObject.
                if (placement != null) { PlacedEntities.Remove(placement.PlacementId); RefreshPicker(); }
                else                   Destroy(hit.gameObject);
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

        // TruncateName is deliberately GONE rather than left unused: a helper that cuts a
        // label to a hand-written character count is the defect itself, and leaving it here is
        // leaving the next slot builder something convenient and wrong to reach for. TMP's
        // TextOverflowModes.Ellipsis spends the width the widget actually has.
    }
}
