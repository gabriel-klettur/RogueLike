using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Enemies.FSM
{
    /// <summary>
    /// Entities panel — by_archetype + by_eid editing.
    /// Mirrors Python <c>fsm_assignments.json</c> structure.
    ///
    /// <c>by_archetype</c>'s key is now a <see cref="Valkur.Data.MonsterCatalog"/> picker
    /// (not free text) and its value a dropdown of the currently loaded FSM set ids in
    /// both categories — <c>FSMRuntimeFactory.LoadAssignmentsFromDisk</c> only ever
    /// resolves a set id that actually exists, so a typo used to fail silently at runtime
    /// with no diagnostic until a monster spawned. <c>by_eid</c> keeps free text for its
    /// key (there is no catalog of live entity ids to pick from) but is clearly labelled:
    /// <c>FSMRuntimeFactory</c> only ever reads <c>by_archetype</c>.
    ///
    /// The <c>by_archetype</c> view also lists what is NOT in the file. Listing only the
    /// keys <c>assignments.json</c> already holds made a monster with no assignment
    /// indistinguishable from one with an assignment: it appeared only inside the
    /// <c>+ Add</c> dropdown, rendered exactly like every covered monster beside it. Eight
    /// shipped monsters sat with no FSM set for months — each booting the hard-coded
    /// <c>IdleState</c> fallback — and nothing in this panel said so.
    /// </summary>
    public partial class FSMRuntimeEditor : SingletonMonoBehaviour<FSMRuntimeEditor>, GameEditorManager.IGameEditor
    {
        private string _entitiesCategory = "by_archetype"; // or "by_eid"

        /// <summary>
        /// Amber — the panel's EXISTING "you have to look at this" colour, first used by
        /// <see cref="BuildEntitiesByEidWarning"/>. The UNASSIGNED section reuses it rather
        /// than inventing a second warning hue, so a designer does not have to learn two
        /// colour languages to tell an actionable gap from decoration.
        /// </summary>
        private static readonly Color ENT_GAP_COLOR = new Color(0.85f, 0.55f, 0.20f, 1f);

        /// <summary>
        /// Grey — the panel's existing "inherited, not authored here" colour (the Animations
        /// panel dims a per-set row it did not author with exactly this value). A monster
        /// covered only by <c>MonsterDefinition.fsmSet</c> resolves a real set through
        /// <c>FSMRuntimeFactory</c>'s last-resort hint and is NOT broken; painting it amber
        /// would send an author chasing eight non-problems and blunt the real warning.
        /// </summary>
        private static readonly Color ENT_INHERITED_COLOR = new Color(0.5f, 0.5f, 0.5f, 1f);

        /// <summary>Where a monster key's FSM set actually comes from.</summary>
        public enum FSMSetSource
        {
            /// <summary>A <c>by_archetype</c> entry — what this panel edits.</summary>
            Assignment,
            /// <summary><c>MonsterDefinition.fsmSet</c>, resolved as the last-resort hint.</summary>
            DefinitionFallback,
            /// <summary>Neither source yields a loaded set — the entity boots a bare IdleState.</summary>
            Unassigned,
        }

        /// <summary>One catalog monster and the provenance of the set it will boot with.</summary>
        public class MonsterFSMCoverage
        {
            public string monsterKey;
            public FSMSetSource source;
            /// <summary>The set that will actually be used; empty when <see cref="source"/> is Unassigned.</summary>
            public string setId;
            /// <summary>Human-readable provenance (or the reason nothing resolved).</summary>
            public string note;
        }

        private void RefreshEntities()
        {
            var content = _uiRefs.EntitiesContent;
            if (content == null) return;
            // SafeDestroy: this refresh also runs from EditMode tests, where a raw
            // Object.Destroy is a silent no-op that logs an error instead.
            for (int i = content.childCount - 1; i >= 0; i--)
                SafeDestroy.GameObjectOf(content.GetChild(i));

            bool isArchetype = _entitiesCategory == "by_archetype";

            // Get current category dict from raw assignments
            var dict = GetAssignmentCategoryDict();
            var setIds = CollectSetIdsForPicker();

            // Coverage is a by_archetype-only concept: by_eid is keyed by F5 placement id,
            // for which no catalog to diff against exists.
            var coverage = isArchetype ? CollectMonsterCoverage() : null;

            // Header: prev/next category arrows + label (which states the gap as a number)
            BuildEntitiesHeader(content, ComposeEntitiesHeaderLabel(dict, coverage));

            if (!isArchetype)
                BuildEntitiesByEidWarning(content);

            if (dict == null)
            {
                EditorUIHelpers.AddLabel(content, "(no assignments loaded)", 11f);
                return;
            }

            // Sorted entries
            var keys = dict.Keys.OrderBy(k => k, System.StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var k in keys) BuildEntityRow(content, k, AsStr(dict[k]), setIds);

            // Add row
            BuildEntityAddRow(content, setIds);

            // …then everything the file does NOT cover.
            if (coverage != null) BuildEntityCoverageSections(content, coverage, setIds);
        }

        /// <summary>
        /// The header carries the counts so the gap is a number an author can act on rather
        /// than something they have to notice by scrolling. Every count is derived from the
        /// live catalog and the live assignment dict — nothing here assumes how many
        /// monsters ship or how many sets exist.
        /// </summary>
        private string ComposeEntitiesHeaderLabel(
            Dictionary<string, object> dict, List<MonsterFSMCoverage> coverage)
        {
            if (coverage == null) return _entitiesCategory;
            int assigned = dict?.Count ?? 0;
            int viaDefinition = coverage.Count(c => c.source == FSMSetSource.DefinitionFallback);
            int gap = coverage.Count(c => c.source == FSMSetSource.Unassigned);
            return $"{_entitiesCategory} — {assigned} assigned / {viaDefinition} via fsmSet / {gap} unassigned";
        }

        private void BuildEntitiesHeader(Transform parent, string labelText)
        {
            var row = new GameObject("EntHeader", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 26f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childForceExpandHeight = true;
            hlg.childControlHeight = true; hlg.childControlWidth = true;

            var prev = EditorUIHelpers.MakeButton(row.transform, "<", () => { _entitiesCategory = "by_archetype"; RefreshEntities(); }, 26f, 11f);
            var prevLE = prev.GetComponent<LayoutElement>() ?? prev.gameObject.AddComponent<LayoutElement>();
            prevLE.preferredWidth = 22f; prevLE.flexibleWidth = 0f;

            var lbl = EditorUIHelpers.AddLabel(row.transform, labelText, 11f);
            lbl.gameObject.name = "EntHeaderLabel";
            var lblLE = lbl.gameObject.AddComponent<LayoutElement>();
            lblLE.flexibleWidth = 1f;
            lbl.alignment = TextAlignmentOptions.Center;

            var next = EditorUIHelpers.MakeButton(row.transform, ">", () => { _entitiesCategory = "by_eid"; RefreshEntities(); }, 26f, 11f);
            var nextLE = next.GetComponent<LayoutElement>() ?? next.gameObject.AddComponent<LayoutElement>();
            nextLE.preferredWidth = 22f; nextLE.flexibleWidth = 0f;
        }

        /// <summary>
        /// This half used to warn that it was authored into a void:
        /// <c>FSMRuntimeFactory</c> parsed <c>by_archetype</c> only, so every entry here had
        /// zero effect on any live monster. It is now read
        /// (<c>FSMRuntimeFactory.TryBuildForEntity</c>), and this note explains the one thing
        /// a designer still has to know — the key is the F5 PLACEMENT id, not a monster key,
        /// and only a placed entity has one.
        /// </summary>
        private void BuildEntitiesByEidWarning(Transform parent)
        {
            var go = new GameObject("EntByEidWarning", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = 42f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = "by_eid overrides ONE placed entity's set, beating its archetype. " +
                       "The key is that placement's id (F5 placements only) — a monster key " +
                       "here matches nothing and is silently ignored.";
            tmp.fontSize = 10f;
            tmp.color = ENT_GAP_COLOR;
            tmp.enableWordWrapping = true;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
        }

        private void BuildEntityRow(Transform parent, string key, string val, List<string> setIds)
        {
            var row = new GameObject($"Ent_{key}", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 26f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childControlHeight = true; hlg.childControlWidth = true;
            hlg.childForceExpandHeight = true;

            var keyLbl = EditorUIHelpers.AddLabel(row.transform, key, 11f);
            (keyLbl.gameObject.AddComponent<LayoutElement>()).preferredWidth = 100f;

            // Value: a picker over the loaded sets, not free text — the check that the typed
            // set id actually exists. A stale/unknown value (e.g. a set that was since
            // deleted) is kept as an extra option rather than silently discarded, so an
            // author sees what's actually on disk instead of it quietly resolving to
            // whichever option happens to sit at index 0.
            var options = new List<string>(setIds);
            if (!string.IsNullOrEmpty(val) && !options.Contains(val)) options.Insert(0, val);
            int selected = Mathf.Max(0, options.IndexOf(val));

            var dropWrap = new GameObject("SetDropdownWrap", typeof(RectTransform));
            dropWrap.transform.SetParent(row.transform, false);
            dropWrap.AddComponent<LayoutElement>().flexibleWidth = 1f;
            // Cast pins the overload: UIDropdown.Add has both IList<string> and
            // IReadOnlyList<string> forms, and a List<string> satisfies each equally.
            var dropdown = UIDropdown.Add(dropWrap.transform,
                (IList<string>)(options.Count > 0 ? options : new List<string> { "(no sets loaded)" }), selected);
            if (options.Count > 0)
                dropdown.onValueChanged.AddListener(idx =>
                    CommitAssignment(key, options[Mathf.Clamp(idx, 0, options.Count - 1)]));

            var del = EditorUIHelpers.MakeButton(row.transform, "X", () => { CommitAssignment(key, ""); }, 26f, 11f);
            var dLE = del.GetComponent<LayoutElement>() ?? del.gameObject.AddComponent<LayoutElement>();
            dLE.preferredWidth = 22f; dLE.flexibleWidth = 0f;
            del.GetComponent<Image>().color = new Color(0.55f, 0.20f, 0.20f, 0.95f);
        }

        private void BuildEntityAddRow(Transform parent, List<string> setIds)
        {
            bool isArchetype = _entitiesCategory == "by_archetype";

            var row = new GameObject("EntAddRow", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 26f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childControlHeight = true; hlg.childControlWidth = true;
            hlg.childForceExpandHeight = true;

            // Key: MonsterCatalog picker for by_archetype — no catalog of runtime entity
            // ids exists for by_eid, so that half keeps a free-text field.
            TMP_Dropdown keyDropdown = null;
            TMP_InputField keyInput = null;
            List<string> monsterKeys = null;

            if (isArchetype)
            {
                monsterKeys = CollectMonsterKeysForPicker();
                var keyWrap = new GameObject("KeyDropdownWrap", typeof(RectTransform));
                keyWrap.transform.SetParent(row.transform, false);
                keyWrap.AddComponent<LayoutElement>().flexibleWidth = 1f;
                keyDropdown = UIDropdown.Add(keyWrap.transform,
                    (IList<string>)(monsterKeys.Count > 0 ? monsterKeys : new List<string> { "(no monsters in catalog)" }), 0);
            }
            else
            {
                keyInput = EditorUIHelpers.AddInputField(row.transform, "", null);
                (keyInput.gameObject.GetComponent<LayoutElement>() ?? keyInput.gameObject.AddComponent<LayoutElement>())
                    .flexibleWidth = 1f;
            }

            var valueWrap = new GameObject("SetDropdownWrap", typeof(RectTransform));
            valueWrap.transform.SetParent(row.transform, false);
            valueWrap.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var setDropdown = UIDropdown.Add(valueWrap.transform,
                (IList<string>)(setIds.Count > 0 ? setIds : new List<string> { "(no sets loaded)" }), 0);

            var addBtn = EditorUIHelpers.MakeButton(row.transform, "+ Add", () =>
            {
                string key = isArchetype
                    ? (monsterKeys.Count > 0 ? monsterKeys[Mathf.Clamp(keyDropdown.value, 0, monsterKeys.Count - 1)] : null)
                    : (keyInput.text ?? "").Trim();
                string setId = setIds.Count > 0 ? setIds[Mathf.Clamp(setDropdown.value, 0, setIds.Count - 1)] : null;
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(setId)) return;
                CommitAssignment(key, setId);
            }, 26f, 11f);
            (addBtn.GetComponent<LayoutElement>() ?? addBtn.gameObject.AddComponent<LayoutElement>()).preferredWidth = 50f;
            addBtn.GetComponent<Image>().color = new Color(0.20f, 0.35f, 0.20f, 0.95f);
        }

        // ── Coverage sections (what the file does NOT say) ───────────────────────

        /// <summary>
        /// Renders the two groups the assignment dict cannot show, because neither has an
        /// entry in it: monsters resolving through <c>MonsterDefinition.fsmSet</c>, and
        /// monsters resolving through nothing at all. Both are drawn from the same
        /// <see cref="Valkur.Data.MonsterCatalog"/> the <c>+ Add</c> key picker reads, so a
        /// monster can never be offered for assignment while being absent from this audit.
        /// </summary>
        private void BuildEntityCoverageSections(
            Transform parent, List<MonsterFSMCoverage> coverage, List<string> setIds)
        {
            var viaDefinition = coverage.Where(c => c.source == FSMSetSource.DefinitionFallback).ToList();
            var gap = coverage.Where(c => c.source == FSMSetSource.Unassigned).ToList();
            if (viaDefinition.Count == 0 && gap.Count == 0) return;

            if (viaDefinition.Count > 0)
            {
                BuildEntityCoverageHeader(parent, "EntFallbackHeader",
                    $"VIA MonsterDefinition.fsmSet ({viaDefinition.Count}) — resolved, but not from this file",
                    ENT_INHERITED_COLOR);
                foreach (var c in viaDefinition)
                    BuildEntityCoverageRow(parent, $"EntFallback_{c.monsterKey}", c,
                        ENT_INHERITED_COLOR, $"-> {c.setId}  ({c.note})", "(pin to assignments.json...)", setIds);
            }

            if (gap.Count > 0)
            {
                BuildEntityCoverageHeader(parent, "EntUnassignedHeader",
                    $"UNASSIGNED ({gap.Count}) — these boot the hard-coded IdleState fallback",
                    ENT_GAP_COLOR);
                foreach (var c in gap)
                    BuildEntityCoverageRow(parent, $"EntUnassigned_{c.monsterKey}", c,
                        ENT_GAP_COLOR, c.note, "(assign a set...)", setIds);
            }
        }

        /// <summary>
        /// A section banner. Note it carries no <see cref="Image"/>: a TMP component and an
        /// Image on the same GameObject throw a <c>NullReferenceException</c> in this
        /// project's uGUI setup, which is why <see cref="BuildEntitiesByEidWarning"/> is
        /// built the same bare way.
        /// </summary>
        private void BuildEntityCoverageHeader(Transform parent, string name, string text, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = 24f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 10f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = color;
            tmp.enableWordWrapping = true;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
        }

        private void BuildEntityCoverageRow(Transform parent, string name, MonsterFSMCoverage coverage,
            Color color, string note, string placeholder, List<string> setIds)
        {
            var row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            row.AddComponent<LayoutElement>().preferredHeight = 26f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.childControlHeight = true; hlg.childControlWidth = true;
            hlg.childForceExpandHeight = true;

            var keyLbl = EditorUIHelpers.AddLabel(row.transform, coverage.monsterKey, 11f);
            keyLbl.gameObject.name = "CoverageKeyLabel";
            keyLbl.color = color;
            (keyLbl.gameObject.AddComponent<LayoutElement>()).preferredWidth = 100f;

            var noteLbl = EditorUIHelpers.AddLabel(row.transform, note, 9f);
            noteLbl.gameObject.name = "CoverageNoteLabel";
            noteLbl.color = color;
            (noteLbl.gameObject.AddComponent<LayoutElement>()).flexibleWidth = 1f;

            BuildEntityAssignControl(row.transform, coverage.monsterKey, setIds, placeholder);
        }

        /// <summary>
        /// The one-click assign control: the SAME set-id dropdown
        /// <see cref="BuildEntityRow"/> gives an assigned row, fronted by a placeholder
        /// option so that picking any real set IS the commit.
        ///
        /// The placeholder is load-bearing. Without it the first set id would already sit
        /// selected at index 0, so choosing it would raise no change event — the row would
        /// look actionable, be clicked, and do nothing, which is the same class of silent
        /// no-op this whole section exists to expose.
        /// </summary>
        private void BuildEntityAssignControl(
            Transform row, string monsterKey, List<string> setIds, string placeholder)
        {
            var wrap = new GameObject("AssignDropdownWrap", typeof(RectTransform));
            wrap.transform.SetParent(row, false);
            wrap.AddComponent<LayoutElement>().flexibleWidth = 1f;

            bool hasSets = setIds.Count > 0;
            var options = new List<string> { hasSets ? placeholder : "(no sets loaded)" };
            if (hasSets) options.AddRange(setIds);

            // Cast pins the overload — see BuildEntityRow for why.
            var dropdown = UIDropdown.Add(wrap.transform, (IList<string>)options, 0);
            if (!hasSets) return;
            dropdown.onValueChanged.AddListener(idx =>
            {
                if (idx <= 0 || idx >= options.Count) return;
                CommitAssignment(monsterKey, options[idx]);
            });
        }

        // ── Picker data sources ──────────────────────────────────────────────────

        private List<string> CollectSetIdsForPicker()
            => _fsmSets.Select(s => s.id).Where(id => !string.IsNullOrEmpty(id))
                       .OrderBy(id => id, System.StringComparer.OrdinalIgnoreCase).ToList();

        private List<string> CollectMonsterKeysForPicker()
            => CollectCatalogDefinitions().Select(d => d.monsterKey).ToList();

        /// <summary>
        /// The ONE monster source this panel has. Both the <c>+ Add</c> key picker and the
        /// coverage audit read it, so a monster can never be offerable for assignment while
        /// being invisible to the audit — a second enumeration would let those two lists
        /// drift apart and re-hide exactly what this section is for.
        /// </summary>
        private List<Valkur.Data.MonsterDefinition> CollectCatalogDefinitions()
        {
            ResolveMonsterCatalogIfNeeded();
            var result = new List<Valkur.Data.MonsterDefinition>();
            if (_monsterCatalog == null) return result;
            foreach (var def in _monsterCatalog.Definitions)
                if (def != null && !string.IsNullOrEmpty(def.monsterKey))
                    result.Add(def);
            result.Sort((a, b) => string.Compare(a.monsterKey, b.monsterKey,
                System.StringComparison.OrdinalIgnoreCase));
            return result;
        }

        /// <summary>
        /// Classifies every catalog monster by where its FSM set comes from, mirroring the
        /// order <c>FSMRuntimeFactory.TryBuildForEntity</c> actually resolves in:
        /// <c>by_archetype</c> first, then <c>MonsterDefinition.fsmSet</c> as the
        /// last-resort hint, then nothing.
        ///
        /// The hint only counts when it names a set that is LOADED. An asset declaring
        /// <c>fsmSet: Some_Deleted_Set</c> resolves nothing at runtime, so reporting it as
        /// covered would recreate the original blind spot with an extra step — the panel
        /// would claim a monster was fine while it booted the hard-coded IdleState.
        /// <c>by_eid</c> is not consulted: it is keyed by F5 placement id, not by monster
        /// key, so it can neither confirm nor deny an archetype's coverage.
        /// </summary>
        private List<MonsterFSMCoverage> CollectMonsterCoverage()
        {
            var result = new List<MonsterFSMCoverage>();
            var byArchetype = GetAssignmentDict("by_archetype");
            // Ordinal, NOT OrdinalIgnoreCase: FSMRuntimeFactory declares _setsById with
            // StringComparer.Ordinal, so "monster_default" does NOT resolve to
            // "Monster_Default" at runtime. A case-insensitive check here would paint that
            // monster green while the game warns and boots a bare IdleState — a false
            // all-clear produced by the very check added to prevent false all-clears.
            var loaded = new HashSet<string>(CollectSetIdsForPicker(), System.StringComparer.Ordinal);

            foreach (var def in CollectCatalogDefinitions())
            {
                string key = def.monsterKey;

                string assigned = null;
                if (byArchetype != null && byArchetype.TryGetValue(key, out var raw))
                    assigned = AsStr(raw).Trim();

                if (!string.IsNullOrEmpty(assigned))
                {
                    // An assignment naming a set that is not loaded is not coverage — it is
                    // the worst of the three states. FSMRuntimeFactory warns and returns
                    // FALSE without falling through to the fsmSet hint, so a dangling entry
                    // also blocks the one thing that could have rescued the monster. Counting
                    // it as assigned would let the header read "0 unassigned" while monsters
                    // boot bare, which is the exact blind spot this audit exists to close.
                    if (loaded.Contains(assigned))
                    {
                        result.Add(new MonsterFSMCoverage
                        {
                            monsterKey = key,
                            source = FSMSetSource.Assignment,
                            setId = assigned,
                            note = "assignments.json",
                        });
                        continue;
                    }

                    result.Add(new MonsterFSMCoverage
                    {
                        monsterKey = key,
                        source = FSMSetSource.Unassigned,
                        setId = assigned,
                        note = $"assignments.json names '{assigned}', which no longer exists",
                    });
                    continue;
                }

                string hint = (def.fsmSet ?? "").Trim();
                if (hint.Length > 0 && loaded.Contains(hint))
                {
                    result.Add(new MonsterFSMCoverage
                    {
                        monsterKey = key,
                        source = FSMSetSource.DefinitionFallback,
                        setId = hint,
                        note = "MonsterDefinition.fsmSet",
                    });
                    continue;
                }

                result.Add(new MonsterFSMCoverage
                {
                    monsterKey = key,
                    source = FSMSetSource.Unassigned,
                    setId = "",
                    note = hint.Length == 0
                        ? "no by_archetype entry, no MonsterDefinition.fsmSet"
                        : $"MonsterDefinition.fsmSet '{hint}' names no loaded set",
                });
            }

            return result;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Self-resolves the catalog reference when the Inspector field was left empty.
        /// Same limitation as `EntitiesRuntimeEditor`/F3's spawner catalog (see
        /// ENTITIES_FSM_PVM_AUDIT.md dimension 2): Editor-only, so the picker is empty in a
        /// standalone build until there is a real injection seam. No file under
        /// `Gameplay/Editors/FSM/**` can add one without also touching
        /// `GameplaySceneSetup` (Bootstrap), which is out of scope here.
        /// </summary>
        private void ResolveMonsterCatalogIfNeeded()
        {
            if (_monsterCatalog != null) return;
            var guids = UnityEditor.AssetDatabase.FindAssets($"t:{nameof(Valkur.Data.MonsterCatalog)}");
            if (guids.Length == 0) return;
            var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
            _monsterCatalog = UnityEditor.AssetDatabase.LoadAssetAtPath<Valkur.Data.MonsterCatalog>(path);
        }
#else
        private void ResolveMonsterCatalogIfNeeded() { }
#endif

        // ── Data plumbing ─────────────────────────────────────────────────────

        private Dictionary<string, object> GetAssignmentCategoryDict()
            => GetAssignmentDict(_entitiesCategory);

        /// <summary>
        /// Named-category accessor. The coverage audit must read <c>by_archetype</c> even
        /// while the panel is showing <c>by_eid</c>, so it cannot go through the
        /// <see cref="_entitiesCategory"/>-bound overload without silently auditing the
        /// wrong key namespace.
        /// </summary>
        private Dictionary<string, object> GetAssignmentDict(string category)
        {
            if (_assignmentsRoot == null) return null;
            if (!_assignmentsRoot.TryGetValue(category, out var node) ||
                !(node is Dictionary<string, object> d))
            {
                d = new Dictionary<string, object>();
                _assignmentsRoot[category] = d;
            }
            return d;
        }

        private void CommitAssignment(string key, string value)
        {
            var d = GetAssignmentCategoryDict();
            if (d == null) return;
            value = (value ?? "").Trim();
            if (value.Length == 0) d.Remove(key);
            else                    d[key] = value;
            SaveAssignments();
            RefreshEntities();
            SetStatus("Assignments saved.");
        }
    }
}
