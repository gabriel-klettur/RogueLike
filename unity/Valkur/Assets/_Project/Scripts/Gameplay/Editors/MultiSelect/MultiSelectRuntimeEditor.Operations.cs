using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.MultiSelect
{
    /// <summary>
    /// The group operations — delete, duplicate — plus the undo stack they share and the
    /// outline pass that draws the result.
    ///
    /// <para>EVERY OPERATION IS ONE UNDO ENTRY AND ONE SAVE PER TOUCHED FILE, in that order:
    /// build the whole change in memory, then persist. The order is <c>CraftingService</c>'s
    /// and it is here for the same reason — a delete that removed six things and then failed
    /// to write two of the three files would leave a lamppost with a light and no post, and
    /// nothing on screen would say so.</para>
    /// </summary>
    public sealed partial class MultiSelectRuntimeEditor
    {
        private readonly Dictionary<GameObject, SelectionBoxOutline> _outlines =
            new Dictionary<GameObject, SelectionBoxOutline>(64);
        private Transform _outlineRoot;

        // ── The one place a group change is recorded ───────────────────────────

        /// <summary>
        /// Run <paramref name="doAction"/>, record it with its inverse, and save every domain
        /// the selection touches — on the way there AND on the way back, because an undo is a
        /// change to the same three files.
        /// </summary>
        private void RecordAndRun(string label, Action doAction, Action undoAction, bool deletion)
        {
            // The domains are captured BEFORE the action: a delete empties the selection, so
            // asking afterwards which files to write would answer "none".
            var touched = TouchedDomains();

            _undo.Do(label,
                () => { doAction?.Invoke(); PersistDomains(touched, deletion); },
                () => { undoAction?.Invoke(); PersistDomains(touched, deletion); });
        }

        private List<ISelectionDomain> TouchedDomains()
        {
            var list = new List<ISelectionDomain>(_domains.Length);
            var items = _selection.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var d = items[i].Domain;
                if (d != null && !list.Contains(d)) list.Add(d);
            }
            return list;
        }

        private static void PersistDomains(List<ISelectionDomain> domains, bool deletion)
        {
            if (domains == null) return;
            for (int i = 0; i < domains.Count; i++)
            {
                try { domains[i].Persist(deletion); }
                catch (Exception ex)
                {
                    // One domain refusing its write (an interior, an anti-wipe guard) must not
                    // abandon the other two mid-way: the group is already changed in the scene,
                    // so the best remaining outcome is that every file that CAN take it does.
                    Debug.LogError($"[Selection] Save failed for '{domains[i].Id}': {ex.Message}");
                }
            }
        }

        // ── Delete ─────────────────────────────────────────────────────────────

        // ── Delete arming ──────────────────────────────────────────────────────
        //
        // Every other delete in the project asks first: the Buildings editor confirms one
        // building, the Particles editor asks TWICE to empty a zone. This one takes 23 things
        // out of three files at once, and it was the only delete that did not ask.
        //
        // A two-click ARM rather than a modal, which is the shape the quest Abandon button
        // already uses: it costs no dialog, and — unlike a modal — it covers the Supr key and
        // the button with one mechanism. It disarms on anything that makes the second press
        // mean something different from the first: a changed selection, a closed editor, or
        // simply time passing, because coming back to a primed red button with no memory of
        // having pressed it is one click away from losing the work.

        internal const string DELETE_LABEL  = "Borrar (Supr)";
        private  const float  ARM_SECONDS   = 4f;

        private bool  _deleteArmed;
        private float _deleteArmedAt;
        private int   _deleteArmedCount;

        /// <summary>True while the next Borrar press would actually delete. Read by the tests.</summary>
        internal bool DeleteArmed => _deleteArmed && Time.unscaledTime - _deleteArmedAt <= ARM_SECONDS;

        private void DisarmDelete(bool repaint = true)
        {
            if (!_deleteArmed) return;
            _deleteArmed = false;
            if (repaint) RefreshDeleteButton();
        }

        private void RefreshDeleteButton()
        {
            if (_deleteTmp != null)
                _deleteTmp.text = DeleteArmed
                    ? $"Confirmar borrado de {_deleteArmedCount}"
                    : DELETE_LABEL;
            if (_deleteBtn != null)
                UIButton.SetTint(_deleteBtn, DeleteArmed ? UITheme.DANGER : UITheme.DANGER_IDLE);
        }

        private void DeleteSelection()
        {
            _selection.Prune();
            if (_selection.Count == 0)
            {
                DisarmDelete();
                SetStatus("No hay nada seleccionado.");
                return;
            }

            // First press ARMS. The count is captured with it, so a selection that changed
            // between the two presses disarms rather than deleting something else.
            if (!DeleteArmed || _deleteArmedCount != _selection.Count)
            {
                _deleteArmed      = true;
                _deleteArmedAt    = Time.unscaledTime;
                _deleteArmedCount = _selection.Count;
                RefreshDeleteButton();
                SetStatus($"Vas a borrar {Elements(_selection.Count)} ({DescribeSelectionByDomain()}). " +
                          "Pulsa otra vez para confirmar.");
                return;
            }

            DisarmDelete(repaint: false);

            var items = new List<SelectionItem>(_selection.Items);
            var tokens = new object[items.Count];
            string summary = DescribeSelectionByDomain();

            RecordAndRun($"Borrar {items.Count}",
                () =>
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (!items[i].IsAlive) continue;
                        tokens[i] = items[i].Domain.Delete(items[i].Go);
                    }
                },
                () =>
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (tokens[i] == null) continue;
                        var back = items[i].Domain.Restore(tokens[i]);
                        // A restored EMITTER is a new GameObject — its domain destroys rather
                        // than deactivates — so the entry has to be re-pointed or the set is
                        // left holding a destroyed reference that Prune then silently drops.
                        if (back != null)
                        {
                            _selection.Repoint(items[i].Go, back);
                            items[i] = new SelectionItem(items[i].Domain, back);
                        }
                    }
                    RefreshPanel();
                },
                deletion: true);

            _selection.Clear();
            HideAllOutlines();
            RefreshPanel();
            RefreshDeleteButton();
            SetStatus($"Borrados {Elements(items.Count)} ({summary}). Ctrl+Z para deshacer.");
        }

        // ── Duplicate ──────────────────────────────────────────────────────────

        /// <summary>How far a duplicate lands from its original, in world units. Enough to be
        /// visibly a second object rather than a copy hidden exactly behind the first.</summary>
        private const float DUPLICATE_OFFSET = 1f;

        private void DuplicateSelection()
        {
            _selection.Prune();
            if (_selection.Count == 0) { SetStatus("No hay nada seleccionado."); return; }

            var sources = new List<SelectionItem>(_selection.Items);
            var copies  = new List<SelectionItem>(sources.Count);
            var offset  = new Vector3(DUPLICATE_OFFSET, -DUPLICATE_OFFSET, 0f);

            RecordAndRun($"Duplicar {sources.Count}",
                () =>
                {
                    copies.Clear();
                    for (int i = 0; i < sources.Count; i++)
                    {
                        var s = sources[i];
                        if (!s.IsAlive) continue;
                        var copy = s.Domain.Duplicate(s.Go, s.Domain.PositionOf(s.Go) + offset);
                        if (copy != null) copies.Add(new SelectionItem(s.Domain, copy));
                    }

                    // The COPIES become the selection, so a second duplicate or a drag acts on
                    // what just arrived rather than on what it came from — the same choice the
                    // Buildings clipboard's paste makes.
                    _selection.Clear();
                    for (int i = 0; i < copies.Count; i++) _selection.Add(copies[i].Domain, copies[i].Go);
                    RefreshPanel();
                },
                () =>
                {
                    for (int i = 0; i < copies.Count; i++)
                        if (copies[i].IsAlive) copies[i].Domain.DiscardDuplicate(copies[i].Go);
                    copies.Clear();
                    _selection.Clear();
                    RefreshPanel();
                },
                deletion: false);

            SetStatus(copies.Count == 0
                ? "No se pudo duplicar nada."
                : $"Duplicados {Elements(copies.Count)} ({DescribeSelectionByDomain()}). Ctrl+Z para deshacer.");
        }

        // ── Undo / redo / save ─────────────────────────────────────────────────

        private void DoUndo()
        {
            if (!_undo.CanUndo) { SetStatus("Nada que deshacer."); return; }
            string label = _undo.PeekUndoLabel();
            _undo.Undo();
            _selection.Prune();
            RefreshPanel();
            SetStatus($"Deshecho: {label}");
        }

        private void DoRedo()
        {
            if (!_undo.CanRedo) { SetStatus("Nada que rehacer."); return; }
            _undo.Redo();
            _selection.Prune();
            RefreshPanel();
            SetStatus("Rehecho.");
        }

        /// <summary>Ctrl+S. Writes every domain that has anything selected; with an empty
        /// selection it writes all three, which is the honest reading of "save my work".</summary>
        private void SaveTouchedDomains(bool deletion, string message)
        {
            var touched = TouchedDomains();
            if (touched.Count == 0)
                for (int i = 0; i < _domains.Length; i++)
                    if (_domains[i].Available) touched.Add(_domains[i]);

            PersistDomains(touched, deletion);
            SetStatus(message);
        }

        private void ClearSelection()
        {
            _selection.Clear();
            HideAllOutlines();
            RefreshPanel();
            SetStatus("Seleccion vacia.");
        }

        // ── Outlines ───────────────────────────────────────────────────────────

        private void DrawOutlines()
        {
            EnsureOutlineRoot();

            var items = _selection.Items;
            var primary = _selection.Primary;

            // Hide first, then draw what is live. Cheaper than reconciling two collections,
            // and it means a member destroyed since the last frame simply stops being drawn.
            foreach (var kv in _outlines) kv.Value.Hide();

            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (!it.IsAlive || !it.Domain.TryGetRect(it.Go, out var rect)) continue;
                OutlineFor(it.Go).Draw(rect, it.Domain.Tint, it.Go == primary.Go);
            }
        }

        private SelectionBoxOutline OutlineFor(GameObject go)
        {
            if (_outlines.TryGetValue(go, out var existing) && existing != null) return existing;

            var host = new GameObject("SelectionOutline");
            host.transform.SetParent(_outlineRoot, false);
            var outline = host.AddComponent<SelectionBoxOutline>();
            _outlines[go] = outline;
            return outline;
        }

        private void EnsureOutlineRoot()
        {
            if (_outlineRoot != null) return;
            var go = new GameObject("[SelectionOutlines]");
            go.transform.SetParent(transform, false);
            _outlineRoot = go.transform;
        }

        private void HideAllOutlines()
        {
            foreach (var kv in _outlines)
                if (kv.Value != null) kv.Value.Hide();
        }

        // ── Status text ────────────────────────────────────────────────────────

        /// <summary>"3 edificios, 1 luz" — the one line that says a group spans three files,
        /// which is the whole reason this tool exists and is otherwise invisible.</summary>
        private string DescribeSelectionByDomain()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _domains.Length; i++)
            {
                int n = _selection.CountIn(_domains[i].Id);
                if (n == 0) continue;
                if (sb.Length > 0) sb.Append(", ");
                // Singular when there is one of them: "1 edificios" is the kind of line that
                // makes an author distrust the rest of the readout.
                sb.Append(n).Append(' ')
                  .Append(n == 1 ? _domains[i].LabelSingular : _domains[i].Label.ToLowerInvariant());
            }
            return sb.Length == 0 ? "nada" : sb.ToString();
        }

        /// <summary>"1 elemento" / "24 elementos". The "(s)" spelling was the same shrug as
        /// "1 edificios" and reads as a line nobody proofread.</summary>
        private static string Elements(int n) => n == 1 ? "1 elemento" : n + " elementos";


    }
}
