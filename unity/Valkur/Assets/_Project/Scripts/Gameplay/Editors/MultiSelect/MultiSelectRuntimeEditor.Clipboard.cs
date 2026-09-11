using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Valkur.Core.Input;

namespace Valkur.Gameplay.Editors.MultiSelect
{
    /// <summary>
    /// Ctrl+C / Ctrl+V across the three domains, and the ghost that shows where the paste will
    /// land.
    ///
    /// <para>THE CLIPBOARD HOLDS SNAPSHOTS, NOT THE SOURCE OBJECTS — the rule the Buildings
    /// clipboard already records, and it matters more here because a clipboard genuinely
    /// outlives its source: copy a lamppost, delete it, paste it back. A live reference would
    /// paste whatever the source had become, or throw once it was gone.</para>
    ///
    /// <para>A GROUP IS STORED AS OFFSETS FROM ITS ANCHOR, and the anchor is the PRIMARY — the
    /// last thing picked, matching <see cref="MultiSelectSet.Primary"/>. Absolute positions
    /// would make every paste land on top of the originals.</para>
    ///
    /// <para>THE GHOST AND THE PASTE SHARE ONE ARITHMETIC. Both put the anchor at the pointer
    /// and every other piece at its stored offset, so the preview is not an approximation of
    /// the result — it is the same calculation drawn early. That is what lets this paste carry
    /// no correction of its own: the Buildings clipboard nudges its paste up by half the
    /// anchor's drawn height so the SPRITE centres on the cursor, which is a sensible guess
    /// when nothing on screen can be checked against it, and a discrepancy the moment a ghost
    /// can.</para>
    /// </summary>
    public sealed partial class MultiSelectRuntimeEditor
    {
        /// <summary>One copied placement: which domain owns it, its by-value snapshot, and
        /// where it sits relative to the group's anchor.</summary>
        private struct ClipboardEntry
        {
            public ISelectionDomain Domain;
            public object           Token;
            public Vector3          Offset;
        }

        private readonly List<ClipboardEntry> _clipboard = new List<ClipboardEntry>(32);
        private SelectionGhost _ghost;

        /// <summary>True once something has been copied. Read by the panel and by the tests, so
        /// "is there anything to paste" has one answer.</summary>
        internal bool HasClipboard => _clipboard.Count > 0;

        internal int ClipboardCount => _clipboard.Count;

        // ── The frame ──────────────────────────────────────────────────────────

        /// <summary>
        /// Ctrl+C and Ctrl+V, plus keeping the ghost under the pointer.
        ///
        /// <para>Both are this editor's OWN Ctrl tools, declared in <c>Editor.Selection</c>
        /// with <c>requiresCtrl</c>, exactly as the Tile and Buildings clipboards are. Three
        /// editors now bind Ctrl+C on the same key and that is not a conflict — each is live
        /// only inside its own context, which is the property that gives every editor a whole
        /// keyboard.</para>
        /// </summary>
        private void TickClipboard()
        {
            if (EditorInput.Tool(InputActionCatalog.MapSelectionEditor, "Copy"))  { CopySelection(); return; }
            if (EditorInput.Tool(InputActionCatalog.MapSelectionEditor, "Paste")) { PasteAtGhost();  return; }

            TickGhost();
        }

        private void TickGhost()
        {
            if (_ghost == null || !_ghost.HasPieces) return;

            // Hidden over the panel, because a paste there is refused: a ghost hovering on a
            // button would be promising a placement the tool will not make.
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            _ghost.SetVisible(!overUi);
            if (!overUi) _ghost.MoveTo(PointerWorld());
        }

        // ── Copy ───────────────────────────────────────────────────────────────

        private void CopySelection()
        {
            _selection.Prune();
            if (_selection.Count == 0) { SetStatus("No hay nada que copiar."); return; }

            var items  = _selection.Items;
            var anchor = _selection.Primary;
            if (!anchor.IsAlive) { SetStatus("No hay nada que copiar."); return; }
            Vector3 anchorPos = anchor.Domain.PositionOf(anchor.Go);

            _clipboard.Clear();
            var ghostPieces = new List<SelectionGhost.Piece>(items.Count * 2);
            var spriteBuf   = new List<SpriteRenderer>(4);

            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (!it.IsAlive) continue;

                var token = it.Domain.CaptureForClipboard(it.Go);
                if (token == null) continue;   // a member this domain cannot snapshot is skipped, not fatal

                Vector3 offset = it.Domain.PositionOf(it.Go) - anchorPos;
                _clipboard.Add(new ClipboardEntry { Domain = it.Domain, Token = token, Offset = offset });
                AppendGhostPieces(it, offset, ghostPieces, spriteBuf);
            }

            if (_clipboard.Count == 0) { SetStatus("No se pudo copiar nada."); return; }

            EnsureGhost();
            _ghost.Build(ghostPieces);
            _ghost.SetVisible(true);

            RefreshPanel();
            SetStatus($"Copiados {Elements(_clipboard.Count)} ({DescribeSelectionByDomain()}). " +
                      "Ctrl+V pega donde este la sombra.");
        }

