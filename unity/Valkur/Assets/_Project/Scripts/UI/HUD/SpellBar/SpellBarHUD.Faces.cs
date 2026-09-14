using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Core.Input;

namespace Valkur.UI.HUD
{
    public sealed partial class SpellBarHUD
    {
        private enum FlipPhase
        {
            None = 0,
            Out = 1,
            In = 2,
        }

        /// <summary>One drawn slot: its meaning, the box the flip turns, and the slot itself.</summary>
        private sealed class Cell
        {
            public SpellBarEntry Entry;
            public RectTransform Box;
            public HudAbilitySlot Slot;
            public Vector2 Centre;
        }

        private readonly List<Cell> _cells = new List<Cell>();
        private readonly List<int> _grooves = new List<int>();
        private Stance _face;
        private string _signature = "";
        private float _pollLeft;
        private bool _everPopulated;

        private FlipPhase _flip;
        private float _flipT;
        private Stance _flipTarget;

        /// <summary>True while the War face shows the keyboard's Shift layer.</summary>
        private bool _shiftPage;
        private bool _flipPage;
        /// <summary>True when the running turn is a PAGE turn (Shift pressed or released) rather
        /// than a posture change: shorter, and silent apart from the gem.</summary>
        private bool _flipIsPage;

        /// <summary>Spells of the War page on screen that did not fit the earned bar.</summary>
        private int _overflow;

        // -- Which face, which slots -----------------------------------------------------

        private List<SpellBarEntry> EntriesFor(Stance face) => EntriesFor(face, _shiftPage);

        /// <summary>
        /// The War face is two PAGES of one keyboard: the bare keys, and the Shift layer while
        /// Shift is held. Showing both at once put up to seventy slots in seven rows over the
        /// world, and a spell's key cap alone could not say which layer it sat on; a page per
        /// layer keeps the bar the size of what the player's fingers can reach right now, and
        /// turning it on Shift is what TEACHES the second layer exists.
        /// </summary>
        private List<SpellBarEntry> EntriesFor(Stance face, bool shiftPage)
        {
            _overflow = 0;
            if (face == Stance.Peace) return SpellBarModel.Peace(VerbExists);
            var page = SpellBarModel.War(InputActionCatalog.Spells(),
                                         key => _caster != null && _caster.KnowsSpell(key),
                                         _style.warGroupSize,
                                         onPage: d => IsShiftSlot(d) == shiftPage);

            // The bar is as big as the character has EARNED in the "Barra de Guerra" branch:
            // columns x rows sockets, filled with the page's spells and then with empty sockets,
            // so buying a size is something the player SEES. A rig with no stat store (a bare
            // test) keeps the unbounded bar.
            if (_stats == null) return page;
            return SpellBarModel.Fit(page, _stats.WarBarColumns * _stats.WarBarRows,
                                     _style.warGroupSize, out _overflow);
        }

        /// <summary>Sockets per row: the earned columns on the War face, the style's limit otherwise.</summary>
        private int SlotsPerRow()
        {
            int limit = Mathf.Max(1, _style.maxSlotsPerRow);
            if (_face != Stance.War || _stats == null) return limit;
            return Mathf.Clamp(_stats.WarBarColumns, 1, limit);
        }

        /// <summary>True when the spell action's live binding is a Shift chord.</summary>
        private static bool IsShiftSlot(InputActionDescriptor d)
        {
            var action = ResolveAction(d != null ? d.Id : null);
            return action != null && InputBindingResolver.Primary(action).IsChord;
        }

        /// <summary>Is the chord layer's modifier held? Read off the chords in the asset, so the
        /// page follows the layer wherever it is bound.</summary>
        private static bool ShiftLayerHeld()
        {
            var gameplay = InputService.Instance != null ? InputService.Instance.Gameplay : null;
            return gameplay != null && gameplay.Map != null
                && InputBindingResolver.IsAnyChordModifierHeld(gameplay.Map);
        }

