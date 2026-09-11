using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Core.Input;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.MultiSelect
{
    /// <summary>
    /// Picking, the drag-select marquee and the group drag.
    ///
    /// <para>Every input read goes through <c>EditorInput</c> — the SHARED verbs (<c>Select</c>,
    /// <c>Delete</c>, <c>Undo</c>, <c>Redo</c>, <c>Save</c>), which carry no <c>OwnerEditor</c>
    /// and are therefore live in every editor context including this one — plus
    /// <c>MouseInputManager</c> / <c>KeyboardInputManager</c>. Never a raw key read: a literal
    /// is invisible to the Controls editor and does not move when the key is rebound.</para>
    ///
    /// <para>The tool owns exactly TWO actions of its own, <c>Copy</c> and <c>Paste</c> in the
    /// <c>Editor.Selection</c> map (see <c>.Clipboard.cs</c>). Everything else it does is a
    /// button on its panel, because an owned key costs an action in the asset AND a descriptor
    /// in the closed <c>InputActionCatalog</c> — worth it for a gesture used a hundred times a
    /// session, not for one the panel is already showing.</para>
    /// </summary>
    public sealed partial class MultiSelectRuntimeEditor
    {
        /// <summary>A click may miss its target by this much in world units and still land.
        /// A light's handle is under half a unit and an emitter can be a fifth of one; without
        /// a floor the small things are unclickable, which is the same reason
        /// <c>HitTestEmitter</c> carries <c>EMITTER_GRAB_RADIUS</c>.</summary>
        private const float GRAB_PAD = 0.25f;

        /// <summary>Below this the gesture was a click, not a drag. In world units, so it
        /// scales with the zoom the same way the author's intent does.</summary>
        private const float DRAG_THRESHOLD = 0.15f;

        /// <summary>
        /// What a press ARMED, before the author has said which gesture they meant.
        ///
        /// <para>The tool used to decide at the PRESS: pressing anything unselected cleared the
        /// selection, selected that one thing and started moving it, so a box could only ever
        /// begin on empty ground. Measured in the shipped world, on the dense cluster this tool
        /// exists for, <b>98.4 % of starting corners were blocked</b> — the drag-select was
        /// unreachable exactly where it was needed.</para>
        ///
        /// <para>The gesture is resolved by what the author DOES next: move past the threshold
        /// and it is a drag, release without moving and it was a click. A press over an object
        /// is therefore ambiguous until then, and that ambiguity is the feature.</para>
        /// </summary>
        internal enum Gesture { None, PendingMarquee, PendingMove }

        /// <summary>
        /// THE gesture-priority rule, as a pure function of the press.
        ///
        /// <para>Extracted so it can be pinned without a scene: everything else about a press
        /// needs live editors, live content and a rendered frame, while the RULE is three
        /// booleans. It is also the whole of what the redesign changed, so it is the thing a
        /// regression would land on.</para>
        ///
        /// <list type="bullet">
        /// <item><b>Ctrl on something</b> — a complete gesture already; arm nothing, so a
        /// Ctrl-drag off an object cannot half-become a move.</item>
        /// <item><b>Something already selected</b> — a move. The only case where the author
        /// has already said which objects they mean.</item>
        /// <item><b>Anything else, INCLUDING a hit</b> — a marquee. This is the line that
        /// changed: a press on an unselected object used to commit to selecting and moving it,
        /// which is what made a box impossible to start over 98.4 % of a dense cluster.</item>
        /// </list>
        /// </summary>
        internal static Gesture ResolvePress(bool hitSomething, bool hitIsSelected, bool ctrlHeld)
        {
            if (ctrlHeld && hitSomething)      return Gesture.None;
            if (hitSomething && hitIsSelected) return Gesture.PendingMove;
            return Gesture.PendingMarquee;
        }

        private Gesture _gesture;
        private Vector3 _pressWorld;
        private bool    _pastThreshold;

        /// <summary>What the press landed on, if the release turns out to be a click.</summary>
        private GameObject       _pendingClickGo;
        private ISelectionDomain _pendingClickDomain;

        private Vector3       _marqueeStartWorld;
        private GameObject    _marqueeGo;
        private RectTransform _marqueeRt;

        private Vector3 _dragLastWorld;
        private readonly List<(SelectionItem item, Vector3 start)> _dragOrigins =
            new List<(SelectionItem, Vector3)>(32);

        // ── The frame ──────────────────────────────────────────────────────────

        private void TickInteraction()
        {
            // A double-click armed one frame ago. Opening HERE rather than inside the press is
            // the whole of why the target editor never sees the click that summoned it: Unity
            // gives no order between two editors' Update, so an editor activated mid-frame can
            // still be asked for its own Update afterwards — with the left button reported as
            // pressed THIS frame, which in the Buildings editor's place mode is a placement
            // nobody asked for. One frame of delay is invisible and removes the race entirely.
            if (ConsumePendingEditorOpen()) return;

            if (EditorInput.UndoPressed()) { DoUndo(); return; }
            if (EditorInput.RedoPressed()) { DoRedo(); return; }
            if (EditorInput.SavePressed()) { SaveTouchedDomains(deletion: false, "Guardado manual."); return; }
            if (EditorInput.DeletePressed()) { DeleteSelection(); return; }

            // Ctrl+C / Ctrl+V, and the ghost that follows the pointer while something is
            // copied. Ahead of the pointer gestures because a Ctrl press must not also arm a
            // marquee under the cursor it happens to be over.
            TickClipboard();

            // The camera pan owns the pointer while it is dragging; competing for it would
            // start a marquee every time the author pans across empty ground.
            if (_cameraPan.IsPanning) { AbortGesture(); return; }

            Vector3 world = PointerWorld();

            if (EditorInput.SelectPressed() && !PointerOverUI()) OnSelectPressed(world);
            else if (EditorInput.SelectHeld())                   OnSelectHeld(world);
            if (EditorInput.SelectReleased())                    OnSelectReleased(world);
        }

        private static bool PointerOverUI() =>
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        private static Vector3 PointerWorld()
        {
            var cam = Camera.main;
            if (cam == null) return Vector3.zero;
            var w = cam.ScreenToWorldPoint(MouseInputManager.GetScreenMousePosition());
            w.z = 0f;
            return w;
        }

        // ── Press / hold / release ─────────────────────────────────────────────

        // A press ARMS a gesture; it never commits one. Which gesture it armed is decided by
        // what happens next:
        //
        //   press on something ALREADY SELECTED  -> a move is armed
        //   press on anything else               -> a MARQUEE is armed
        //   release without moving               -> it was a CLICK after all
        //
        // The consequence the tool was rebuilt for: a box can be started ANYWHERE — over a
        // building, over an emitter, over empty ground. Only an already-selected thing claims
        // the drag, which is exactly the case where the author has already said what they mean.

        private void OnSelectPressed(Vector3 world)
        {
            // A FAST repeat on a selected item means "open this one"; a SLOW repeat on the
            // same spot means "give me the next one down". They are the same two clicks and
            // only the interval separates them, so the double-click is tested FIRST and, when
            // it fires, returns before HitTest — which is what advances the cycle. Testing it
            // afterwards would open the editor for whichever item the cycle had just moved on
            // to, i.e. never the one the author was looking at.
            if (_doubleClick.PollLeftDouble() && TryArmEditorOpen(world))
            {
                // Nothing is armed for this press: the release must not also select, promote
                // or start a marquee in an editor that is about to be closed.
                _gesture            = Gesture.None;
                _pendingClickGo     = null;
                _pendingClickDomain = null;
                return;
            }

            _pressWorld        = world;
            _marqueeStartWorld = world;
            _pastThreshold     = false;

            var hit = HitTest(world, out var hitDomain);
            bool ctrl = KeyboardInputManager.IsCtrlHeld();

            // The hit is remembered whichever gesture is armed, so a release without movement
            // still selects it — the click did not stop working, it stopped being decided
            // before the author had finished making it.
            _pendingClickGo     = hit;
            _pendingClickDomain = hitDomain;
            _gesture = ResolvePress(hit != null, hit != null && _selection.Contains(hit), ctrl);

            switch (_gesture)
            {
                case Gesture.None:
                    // Ctrl+click: toggle one thing, here and now.
                    bool nowSelected = _selection.Toggle(hitDomain, hit);
                    RefreshPanel();
                    SetStatus(nowSelected
                        ? $"Anadido {hitDomain.LabelSingular} - {Elements(_selection.Count)}."
                        : $"Quitado {hitDomain.LabelSingular} - {Elements(_selection.Count)}.");
                    break;

                case Gesture.PendingMove:
                    ArmDrag(world);
                    break;
            }
        }

        private void OnSelectHeld(Vector3 world)
        {
            if (_gesture == Gesture.None) return;

            if (!_pastThreshold)
            {
                if ((world - _pressWorld).magnitude < DRAG_THRESHOLD) return;
                _pastThreshold = true;
                // The box appears only once the gesture IS a drag. Showing it on the press
                // flashed a zero-sized rectangle under the cursor on every single click.
                if (_gesture == Gesture.PendingMarquee) ShowMarquee(true);
            }

            if (_gesture == Gesture.PendingMarquee) UpdateMarquee(world);
            else                                    UpdateDrag(world);
        }

        private void OnSelectReleased(Vector3 world)
        {
            var  gesture = _gesture;
            bool dragged = _pastThreshold;
            _gesture       = Gesture.None;
            _pastThreshold = false;

            if (gesture == Gesture.None) return;

            if (gesture == Gesture.PendingMarquee)
            {
                ShowMarquee(false);
                if (dragged) CommitMarquee(world);
                else         CommitClick();
                return;
            }

            if (dragged) CommitDrag();
            else         PromoteClickedToPrimary();
            _dragOrigins.Clear();
        }

        /// <summary>
        /// A press that never moved. It selects what was under it, replacing the selection —
        /// or clears, when there was nothing there.
        ///
        /// <para>The CLEAR happens HERE rather than on the press, and that is the second half
        /// of the same defect: the old code emptied the selection the instant the button went
        /// down, so a box that caught nothing — or a gesture abandoned by panning away — had
        /// already destroyed the group before the author could see any result.</para>
        /// </summary>
        private void CommitClick()
        {
            if (_pendingClickGo == null)
            {
                if (_selection.Count == 0) return;
                _selection.Clear();
                RefreshPanel();
                SetStatus("Seleccion vacia.");
                return;
            }

            _selection.Clear();
            _selection.Add(_pendingClickDomain, _pendingClickGo);
            RefreshPanel();

            // Say how deep the stack is, or the author has no way to know that clicking again
            // would offer something else — the cycle is only discoverable if the first click
            // admits there were alternatives.
            string what = _pendingClickDomain.Describe(_pendingClickGo);
            SetStatus(_hitCandidates.Count > 1
                ? $"{what}  —  {_cycleIndex + 1} de {_hitCandidates.Count} aqui. Clic otra vez para el siguiente."
                : $"{what} seleccionado.");
        }

        /// <summary>Clicking a member of the group without moving makes it the PRIMARY — what
        /// a later drag anchors on — rather than collapsing the group to it. Collapsing would
        /// make one stray click on a group of twenty cost nineteen.</summary>
        private void PromoteClickedToPrimary()
        {
            if (_pendingClickGo == null || _pendingClickDomain == null) return;
            _selection.Add(_pendingClickDomain, _pendingClickGo);
            RefreshPanel();
            SetStatus($"Ancla: {_pendingClickDomain.LabelSingular}.");
        }

        /// <summary>
        /// Drop whatever gesture is in flight and PUT BACK anything a live drag had moved.
        ///
        /// <para>The restore is the point. Closing the editor mid-drag used to clear the flags
        /// and leave the objects wherever the pointer had dragged them — measured: a building
        /// moved from (166.28, 57.59) to (171.28, 62.59), stayed there, and recorded <b>zero
        /// undo entries</b>, so the next save of any kind would have written that position.
        /// Escape closes the editor, so the gesture that lost data was the one an author uses
        /// to abandon a drag they did not mean to start.</para>
        /// </summary>
        private void AbortGesture()
        {
            if (_gesture == Gesture.PendingMove && _pastThreshold)
            {
                for (int i = 0; i < _dragOrigins.Count; i++)
                {
                    var (item, start) = _dragOrigins[i];
                    if (item.IsAlive) item.Domain.MoveTo(item.Go, start);
                }
            }

            _gesture       = Gesture.None;
            _pastThreshold = false;
            _dragOrigins.Clear();
            ShowMarquee(false);
        }

        // ── Double-click: open the item's own editor ───────────────────────────

        /// <summary>0.4 s / 25 px, the shared defaults every other runtime editor uses. A
        /// double-click that meant something different here would be a gesture the author has
        /// to learn twice.</summary>
        private readonly EditorDoubleClickDetector _doubleClick = new EditorDoubleClickDetector();

        private GameObject       _pendingOpenGo;
        private ISelectionDomain _pendingOpenDomain;

        /// <summary>Scratch for the double-click's own hit test. Separate from
        /// <c>_hitCandidates</c> on purpose: that buffer is what the status line reports the
        /// stack depth from, and overwriting it here would change what the last single click
        /// said it had found.</summary>
        private readonly List<SelectionItem> _openCandidates = new List<SelectionItem>(16);

        /// <summary>
        /// Remember which item a double-click asked to open, if any.
        ///
        /// <para>ONLY AN ALREADY-SELECTED ITEM QUALIFIES, and that is the rule rather than
        /// "whatever is under the pointer". The first click of the double has already selected
        /// something and drawn its outline, so the author can SEE what the second click will
        /// open — and a double-click that opened whatever happened to be topmost would open
        /// the building a light is standing inside, which is the exact stack this tool's
        /// cycling exists to get past.</para>
        ///
        /// <para>Smallest-first ordering carries over from <see cref="HitTestAll"/>, so a
        /// selected light inside a selected building wins over the building.</para>
        /// </summary>
        private bool TryArmEditorOpen(Vector3 world)
        {
            HitTestAll(world, _openCandidates);

            for (int i = 0; i < _openCandidates.Count; i++)
            {
                var c = _openCandidates[i];
                if (!c.IsAlive || !_selection.Contains(c.Go)) continue;
                _pendingOpenGo     = c.Go;
                _pendingOpenDomain = c.Domain;
                return true;
            }
            return false;
        }

        /// <summary>Perform an armed open. Returns true when this editor is now closed, so the
        /// caller stops touching a tool that no longer owns the pointer.</summary>
        private bool ConsumePendingEditorOpen()
        {
            var go  = _pendingOpenGo;
            var dom = _pendingOpenDomain;
            _pendingOpenGo     = null;
            _pendingOpenDomain = null;

            if (go == null || dom == null) return false;

            if (!dom.OpenEditorFor(go, this))
            {
                SetStatus($"El editor de {dom.Label} no esta disponible.");
                return false;
            }
            return true;
        }

        /// <summary>Forget a half-made double-click and any open it armed. Called on open and
        /// on close, or reopening the tool would land in an editor the author left minutes
        /// ago, and a click made before the close would pair with one made after it.</summary>
        private void ResetDoubleClick()
        {
            _doubleClick.Reset();
            _pendingOpenGo     = null;
            _pendingOpenDomain = null;
        }

        // ── Hit testing ────────────────────────────────────────────────────────

        /// <summary>How near two presses must be to count as "the same spot" for cycling.
        /// World units, so it holds its meaning at any zoom.</summary>
        private const float CYCLE_RADIUS = 0.35f;

        private readonly List<SelectionItem> _hitCandidates = new List<SelectionItem>(16);
        private Vector3 _lastClickWorld = new Vector3(float.MaxValue, float.MaxValue, 0f);
        private int     _cycleIndex;

        /// <summary>How many things the last press had to choose between. Feeds the status
        /// line, which is the only place an author learns that a stack exists at all.</summary>
        internal int LastHitCandidateCount => _hitCandidates.Count;
        internal int LastHitCycleIndex     => _cycleIndex;

        /// <summary>
        /// Everything under <paramref name="world"/>, SMALLEST FIRST, across every enabled
        /// domain.
        ///
        /// <para>Small-first is the rule <c>HitTestEmitter</c> already applies within particles
        /// and it matters far more here: a building is several units across and would otherwise
        /// swallow every light and emitter placed inside it — which is precisely the lamppost
        /// this tool exists for. Ordering rather than picking one is what makes the stack
        /// navigable instead of merely resolvable.</para>
        /// </summary>
        private void HitTestAll(Vector3 world, List<SelectionItem> buffer)
        {
            buffer.Clear();
            var point = new Vector2(world.x, world.y);

            for (int d = 0; d < _domains.Length; d++)
            {
                var dom = _domains[d];
                if (!IsEnabled(dom) || !dom.Available) continue;

                dom.Collect(_collectBuffer);
                for (int i = 0; i < _collectBuffer.Count; i++)
                {
                    var go = _collectBuffer[i];
                    if (go == null || !dom.TryGetRect(go, out var rect)) continue;

                    var padded = Rect.MinMaxRect(rect.xMin - GRAB_PAD, rect.yMin - GRAB_PAD,
                                                 rect.xMax + GRAB_PAD, rect.yMax + GRAB_PAD);
                    if (padded.Contains(point)) buffer.Add(new SelectionItem(dom, go));
                }
            }

            buffer.Sort((a, b) =>
            {
                float aa = a.Domain.TryGetRect(a.Go, out var ra) ? ra.width * ra.height : float.MaxValue;
                float bb = b.Domain.TryGetRect(b.Go, out var rb) ? rb.width * rb.height : float.MaxValue;
                return aa.CompareTo(bb);
            });
        }

        /// <summary>
        /// What one press picks out of the stack under it.
        ///
        /// <para>CLICKING THE SAME SPOT AGAIN TAKES THE NEXT ONE DOWN, and wraps. It is the
        /// gesture Unity's own scene view uses, and it needs no modifier — which matters
        /// because a modifier is a thing an author has to be told about, while "it did not pick
        /// the one I wanted, click again" is a thing they try. The alternative, a popup listing
        /// the stack, interrupts a gesture that is over in a tenth of a second.</para>
        ///
        /// <para>The cycle resets the moment the pointer moves more than
        /// <see cref="CYCLE_RADIUS"/>, so walking across a dense street always starts from the
        /// smallest thing under the cursor rather than from wherever the last stack left off.</para>
        /// </summary>
        private GameObject HitTest(Vector3 world, out ISelectionDomain domain)
        {
            HitTestAll(world, _hitCandidates);

            if (_hitCandidates.Count == 0)
            {
                domain = null;
                _cycleIndex = 0;
                _lastClickWorld = world;
                return null;
            }

            bool sameSpot = (world - _lastClickWorld).sqrMagnitude <= CYCLE_RADIUS * CYCLE_RADIUS;
            _cycleIndex = sameSpot ? (_cycleIndex + 1) % _hitCandidates.Count : 0;
            _lastClickWorld = world;

            var picked = _hitCandidates[_cycleIndex];
            domain = picked.Domain;
            return picked.Go;
        }

        // ── Marquee ────────────────────────────────────────────────────────────

        /// <summary>Show or hide the box. It is built lazily and never destroyed, so a drag
        /// costs no allocation after the first one.</summary>
        private void ShowMarquee(bool visible)
        {
            if (visible) EnsureMarquee();
            if (_marqueeGo != null) _marqueeGo.SetActive(visible);
        }

        private void UpdateMarquee(Vector3 world)
        {
            if (_marqueeRt == null) return;
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 a = cam.WorldToScreenPoint(_marqueeStartWorld);
            Vector3 b = cam.WorldToScreenPoint(world);
            float scale = _canvas != null ? Mathf.Max(0.0001f, _canvas.scaleFactor) : 1f;

            var min = new Vector2(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y)) / scale;
            var max = new Vector2(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)) / scale;

            _marqueeRt.anchoredPosition = min;
            _marqueeRt.sizeDelta        = max - min;
        }

        /// <summary>
        /// Everything the box touched becomes the selection.
        ///
        /// <para>REPLACES by default and ADDS while Ctrl is held, and the modifier is read at
        /// the RELEASE rather than at the press — the author decides whether this box extends
        /// the group while they are looking at what it caught, not before they drew it. Ctrl
        /// means the same thing here as it does on a click, which is why there is no second
        /// modifier for it.</para>
        /// </summary>
        private void CommitMarquee(Vector3 world)
        {
            var area = Rect.MinMaxRect(
                Mathf.Min(_marqueeStartWorld.x, world.x), Mathf.Min(_marqueeStartWorld.y, world.y),
                Mathf.Max(_marqueeStartWorld.x, world.x), Mathf.Max(_marqueeStartWorld.y, world.y));

            // A box under the drag threshold on BOTH axes is a click that happened to miss;
            // treating it as a selection would sweep up whatever the cursor grazed.
            if (area.width < DRAG_THRESHOLD && area.height < DRAG_THRESHOLD) { CommitClick(); return; }

            bool additive = KeyboardInputManager.IsCtrlHeld();
            if (!additive) _selection.Clear();

            int added = 0;
            for (int d = 0; d < _domains.Length; d++)
            {
                var dom = _domains[d];
                if (!IsEnabled(dom) || !dom.Available) continue;

                dom.Collect(_collectBuffer);
                for (int i = 0; i < _collectBuffer.Count; i++)
                {
                    var go = _collectBuffer[i];
                    if (go == null || !dom.TryGetRect(go, out var rect)) continue;
                    if (!area.Overlaps(rect, allowInverse: true)) continue;
                    if (_selection.Add(dom, go)) added++;
                }
            }

            RefreshPanel();
            SetStatus(added == 0
                ? (additive ? "El marco no anadio nada." : "El marco no atrapo nada.")
                : $"{(additive ? "Anadidos" : "Seleccionados")} {Elements(added)} - " +
                  $"{DescribeSelectionByDomain()}.");
        }

        private void EnsureMarquee()
        {
            if (_marqueeGo != null || _canvas == null) return;

            _marqueeGo = EditorUIHelpers.CreateUI("SelectionMarquee", _canvas.transform);
            _marqueeRt = _marqueeGo.GetComponent<RectTransform>();
            _marqueeRt.anchorMin = _marqueeRt.anchorMax = Vector2.zero;
            _marqueeRt.pivot     = Vector2.zero;

            var img = _marqueeGo.AddComponent<Image>();
            img.color = UITheme.MARQUEE_FILL;
            // The box is drawn UNDER the cursor the author is dragging with; letting it eat
            // rays would make it consume its own release.
            img.raycastTarget = false;
            _marqueeGo.SetActive(false);
        }

        // ── Group drag ─────────────────────────────────────────────────────────

        /// <summary>Capture where every member started. Nothing has moved yet — the gesture is
        /// only ARMED, and it becomes a move the moment the pointer clears the threshold.</summary>
        private void ArmDrag(Vector3 world)
        {
            _dragLastWorld = world;

            // Origins are captured ONCE, at the press. Deriving them per frame from the live
            // positions would accumulate float error over a long drag and, worse, would leave
            // the undo record pointing at wherever the group happened to be one frame in.
            _dragOrigins.Clear();
            var items = _selection.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (!it.IsAlive) continue;
                _dragOrigins.Add((it, it.Domain.PositionOf(it.Go)));
            }
        }

        private void UpdateDrag(Vector3 world)
        {
            Vector3 delta = world - _pressWorld;
            _dragLastWorld = world;
            for (int i = 0; i < _dragOrigins.Count; i++)
            {
                var (item, start) = _dragOrigins[i];
                if (!item.IsAlive) continue;
                item.Domain.MoveTo(item.Go, start + delta);
            }
        }

        private void CommitDrag()
        {
            Vector3 delta = _dragLastWorld - _pressWorld;
            var origins = new List<(SelectionItem item, Vector3 start)>(_dragOrigins);
            if (origins.Count == 0) return;

            // The move already happened on screen; the command's Do re-applies it so a REDO
            // works, and its Undo puts every member back at its captured origin. One entry
            // for the whole group, which is the property three per-editor entries cannot have.
            RecordAndRun($"Mover {origins.Count}",
                () =>
                {
                    for (int i = 0; i < origins.Count; i++)
                    {
                        var (item, start) = origins[i];
                        if (item.IsAlive) item.Domain.MoveTo(item.Go, start + delta);
                    }
                },
                () =>
                {
                    for (int i = 0; i < origins.Count; i++)
                    {
                        var (item, start) = origins[i];
                        if (item.IsAlive) item.Domain.MoveTo(item.Go, start);
                    }
                },
                deletion: false);

            SetStatus($"Movidos {Elements(origins.Count)} ({delta.x:F2}, {delta.y:F2}). " +
                      "Ctrl+Z para deshacer.");
        }
    }
}