        /// <summary>
        /// The drawn pieces for one clipboard member. A domain with sprites contributes them at
        /// their real world offsets and scales; one without contributes a single translucent
        /// box the size of its own pick rect, which is the only thing there is to preview for a
        /// light or an emitter.
        /// </summary>
        private void AppendGhostPieces(SelectionItem item, Vector3 offset,
                                       List<SelectionGhost.Piece> outPieces,
                                       List<SpriteRenderer> spriteBuf)
        {
            spriteBuf.Clear();
            item.Domain.CollectGhostSprites(item.Go, spriteBuf);

            int drawn = 0;
            for (int i = 0; i < spriteBuf.Count; i++)
            {
                var sr = spriteBuf[i];
                if (sr == null || sr.sprite == null || !sr.enabled) continue;
                outPieces.Add(new SelectionGhost.Piece
                {
                    Sprite = sr.sprite,
                    // The renderer's own world position relative to the anchor, so a building's
                    // canopy keeps sitting above its footprint in the ghost.
                    Offset = offset + (sr.transform.position - item.Domain.PositionOf(item.Go)),
                    Scale  = sr.transform.lossyScale,
                    Tint   = Color.white,
                    Order  = i,
                });
                drawn++;
            }
            if (drawn > 0) return;

            if (!item.Domain.TryGetRect(item.Go, out var rect)) return;
            Vector3 pos = item.Domain.PositionOf(item.Go);
            outPieces.Add(new SelectionGhost.Piece
            {
                Sprite = null,
                Offset = offset + new Vector3(rect.center.x - pos.x, rect.center.y - pos.y, 0f),
                Scale  = new Vector3(Mathf.Max(0.05f, rect.width), Mathf.Max(0.05f, rect.height), 1f),
                Tint   = item.Domain.Tint,
                Order  = 0,
            });
        }

        // ── Paste ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Paste the clipboard with its anchor where the ghost is — which is the pointer.
        ///
        /// <para>Refused while the pointer is over the panel, because the only honest answer
        /// there is "not here": the cursor's world projection is behind the UI, where the
        /// author cannot see what arrived and would press Ctrl+V again.</para>
        /// </summary>
        private void PasteAtGhost()
        {
            if (!HasClipboard) { SetStatus("Portapapeles vacio - copia algo con Ctrl+C."); return; }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                SetStatus("Mueve el puntero sobre el mapa para pegar.");
                return;
            }

            Vector3 anchorPos = PointerWorld();
            var entries = new List<ClipboardEntry>(_clipboard);
            var created = new List<SelectionItem>(entries.Count);

            // The clipboard's domains, not the selection's: a paste writes the files the COPY
            // came from, and the selection is about to be replaced by what arrives.
            var touched = new List<ISelectionDomain>(_domains.Length);
            for (int i = 0; i < entries.Count; i++)
                if (!touched.Contains(entries[i].Domain)) touched.Add(entries[i].Domain);

            _undo.Do($"Pegar {entries.Count}",
                () =>
                {
                    created.Clear();
                    for (int i = 0; i < entries.Count; i++)
                    {
                        var e = entries[i];
                        var go = e.Domain.SpawnFromClipboard(e.Token, anchorPos + e.Offset);
                        if (go != null) created.Add(new SelectionItem(e.Domain, go));
                    }

                    // What just arrived becomes the selection, so a drag or a second Ctrl+V
                    // acts on the copy rather than on what it came from — the same choice the
                    // Buildings clipboard's paste makes.
                    _selection.Clear();
                    for (int i = 0; i < created.Count; i++) _selection.Add(created[i].Domain, created[i].Go);
                    RefreshPanel();
                    PersistDomains(touched, deletion: false);
                },
                () =>
                {
                    for (int i = 0; i < created.Count; i++)
                        if (created[i].IsAlive) created[i].Domain.DiscardDuplicate(created[i].Go);
                    created.Clear();
                    _selection.Clear();
                    RefreshPanel();
                    PersistDomains(touched, deletion: true);
                });

            SetStatus(created.Count == 0
                ? "No se pudo pegar nada."
                : $"Pegados {Elements(created.Count)} en ({anchorPos.x:F1}, {anchorPos.y:F1}). " +
                  "Ctrl+Z para deshacer.");
        }

        // ── The ghost's lifetime ───────────────────────────────────────────────

        private void EnsureGhost()
        {
            if (_ghost != null) return;
            var go = new GameObject("[SelectionGhost]");
            go.transform.SetParent(transform, false);
            _ghost = go.AddComponent<SelectionGhost>();
        }

        /// <summary>Drop the clipboard and its ghost. The ghost is a promise about the next
        /// Ctrl+V, so it must not outlive the editor being open — a translucent lamppost
        /// following the pointer through the game would be a promise nothing can keep.</summary>
        private void ClearClipboard(bool announce)
        {
            _clipboard.Clear();
            if (_ghost != null) { _ghost.Clear(); _ghost.SetVisible(false); }
            RefreshPanel();
            if (announce) SetStatus("Portapapeles vaciado.");
        }

        /// <summary>Hide the ghost without forgetting what is on the clipboard, so reopening
        /// the editor still has something to paste.</summary>
        private void HideGhost()
        {
            if (_ghost != null) _ghost.SetVisible(false);
        }
    }
}
