using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.FSM;
using Valkur.UIKit;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// World-side selection + drag for the Entities Editor (F5), mirroring the
    /// Buildings Editor (F10):
    ///   â€¢ LMB on an NPC in Select mode â†’ set as active (YELLOW outline);
    ///     all other NPCs sharing the same <c>MonsterDefinition.monsterKey</c>
    ///     get an ORANGE outline.
    ///   â€¢ RMB-press on a hovered NPC â†’ start a move-drag (the NPC follows the
    ///     cursor while RMB is held; release commits with undo support, parity
    ///     with Buildings <c>FinalizeMoveDrag</c>).
    ///
    /// Outline rendering uses <see cref="EntityOutlineRenderer"/>, a 4-corner
    /// LineRenderer that follows the NPC's <see cref="SpriteRenderer"/> bounds
    /// every LateUpdate.
    /// </summary>
    public partial class EntitiesRuntimeEditor : SingletonMonoBehaviour<EntitiesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // â”€â”€ Selection state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private FSMMonsterBrain _activeEntity;
        private readonly List<FSMMonsterBrain> _sameKeyEntities = new List<FSMMonsterBrain>();

        // â”€â”€ Drag state (RMB on hovered entity) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private bool    _entityDragging;
        private Vector3 _entityDragStartWorldPos;
        private Vector3 _entityDragOffset;

        // â”€â”€ Outline FX â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private EntityOutlineRenderer        _activeFx;
        private readonly List<EntityOutlineRenderer> _sameKeyFxPool = new List<EntityOutlineRenderer>();

        private static readonly Color ACTIVE_YELLOW       = new Color(1f, 215f / 255f, 0f, 1f);
        private static readonly Color SAME_KEY_ORANGE     = new Color(1f, 0.55f, 0f, 1f);
        private const float ACTIVE_THICKNESS_WORLD        = 0.10f;   // ~ 1.6 px @ PPU 16
        private const float SAME_KEY_THICKNESS_WORLD      = 0.06f;

        // â”€â”€ FX construction / teardown â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void EnsureSelectionFx()
        {
            if (_activeFx == null)
            {
                var go = new GameObject("EntitiesEditor.ActiveFx");
                go.transform.SetParent(transform, false);
                _activeFx = go.AddComponent<EntityOutlineRenderer>();
                _activeFx.Configure(ACTIVE_YELLOW, ACTIVE_THICKNESS_WORLD);
            }
        }

        private void HideSelectionFx()
        {
            if (_activeFx != null)
            {
                _activeFx.Follow(null, null);
                _activeFx.SetVisible(false);
            }
            for (int i = 0; i < _sameKeyFxPool.Count; i++)
            {
                var fx = _sameKeyFxPool[i];
                if (fx != null) { fx.Follow(null, null); fx.SetVisible(false); }
            }
        }

        // â”€â”€ Active entity â†’ outlines â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Set the editor's active NPC, refresh the yellow outline, and rebuild
        /// the orange-outline pool for every other NPC sharing the same
        /// <c>monsterKey</c>. Passing null clears the selection.
        /// </summary>
        private void SetActiveEntity(FSMMonsterBrain brain)
        {
            EnsureSelectionFx();
            _activeEntity = brain;

            if (brain == null)
            {
                HideSelectionFx();
                _sameKeyEntities.Clear();
                return;
            }

            var sr = brain.GetComponentInChildren<SpriteRenderer>();
            _activeFx.Follow(brain.transform, sr);
            _activeFx.SetVisible(sr != null);

            RebuildSameKeyFx(brain);

            string key = brain.Definition != null ? brain.Definition.monsterKey : "?";
            SetStatus($"Selected: {brain.gameObject.name} [{key}]");
        }

        /// <summary>
        /// Pool every NPC in the scene whose <c>monsterKey</c> matches the
        /// active NPC's key (and isn't the active NPC itself), then assign one
        /// orange outline renderer per match. Surplus pool entries are hidden.
        /// </summary>
        private void RebuildSameKeyFx(FSMMonsterBrain active)
        {
            _sameKeyEntities.Clear();

            string activeKey = active != null && active.Definition != null
                ? active.Definition.monsterKey
                : null;

            if (!string.IsNullOrEmpty(activeKey))
            {
                var all = FindObjectsOfType<FSMMonsterBrain>();
                for (int i = 0; i < all.Length; i++)
                {
                    var b = all[i];
                    if (b == null || b == active || b.Definition == null) continue;
                    if (string.Equals(b.Definition.monsterKey, activeKey,
                            System.StringComparison.OrdinalIgnoreCase))
                        _sameKeyEntities.Add(b);
                }
            }

            while (_sameKeyFxPool.Count < _sameKeyEntities.Count)
            {
                var go = new GameObject("EntitiesEditor.SameKeyFx");
                go.transform.SetParent(transform, false);
                var fx = go.AddComponent<EntityOutlineRenderer>();
                fx.Configure(SAME_KEY_ORANGE, SAME_KEY_THICKNESS_WORLD);
                _sameKeyFxPool.Add(fx);
            }

            for (int i = 0; i < _sameKeyFxPool.Count; i++)
            {
                if (i < _sameKeyEntities.Count)
                {
                    var peer = _sameKeyEntities[i];
                    var sr = peer != null ? peer.GetComponentInChildren<SpriteRenderer>() : null;
                    _sameKeyFxPool[i].Follow(peer != null ? peer.transform : null, sr);
                    _sameKeyFxPool[i].SetVisible(peer != null && sr != null);
                }
                else
                {
                    _sameKeyFxPool[i].Follow(null, null);
                    _sameKeyFxPool[i].SetVisible(false);
                }
            }
        }

        // â”€â”€ Hit-test â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Returns the front-most NPC whose sprite bounds (or 0.5-radius collider)
        /// contain <paramref name="worldPos"/>. Front-most = highest sorting order
        /// fallback to closest centre, mirroring Items Editor stacking heuristics.
        /// </summary>
        private FSMMonsterBrain FindEntityAtWorldPosition(Vector3 worldPos)
        {
            FSMMonsterBrain best = null;
            float bestDistSq = float.PositiveInfinity;

            var all = FindObjectsOfType<FSMMonsterBrain>();
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null) continue;
                var sr = b.GetComponentInChildren<SpriteRenderer>();
                if (sr == null || sr.sprite == null) continue;
                var bounds = sr.bounds;
                if (!bounds.Contains(new Vector3(worldPos.x, worldPos.y, bounds.center.z))) continue;

                float d = (b.transform.position - worldPos).sqrMagnitude;
                if (d < bestDistSq) { bestDistSq = d; best = b; }
            }

            // Fallback to physics overlap (Python parity: 0.5 world-unit pick radius
            // for entities that don't have a SpriteRenderer the cursor is over).
            if (best == null)
            {
                var hit = Physics2D.OverlapCircle(worldPos, 0.5f, LayerMask.GetMask("NPC"));
                if (hit != null) best = hit.GetComponentInParent<FSMMonsterBrain>();
            }
            return best;
        }

        // â”€â”€ Per-frame world interaction (called from Update while editor is active) â”€

        /// <summary>
        /// LMB selection (Select mode) + RMB-drag move on any hovered NPC, regardless
        /// of mode. Returns true if the drag pipeline consumed the event so the
        /// regular spawn/delete handler should be skipped this frame.
        /// </summary>
        private bool UpdateEntitySelectionAndDrag()
        {
            var mouse = Mouse.current;
            if (mouse == null) return false;

            bool overUi = UnityEngine.EventSystems.EventSystem.current != null
                       && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

            var cam = Camera.main;
            if (cam == null) return false;

            Vector3 worldPos = cam.ScreenToWorldPoint(Valkur.Core.Input.MouseInputManager.GetScreenMousePosition());
            worldPos.z = 0f;

            // â”€â”€ Active drag â†’ follow cursor while RMB is held â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            if (_entityDragging && _activeEntity != null)
            {
                _activeEntity.transform.position = worldPos + _entityDragOffset;
                if (Valkur.Core.Input.MouseInputManager.WasRightMouseButtonReleasedThisFrame()) FinalizeEntityDrag();
                return true;
            }

            if (overUi) return false;

            // â”€â”€ LMB â†’ select hovered NPC (Select mode only; spawn/delete keep their
            //         own LMB handlers) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            if (_mode == EditorMode.Select && Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonPressedThisFrame())
            {
                var hit = FindEntityAtWorldPosition(worldPos);
                if (hit != null)
                {
                    SetActiveEntity(hit);
                    ShowMonsterPropertiesIfHasDef(hit);
                }
                else
                {
                    // Click on empty world clears the selection.
                    if (_activeEntity != null) SetActiveEntity(null);
                    SetStatus("Nothing under cursor.");
                }
                // Always consume the LMB in Select mode so HandleMapInteraction's
                // legacy SelectEntityAtPosition path doesn't fire on the same frame.
                return true;
            }

            // â”€â”€ RMB â†’ start move-drag on hovered NPC (selects it too, Buildings parity) â”€
            if (Valkur.Core.Input.MouseInputManager.WasRightMouseButtonPressedThisFrame())
            {
                var hit = FindEntityAtWorldPosition(worldPos);
                if (hit != null)
                {
                    SetActiveEntity(hit);
                    _entityDragging = true;
                    _entityDragStartWorldPos = hit.transform.position;
                    _entityDragOffset = hit.transform.position - worldPos;
                    SetStatus($"Move drag: '{hit.gameObject.name}' â€” release RMB to commit.");
                    return true;
                }
            }

            return false;
        }

        private void FinalizeEntityDrag()
        {
            _entityDragging = false;
            if (_activeEntity == null) return;

            var brain    = _activeEntity;
            Vector3 from = _entityDragStartWorldPos;
            Vector3 to   = brain.transform.position;
            if ((to - from).sqrMagnitude <= 0.0001f)
            {
                SetStatus("Move cancelled (no movement).");
                return;
            }

            // Capture a label that survives the brain being destroyed before undo.
            string label = $"Move {brain.gameObject.name} ({to.x:F1},{to.y:F1})";
            _undo.Record(new UndoStack.LambdaCommand(label,
                doAction:   () => { if (brain != null) brain.transform.position = to;   },
                undoAction: () => { if (brain != null) brain.transform.position = from; }));

            SetStatus($"Moved '{brain.gameObject.name}' â†’ ({to.x:F1}, {to.y:F1}).");
        }

        /// <summary>
        /// Routes properties-panel population for the picked NPC (mirrors the
        /// Picker click path) without re-selecting the picker slot.
        /// </summary>
        private void ShowMonsterPropertiesIfHasDef(FSMMonsterBrain brain)
        {
            if (brain == null || brain.Definition == null) return;
            string key = brain.Definition.monsterKey;
            if (string.IsNullOrEmpty(key)) return;
            ShowMonsterProperties(key);
        }
    }
}
