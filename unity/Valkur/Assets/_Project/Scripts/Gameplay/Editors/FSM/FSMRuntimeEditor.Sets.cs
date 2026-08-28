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
    /// Sets-panel mutations: Clone (C), Delete (X with confirm modal), New Set.
    /// Mirrors Python <c>fsm_sets_panel/sets_panel_clone</c> and <c>sets_panel_delete</c>.
    /// </summary>
    public partial class FSMRuntimeEditor : SingletonMonoBehaviour<FSMRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // Replaces the simple sets-list refresh in UI.cs with one that adds
        // per-row Clone (C) and Delete (X) buttons + a "+ New Set" header button.
        // Called from SelectSet/etc. (legacy RefreshSetsList kept for back-compat;
        // here we override its behavior by having it call this).
        private void RefreshSetsListInteractive()
        {
            if (_setsContent == null) return;
            for (int i = _setsContent.childCount - 1; i >= 0; i--)
                SafeDestroy.Of(_setsContent.GetChild(i).gameObject);

            // "+ New Set" header button
            var newBtn = EditorUIHelpers.MakeButton(_setsContent, "+ New Set",
                () => CreateNewSet(), 26f, 11f);
            if (newBtn != null) newBtn.GetComponent<Image>().color = new Color(0.20f, 0.35f, 0.20f, 0.95f);

            string filter = _searchFilter?.Trim().ToLowerInvariant() ?? "";
            int shown = 0;
            foreach (var set in _fsmSets)
            {
                var s = set;
                if (filter.Length > 0)
                {
                    string nm = ((set.label ?? set.id) ?? "").ToLowerInvariant();
                    if (!nm.Contains(filter) && !(set.id ?? "").ToLowerInvariant().Contains(filter)) continue;
                }
                shown++;
                BuildSetRow(s);
            }

            if (shown == 0)
            {
                EditorUIHelpers.AddLabel(_setsContent,
                    _fsmSets.Count == 0 ? "No FSM sets loaded." : $"No sets match '{_searchFilter}'.", 11f);
            }
        }

        private void BuildSetRow(FSMSetData set)
        {
            // Container row holding: [select-button | C | X]
            var row = new GameObject($"SetRow_{set.id}", typeof(RectTransform));
            row.transform.SetParent(_setsContent, false);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 26f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 2f; hlg.padding = new RectOffset(0, 0, 0, 0);
            hlg.childForceExpandHeight = true;
            hlg.childControlHeight = true;
            hlg.childControlWidth  = true;

            // Main select button. The "[seed]" suffix is the only place a designer can
            // see, without opening the set, that this set's STATE LIST auto-refreshes
            // from "Generate Seed from Runtime States" — everything else on it
            // (transitions, blackboard, labels) never does. See AUTO_GENERATED_FLAG_KEY.
            string rowLabel = set.label ?? set.id;
            bool isSeeded = IsSeedGeneratedSet(set);
            if (isSeeded) rowLabel += "  [seed]";
            var selectBtn = EditorUIHelpers.MakeButton(row.transform, rowLabel,
                () => SelectSet(set), 26f, 11f);
            if (set == _selectedSet) selectBtn.GetComponent<Image>().color = EditorUIHelpers.BTN_ACTIVE;
            else if (isSeeded) selectBtn.GetComponent<Image>().color = new Color(0.30f, 0.28f, 0.16f, 0.95f);
            var btnLE = selectBtn.GetComponent<LayoutElement>() ?? selectBtn.gameObject.AddComponent<LayoutElement>();
            btnLE.flexibleWidth = 1f;

            // Clone (C)
            var cloneBtn = EditorUIHelpers.MakeButton(row.transform, "C",
                () => CloneSet(set), 26f, 11f);
            cloneBtn.GetComponent<Image>().color = new Color(0.18f, 0.32f, 0.50f, 0.95f);
            var cLE = cloneBtn.GetComponent<LayoutElement>() ?? cloneBtn.gameObject.AddComponent<LayoutElement>();
            cLE.preferredWidth = 22f; cLE.flexibleWidth = 0f;

            // Delete (X)
            var delBtn = EditorUIHelpers.MakeButton(row.transform, "X",
                () => AskDeleteSet(set), 26f, 11f);
            delBtn.GetComponent<Image>().color = new Color(0.55f, 0.20f, 0.20f, 0.95f);
            var dLE = delBtn.GetComponent<LayoutElement>() ?? delBtn.gameObject.AddComponent<LayoutElement>();
            dLE.preferredWidth = 22f; dLE.flexibleWidth = 0f;
        }

        // ── Clone ────────────────────────────────────────────────────────────────

        private void CloneSet(FSMSetData src)
        {
            if (src == null) return;
            // Serialize raw, deep-clone, mutate id/label, append.
            var json = MiniJsonHelpersWrap.Serialize(src.raw);
            var copyRaw = MiniJsonHelpersWrap.Deserialize(json) as Dictionary<string, object>;
            if (copyRaw == null) return;
            var newId = NewId(src.id, CollectAllSetIds());
            copyRaw["id"] = newId;
            copyRaw["label"] = (src.label ?? src.id) + " (copy)";

            var prevSelectedId = _selectedSet?.id;

            // NOTE: every closure below re-fetches `(List<object>)_setsRoot["sets"]` rather
            // than closing over today's list instance — PersistSets REPLACES that list on
            // every call (`_setsRoot["sets"] = rawSets` with a freshly built list), so a
            // captured reference would silently go stale the moment any OTHER save happens
            // between this Do() and a later Undo()/Redo() of it.
            _undo.Do($"Clone set → '{newId}'",
                doAction: () =>
                {
                    RawSets().Add(copyRaw);
                    BuildTypedSetsFromRaw();
                    PersistSets();
                    _selectedSet = _fsmSets.FirstOrDefault(x => x.id == newId);
                    RefreshSetsListInteractive();
                    RefreshGraph();
                    RefreshProperties();
                },
                undoAction: () =>
                {
                    RawSets().RemoveAll(o => (o is Dictionary<string, object> d) &&
                                             System.Convert.ToString(d.ContainsKey("id") ? d["id"] : "") == newId);
                    BuildTypedSetsFromRaw();
                    PersistSets();
                    _selectedSet = prevSelectedId != null
                        ? _fsmSets.FirstOrDefault(x => x.id == prevSelectedId)
                        : null;
                    RefreshSetsListInteractive();
                    RefreshGraph();
                    RefreshProperties();
                });
            SetStatus($"Cloned → {newId}");
        }

        // ── Delete (with confirmation modal) ─────────────────────────────────────

        private void AskDeleteSet(FSMSetData set)
        {
            if (set == null) return;
            UIModal.Confirm(_canvas.transform,
                "Delete FSM Set",
                $"Delete set '{set.id}'?\nThis action cannot be undone.",
                () => DeleteSetConfirmed(set));
        }

        private void DeleteSetConfirmed(FSMSetData set)
        {
            int typedIdx = _fsmSets.IndexOf(set);
            if (typedIdx < 0) return;

            // Fallback selection if the deleted set was the active one — computed against
            // the list with `set` already excluded, matching what the old
            // `_fsmSets.RemoveAt(idx); … _fsmSets[Mathf.Clamp(idx, 0, _fsmSets.Count-1)]`
            // sequence picked, without relying on `_fsmSets` already being mutated.
            bool wasSelected = ReferenceEquals(_selectedSet, set);
            string fallbackId = null;
            if (wasSelected)
            {
                var remaining = _fsmSets.Where(s => !ReferenceEquals(s, set)).ToList();
                if (remaining.Count > 0)
                    fallbackId = remaining[Mathf.Clamp(typedIdx, 0, remaining.Count - 1)].id;
            }

            object savedLayoutEntry = null;
            bool hadLayoutEntry = _layoutsRoot != null &&
                                   AsDict(_layoutsRoot["by_set"]).TryGetValue(set.id, out savedLayoutEntry);

            _undo.Do($"Delete set '{set.id}'",
                doAction: () =>
                {
                    _fsmSets.Remove(set);
                    RawSets().RemoveAll(o => (o is Dictionary<string, object> d) &&
                                             System.Convert.ToString(d.ContainsKey("id") ? d["id"] : "") == set.id);
                    if (_layoutsRoot != null && _layoutsRoot["by_set"] is Dictionary<string, object> bySet)
                        bySet.Remove(set.id);
                    SaveLayouts();
                    PersistSets();

                    if (wasSelected)
                    {
                        _selectedSet = fallbackId != null ? _fsmSets.FirstOrDefault(x => x.id == fallbackId) : null;
                        _selectedState = null; _selectedTransition = null;
                    }
                    RefreshSetsListInteractive();
                    RefreshGraph();
                    RefreshProperties();
                },
                undoAction: () =>
                {
                    int insertAt = Mathf.Clamp(typedIdx, 0, _fsmSets.Count);
                    _fsmSets.Insert(insertAt, set);
                    if (hadLayoutEntry && _layoutsRoot != null && _layoutsRoot["by_set"] is Dictionary<string, object> bySet2)
                        bySet2[set.id] = savedLayoutEntry;
                    SaveLayouts();
                    PersistSets(); // rebuilds _setsRoot["sets"] from _fsmSets, restoring set.raw's slot
                    if (wasSelected) _selectedSet = set;
                    RefreshSetsListInteractive();
                    RefreshGraph();
                    RefreshProperties();
                });
            SetStatus($"Deleted '{set.id}'");
        }

        // ── New Set ──────────────────────────────────────────────────────────────

        private void CreateNewSet()
        {
            UIModal.Prompt(_canvas.transform, "New FSM Set", "NewSet",
                rawId =>
                {
                    var name = (rawId ?? "").Trim();
                    if (string.IsNullOrEmpty(name)) return;
                    var newId = name;
                    if (CollectAllSetIds().Contains(newId))
                        newId = NewId(name, CollectAllSetIds());
                    var newRaw = new Dictionary<string, object>
                    {
                        { "id", newId },
                        { "label", name },
                        { "initial", "Idle" },
                        { "states", new List<object>
                            {
                                new Dictionary<string, object>
                                {
                                    { "id", "Idle" }, { "label", "Idle" },
                                    { "class", "IdleState" }, { "props", new Dictionary<string, object>() },
                                    { "terminal", false },
                                }
                            }
                        },
                        { "transitions", new List<object>() },
                    };
                    var prevSelectedId = _selectedSet?.id;

                    _undo.Do($"New set '{newId}'",
                        doAction: () =>
                        {
                            RawSets().Add(newRaw);
                            BuildTypedSetsFromRaw();
                            PersistSets();
                            _selectedSet = _fsmSets.FirstOrDefault(x => x.id == newId);
                            RefreshSetsListInteractive();
                            RefreshGraph();
                            RefreshProperties();
                        },
                        undoAction: () =>
                        {
                            RawSets().RemoveAll(o => (o is Dictionary<string, object> d) &&
                                                     System.Convert.ToString(d.ContainsKey("id") ? d["id"] : "") == newId);
                            BuildTypedSetsFromRaw();
                            PersistSets();
                            _selectedSet = prevSelectedId != null
                                ? _fsmSets.FirstOrDefault(x => x.id == prevSelectedId)
                                : null;
                            RefreshSetsListInteractive();
                            RefreshGraph();
                            RefreshProperties();
                        });
                    SetStatus($"Created '{newId}'");
                });
        }

        /// <summary>Always re-fetches the live raw sets list — see the note in
        /// <see cref="CloneSet"/> about why undo/redo closures must not cache it.</summary>
        private List<object> RawSets()
        {
            if (_setsRoot == null) _setsRoot = new Dictionary<string, object> { { "version", 1L }, { "sets", new List<object>() } };
            if (!(_setsRoot["sets"] is List<object> list))
            {
                list = new List<object>();
                _setsRoot["sets"] = list;
            }
            return list;
        }

        // ── MiniJson assembly bridge ────────────────────────────────────────────
        // The runtime MiniJsonRuntime now provides Serialize too; this internal
        // alias keeps the call sites short and avoids a wide using-imports change.
        private static class MiniJsonHelpersWrap
        {
            public static string Serialize(object o) => Valkur.Gameplay.World.MiniJsonRuntime.Serialize(o);
            public static object Deserialize(string s) => Valkur.Gameplay.World.MiniJsonRuntime.Deserialize(s);
        }
    }
}
