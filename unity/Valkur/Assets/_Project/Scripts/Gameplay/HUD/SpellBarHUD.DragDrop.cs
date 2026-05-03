using UnityEngine;
using UnityEngine.EventSystems;
using Valkur.Data;
using Valkur.Gameplay.Spells.UI;

namespace Valkur.Gameplay.UI
{
    public partial class SpellBarHUD
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Click → cast
        // ─────────────────────────────────────────────────────────────────────

        public void OnSlotClicked(int index)
        {
            if (_caster == null) return;
            if (index < 0 || index >= _slotViews.Length) return;
            var v = _slotViews[index];
            if (string.IsNullOrEmpty(v.SpellKey)) return;

            Vector2 dir = ResolveCastDirection();
            if (v.SlotIndex >= 0) _caster.TryCast(v.SlotIndex, dir);
            else                  _caster.TryCastByKey(v.SpellKey, dir);
        }

        public bool CanAcceptSpellDrop(int slotIndex)
        {
            ResolvePlayer();
            return _caster != null && slotIndex >= 0 && slotIndex < _caster.SlotCount;
        }

        public bool TryAssignSpellToSlot(int slotIndex, SpellDefinition spell)
        {
            ResolvePlayer();
            if (_caster == null || spell == null) return false;
            if (slotIndex < 0 || slotIndex >= _caster.SlotCount) return false;

            _caster.SetSpell(slotIndex, spell);
            Populate();
            RefreshDynamic();
            return true;
        }

        public bool TryMoveAssignedSpell(int fromSlotIndex, int toSlotIndex)
        {
            ResolvePlayer();
            if (_caster == null) return false;
            if (fromSlotIndex == toSlotIndex) return false;
            if (fromSlotIndex < 0 || fromSlotIndex >= _caster.SlotCount) return false;
            if (toSlotIndex < 0 || toSlotIndex >= _caster.SlotCount) return false;

            var fromSpell = _caster.GetSpellAtSlot(fromSlotIndex);
            if (fromSpell == null) return false;

            var toSpell = _caster.GetSpellAtSlot(toSlotIndex);
            _caster.SetSpell(toSlotIndex, fromSpell);
            _caster.SetSpell(fromSlotIndex, toSpell);
            Populate();
            RefreshDynamic();
            return true;
        }

        public void BeginSlotDrag(int index, PointerEventData ev)
        {
            ResolvePlayer();
            if (_caster == null || ev.button != PointerEventData.InputButton.Left) return;
            if (index < 0 || index >= _slotViews.Length) return;

            var view = _slotViews[index];
            if (view.SlotIndex < 0) return;

            var spell = _caster.GetSpellAtSlot(view.SlotIndex);
            if (spell == null) return;

            var dragger = view.Root != null ? view.Root.GetComponent<CanvasGroup>() : null;
            if (dragger == null && view.Root != null)
                dragger = view.Root.AddComponent<CanvasGroup>();

            if (dragger != null)
            {
                dragger.alpha = 0.55f;
                dragger.blocksRaycasts = false;
            }

            SpellDragContext.Begin(spell, view.Icon != null ? view.Icon.sprite : spell.sprite, SpellDragOrigin.HudSlot, view.SlotIndex, _canvas, ev.position);
        }

        public void UpdateSlotDrag(PointerEventData ev)
        {
            if (!SpellDragContext.IsDragging)
                return;

            SpellDragContext.UpdatePosition(ev.position, _canvas);
        }

        public void EndSlotDrag(int index, PointerEventData ev)
        {
            if (index >= 0 && index < _slotViews.Length)
            {
                var view = _slotViews[index];
                if (view.Root != null)
                {
                    var cg = view.Root.GetComponent<CanvasGroup>();
                    if (cg != null)
                    {
                        cg.alpha = 1f;
                        cg.blocksRaycasts = true;
                    }
                }
            }

            SpellDragContext.End();
        }
    }
}
