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
            // Confirm is an action button, not a mode â€” keep neutral.
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

        // â”€â”€ Player icons â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
            // 8-direction Ã— 5-frame layout used by all migrated player sheets).
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

#if UNITY_EDITOR
            // SetDirty alone, never Undo.RecordObject: a bulk editor that records to
            // the GLOBAL undo stack is what silently reverted 193 building templates
            // in memory the first time anything popped it.
            UnityEditor.EditorUtility.SetDirty(def);
#endif
            int live = ReapplyToLiveMonsters(def);
            _pendingAssetWrites = true;
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

            ResolveSpellCatalogFallback();
            HidePropsHint();
            var s = def.stats;

            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsIdentitySection, "Key",  def.monsterKey);
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsIdentitySection, "Name", def.displayName);

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

            string idle = (def.assetConfig != null && def.assetConfig.idle.south != null)
                ? def.assetConfig.idle.south.name : "â€”";
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsAssetsSection, "Idle Sprite", idle);

            // Show "Open Boss Editor →" button when the monster has a BossDefinition.
            UpdateBossHandoffButton(key);

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

            if (_ui.BossHandoffBtnGo != null) _ui.BossHandoffBtnGo.SetActive(false);
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
                case EditorMode.AddOnSystem when !string.IsNullOrEmpty(_selectedKey): SetStatus($"Add-On-System: ({worldPos.x:F1}, {worldPos.y:F1}) â€” Confirm to persist."); break;
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
                SetStatus($"Deleted {hit.gameObject.name}");
                Debug.Log($"[EntitiesEditor] Deleted {hit.gameObject.name}");
                // Deleting a placement has to reach the saved file too, or it comes back on
                // the next Stop/Play the same way it would if it had never been removed.
                bool wasPlacedEntity = hit.GetComponent<PersistedEntityInstance>() != null;
                Destroy(hit.gameObject);
                if (wasPlacedEntity) MarkEntityPlacementsDirty();
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