        /// <summary>
        /// The posture is read every frame, so a flip starts on the frame Tab went down. What
        /// the face CONTAINS is re-read four times a second — a spell learned, a key silenced in
        /// the Controls editor, a panel that finished building — which is fast enough to read as
        /// immediate and cheap enough to ignore.
        /// </summary>
        private void PollFace(float dt)
        {
            var stance = PlayerStance.Current;
            bool shift = stance == Stance.War && ShiftLayerHeld();
            if (_flip != FlipPhase.None)
            {
                // Toggled again mid-turn: the turn simply lands on whatever is current. A posture
                // change arriving during a page turn upgrades it, so the posture still announces.
                if (stance != _flipTarget && _flipIsPage) _flipIsPage = false;
                _flipTarget = stance;
                _flipPage = shift;
                return;
            }
            if (stance != _face)
            {
                BeginFlip(stance, shift, page: false);
                return;
            }
            if (shift != _shiftPage)
            {
                BeginFlip(stance, shift, page: true);
                return;
            }

            _pollLeft -= dt;
            if (_pollLeft > 0f) return;
            _pollLeft = PollSeconds;
            var entries = EntriesFor(_face);
            if (SpellBarModel.Signature(_face, entries) != _signature)
                Rebuild(entries, celebrate: _everPopulated);
            else if (_overflow != _shownOverflow)
                RefreshOverflow();   // a spell learned past the earned size changes no socket
        }

        private int _shownOverflow = -1;

        /// <summary>Rebuilds the slots of the current face. <paramref name="celebrate"/> lights up
        /// spells that were not on the bar before — a spell just learned.</summary>
        private void Rebuild(List<SpellBarEntry> entries, bool celebrate)
        {
            HashSet<string> before = null;
            if (celebrate)
            {
                before = new HashSet<string>();
                for (int i = 0; i < _cells.Count; i++) before.Add(_cells[i].Entry.Key);
            }
            Stance faceBefore = _laidOutFace;
            int socketsBefore = SocketCount();

            Layout(entries);
            _signature = SpellBarModel.Signature(_face, entries);
            _everPopulated = true;
            _laidOutFace = _face;
            RefreshOverflow();

            if (before == null || _face != Stance.War) return;

            for (int i = 0; i < _cells.Count; i++)
            {
                var c = _cells[i];
                if (c.Entry.Kind != SpellBarEntryKind.Spell || before.Contains(c.Entry.Key)) continue;
                c.Slot.Flash(_hud);
                BurstLearn(c);
            }

            // The bar GREW on the same face: a size bought in the "Barra de Guerra" branch. The
            // new sockets are the reward, so they are the ones that light up — the talent's effect
            // happens where the player is already looking.
            int sockets = SocketCount();
            if (faceBefore == Stance.War && socketsBefore > 0 && sockets > socketsBefore)
                for (int i = 0, seen = 0; i < _cells.Count; i++)
                {
                    if (_cells[i].Entry.Kind == SpellBarEntryKind.Stance) continue;
                    if (seen++ < socketsBefore) continue;
                    _cells[i].Slot.Flash(_hud);
                    BurstLearn(_cells[i]);
                }
        }

        private Stance _laidOutFace = Stance.Peace;

        /// <summary>Every slot but the posture switch: the sockets the earned size pays for.</summary>
        private int SocketCount()
        {
            int n = 0;
            for (int i = 0; i < _cells.Count; i++)
                if (_cells[i].Entry.Kind != SpellBarEntryKind.Stance) n++;
            return n;
        }

        /// <summary>
        /// "+3" over the bar's right shoulder when the page holds more known spells than the
        /// earned bar has sockets for. Dim, not gold: it is a fact worth noticing, not an alarm —
        /// the spells still cast from their keys — and it is the nudge towards the talents branch.
        /// </summary>
        private void RefreshOverflow()
        {
            if (_overflowLabel == null) return;
            _shownOverflow = _overflow;
            bool show = _face == Stance.War && _overflow > 0;
            _overflowLabel.gameObject.SetActive(show);
            if (!show) return;
            _overflowLabel.SetText("+" + _overflow);
            HudRect.Place(_overflowLabel.rectTransform, _widthTexels - 22, _heightTexels - 8, 18, 7);
        }

        // -- Layout --------------------------------------------------------------------------

        private void ClearCells()
        {
            for (int i = 0; i < _cells.Count; i++)
                if (_cells[i].Box != null) HudLifetime.Release(_cells[i].Box.gameObject);
            _cells.Clear();
        }

        /// <summary>
        /// Places the face: rows of at most <c>maxSlotsPerRow</c>, the first at the bottom, a
        /// wider gap where the group changes, the posture switch at the right end of the bottom
        /// row. Every coordinate is a whole texel; each slot's box is pivoted on its centre (an
        /// even edge makes that a whole texel too) so the flip turns it in place.
        /// </summary>
        private void Layout(List<SpellBarEntry> entries)
        {
            HideTooltip();
            ClearCells();
            _grooves.Clear();

            int size = _style.SlotTexels;
            int gap = _style.slotGapTexels;
            int groupGap = _style.groupGapTexels;
            int pad = _style.paddingTexels;
            // The top band clears the gem BakePanel sets into the top edge (its diamond reaches
            // six texels down), and the padding clears the panel stone's corner rivets, which sit
            // four and five texels in — with less, a gold rivet peeks out from under a slot.
            int top = Mathf.Max(pad + 1, GemClearance);
            int perRow = SlotsPerRow();

            var body = new List<SpellBarEntry>();
            SpellBarEntry? stance = null;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Kind == SpellBarEntryKind.Stance) stance = entries[i];
                else body.Add(entries[i]);
            }

            // The EARNED War bar is a GRID, and a grid has to line up. Laid out like the Peace
            // face — each row centred on its own, groups counted by list index — a bar of three
            // rows had its columns wander half a slot between rows and its grooves fall in a
            // different place on every row, which reads as three bars stacked, not one.
            if (_face == Stance.War && _stats != null)
            {
                LayoutGrid(body, stance, size, gap, groupGap, pad, top, perRow);
                return;
            }

            int rowCount = Mathf.Max(1, Mathf.CeilToInt(body.Count / (float)perRow));
            var rows = new List<List<SpellBarEntry>>(rowCount);
            for (int r = 0; r < rowCount; r++)
            {
                int from = r * perRow;
                int n = Mathf.Clamp(body.Count - from, 0, perRow);
                rows.Add(body.GetRange(from, n));
            }
            if (stance.HasValue) rows[0].Add(stance.Value);

            // Each row's x positions and width, before the bar's width is known.
            var xs = new List<List<int>>(rowCount);
            int width = 0;
            for (int r = 0; r < rowCount; r++)
            {
                var row = rows[r];
                var rowXs = new List<int>(row.Count);
                int x = pad;
                for (int i = 0; i < row.Count; i++)
                {
                    if (i > 0)
                    {
                        bool newGroup = row[i].Group != row[i - 1].Group;
                        if (newGroup && r == 0 && rowCount == 1)
                            _grooves.Add(x + (gap + groupGap) / 2 - 1);
                        x += gap + (newGroup ? groupGap : 0);
                    }
                    rowXs.Add(x);
                    x += size;
                }
                xs.Add(rowXs);
                width = Mathf.Max(width, x + pad);
            }

            _widthTexels = Mathf.Max(width, pad * 2 + size);
            _heightTexels = pad + rowCount * size + (rowCount - 1) * gap + top;

            int index = 0;
            for (int r = 0; r < rowCount; r++)
            {
                var row = rows[r];
                int rowWidth = row.Count > 0 ? xs[r][row.Count - 1] + size + pad : 0;
                int offset = (_widthTexels - rowWidth) / 2;
                int y = pad + r * (size + gap);
                for (int i = 0; i < row.Count; i++)
                    _cells.Add(MakeCell(row[i], index++, xs[r][i] + offset, y, size));
            }

            ResizeFrame();
            Refit(force: true);
        }

        /// <summary>
        /// The War face as the grid the "Barra de Guerra" talents bought: every row shares the
        /// same column positions, a group opens every <c>warGroupSize</c> COLUMNS (so the grooves
        /// stack into one line down the whole bar), the first row is at the bottom — the number
        /// row, nearest the player's panel — and the posture switch stands one group gap to the
        /// right of that bottom row, outside the grid, because it is not one of the sockets the
        /// talents paid for.
        /// </summary>
        private void LayoutGrid(List<SpellBarEntry> body, SpellBarEntry? stance, int size, int gap,
                                int groupGap, int pad, int top, int perRow)
        {
            int groupSize = Mathf.Max(1, _style.warGroupSize);
            int rowCount = Mathf.Max(1, Mathf.CeilToInt(body.Count / (float)perRow));

            int ColumnX(int col) => pad + col * (size + gap) + (col / groupSize) * groupGap;

            int gridRight = ColumnX(perRow - 1) + size;
            int stanceX = gridRight + gap + groupGap;
            int right = stance.HasValue ? stanceX + size : gridRight;

            _widthTexels = right + pad;
            _heightTexels = pad + rowCount * size + (rowCount - 1) * gap + top;

            if (rowCount == 1)
                for (int col = groupSize; col < perRow; col += groupSize)
                    _grooves.Add(ColumnX(col) - (gap + groupGap) / 2 - 1);
            if (stance.HasValue) _grooves.Add(stanceX - (gap + groupGap) / 2 - 1);
            if (rowCount > 1) _grooves.Clear();   // a groove is a line across ONE row of the stone

            int index = 0;
            for (int i = 0; i < body.Count; i++)
            {
                int row = i / perRow, col = i % perRow;
                int y = pad + row * (size + gap);
                _cells.Add(MakeCell(body[i], index++, ColumnX(col), y, size));
            }
            if (stance.HasValue)
                _cells.Add(MakeCell(stance.Value, index, stanceX, pad, size));

            ResizeFrame();
            Refit(force: true);
        }

        private Cell MakeCell(SpellBarEntry entry, int index, int x, int y, int size)
        {
            var box = HudRect.Make("Cell_" + index, _slotsRoot, x, y, size, size);
            // Pivot on the centre so the flip turns the slot in place. Its children are anchored
            // to the box's own corner, so moving the pivot moves nothing they draw.
            box.pivot = new Vector2(0.5f, 0.5f);
            box.anchoredPosition = new Vector2(x + size / 2, y + size / 2);

            string actionId = entry.ActionId;
            string key = entry.Kind == SpellBarEntryKind.Spell ? entry.Key : null;
            var slot = new HudAbilitySlot(box, _art, index, 0, 0, size,
                                          () => key, () => ResolveAction(actionId), _additive);
            var cell = new Cell { Entry = entry, Box = box, Slot = slot, Centre = new Vector2(x + size * 0.5f, y + size * 0.5f) };

            if (entry.Kind == SpellBarEntryKind.Verb || entry.Kind == SpellBarEntryKind.Stance)
                slot.SetVerb(MakeVerb(entry));
            slot.BecameReady += OnSlotReady;

            var hover = slot.Root.gameObject.AddComponent<HudSlotHover>();
            hover.Entered = () => ShowTip(cell);
            hover.Exited = HideTooltip;
            var click = slot.Root.gameObject.AddComponent<SpellBarSlotClick>();
            click.Clicked = () => OnCellClicked(cell);
            return cell;
        }

        private void ResizeFrame()
        {
            var old = _stoneTex;
            _stoneTex = SpellBarArt.BakeFrame(_widthTexels, _heightTexels, _hud, _style.AccentFor(_face), _grooves);
            _stone.texture = _stoneTex;
            HudRect.Place(_stone.rectTransform, 0, 0, _widthTexels, _heightTexels);
            HudRect.Place(_pixels, 0, 0, _widthTexels, _heightTexels);
            HudRect.Place(_content, 0, 0, _widthTexels, _heightTexels);
            HudRect.Place(_slotsRoot, 0, 0, _widthTexels, _heightTexels);
            // The glow sits on the gem BakePanel sets into the top edge, centred.
            HudRect.Place(_gemGlow.rectTransform, _widthTexels / 2 - 6, _heightTexels - 3 - 6, 13, 13);
            if (old != null && old != _stoneTex) HudLifetime.Release(old);
        }

        private static InputAction ResolveAction(string actionId)
        {
            if (string.IsNullOrEmpty(actionId)) return null;
            var gameplay = InputService.Instance != null ? InputService.Instance.Gameplay : null;
            if (gameplay == null || gameplay.Map == null) return null;
            int slash = actionId.IndexOf('/');
            return gameplay.Map.FindAction(slash >= 0 ? actionId.Substring(slash + 1) : actionId);
        }

        // -- The flip ------------------------------------------------------------------------

        private void BeginFlip(Stance target, bool shiftPage, bool page)
        {
            _flipTarget = target;
            _flipPage = shiftPage;
            _flipIsPage = page;
            _flip = FlipPhase.Out;
            _flipT = 0f;
            HideTooltip();
        }

        /// <summary>
        /// The bar turns over one slot at a time, left to right: every slot narrows to nothing,
        /// the face is rebuilt behind them, and they open again showing the other posture. Width
        /// steps in pairs of texels so a turning slot keeps both edges on the grid.
        /// </summary>
        private void TickFlip(float dt)
        {
            if (_flip == FlipPhase.None) return;
            _flipT += dt;
            float half = Mathf.Max(0.01f, _flipIsPage ? _style.pageHalfSeconds : _style.flipHalfSeconds);
            float stagger = _flipIsPage ? _style.pageStaggerSeconds : _style.flipStaggerSeconds;
            float end = half + stagger * Mathf.Max(0, _cells.Count - 1);

            if (_flip == FlipPhase.Out)
            {
                for (int i = 0; i < _cells.Count; i++)
                    SetTurn(_cells[i], 1f - Ease(Mathf.Clamp01((_flipT - i * stagger) / half)));
                if (_flipT < end) return;

                _face = _flipTarget;
                _shiftPage = _face == Stance.War && _flipPage;
                Rebuild(EntriesFor(_face), celebrate: false);
                for (int i = 0; i < _cells.Count; i++) SetTurn(_cells[i], 0f);
                _flip = FlipPhase.In;
                _flipT = 0f;
                if (_flipIsPage) OnPageArrived();
                else OnFaceArrived();
                return;
            }

            for (int i = 0; i < _cells.Count; i++)
                SetTurn(_cells[i], Ease(Mathf.Clamp01((_flipT - i * stagger) / half)));
            if (_flipT < end) return;
            for (int i = 0; i < _cells.Count; i++) SetTurn(_cells[i], 1f);
            _flip = FlipPhase.None;
        }

        private void SetTurn(Cell cell, float open)
        {
            int half = Mathf.Max(1, _style.SlotTexels / 2);
            float q = Mathf.Round(Mathf.Clamp01(open) * half) / half;
            var s = cell.Box.localScale;
            if (!Mathf.Approximately(s.x, q)) cell.Box.localScale = new Vector3(q, 1f, 1f);
        }

        private static float Ease(float t) => t * t * (3f - 2f * t);

        // -- Tooltip ---------------------------------------------------------------------------

        private void ShowTip(Cell cell)
        {
            if (_tooltip == null || cell == null || _flip != FlipPhase.None) return;
            var slot = cell.Slot;
            string title, body;
            Color accent = slot.AccentColour(_hud);
            if (cell.Entry.Kind == SpellBarEntryKind.Empty)
            {
                // An empty socket explains itself: room earned and not yet used, and on which layer.
                _tooltip.ShowText(_shiftPage ? "Hueco libre (Shift)" : "Hueco libre",
                                  _shiftPage ? "Aprende un hechizo de Shift+tecla y aparecerá aquí."
                                             : "Aprende un hechizo con tecla y aparecerá aquí.",
                                  accent, cell.Centre.x, _heightTexels, _widthTexels);
                return;
            }
            if (cell.Entry.Kind == SpellBarEntryKind.Spell)
            {
                var spell = slot.Spell;
                if (spell == null) return;
                title = string.IsNullOrEmpty(spell.displayName) ? spell.spellKey : spell.displayName;
                int mana = _caster != null ? _caster.ResolveManaCost(spell) : Mathf.RoundToInt(spell.manaCost);
                float cd = _caster != null ? _caster.ResolveCooldown(spell) : spell.cooldownDuration;
                body = (mana > 0 ? mana + " maná" : "sin coste") + "  ·  "
                     + (cd > 0.05f ? cd.ToString("0.#") + " s" : "sin recarga");
            }
            else
            {
                var verb = slot.Verb;
                if (verb == null) return;
                title = verb.Title;
                body = verb.Detail != null ? verb.Detail() : "";
            }
            string button = slot.BindingLabel;
            if (string.IsNullOrEmpty(button) && cell.Entry.Kind != SpellBarEntryKind.Spell) button = "Clic";
            if (!string.IsNullOrEmpty(button)) body = string.IsNullOrEmpty(body) ? button : body + "\n" + button;
            _tooltip.ShowText(title, body, accent, cell.Centre.x, _heightTexels, _widthTexels);
        }

        private void HideTooltip() => _tooltip?.Hide();

        // -- Clicks ------------------------------------------------------------------------------

        private void OnCellClicked(Cell cell)
        {
            if (cell == null || _flip != FlipPhase.None || !_visible) return;
            if (cell.Entry.Kind == SpellBarEntryKind.Empty) return;
            if (cell.Entry.Kind == SpellBarEntryKind.Spell)
            {
                // Through the controller's own gates; the motes come from the cast event, so a
                // click and a key press look identical.
                if (_controller != null) _controller.TryCastFromHud(cell.Entry.Key);
                return;
            }

            if (VerbSuppressed()) return;
            var verb = cell.Slot.Verb;
            if (verb == null || verb.Invoke == null || !verb.Available) return;
            verb.Invoke();
            cell.Slot.Flash(_hud);
            BurstVerb(cell);
        }
    }
}
