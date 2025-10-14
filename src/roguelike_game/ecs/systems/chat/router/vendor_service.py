from __future__ import annotations

import logging
from typing import Any

from roguelike_game.ecs.systems.chat.chat_bubble_utils import push_bubble
from .io_utils import ChatIO

logger = logging.getLogger(__name__)


class VendorService:
    """Encapsulates vendor/trader interactions for chat commands."""

    def __init__(self, io: ChatIO) -> None:
        self.io = io

    # ---- Query vendor system -------------------------------------------------
    def _get_vendor_trade_system(self, world: Any):
        for s in getattr(world, 'update_systems', []):
            if type(s).__name__ == 'VendorTradeSystem':
                return s
        from roguelike_game.ecs.systems.vendors.vendor_trade_system import VendorTradeSystem
        inst = VendorTradeSystem()
        world.update_systems.append(inst)
        return inst

    def is_trader(self, world: Any, eid: int) -> bool:
        try:
            invs = world.components.get('InventoryComponent', {})
            return eid in invs
        except Exception:
            return False

    # ---- Admin/queries ------------------------------------------------------
    def vendor_stock(self, world: Any, vendor_eid: int, item_id: str = 'wood') -> str:
        try:
            invs = world.components.get('InventoryComponent', {})
            inv = invs.get(vendor_eid)
            qty = 0
            if inv and hasattr(inv, 'slots'):
                for st in getattr(inv, 'slots', []) or []:
                    try:
                        vts = self._get_vendor_trade_system(world)
                        norm_target, _ = vts._normalize_ids(world, vendor_eid, (item_id or 'wood'))
                        iid = str(getattr(st, 'item_id', '')).lower()
                        if st and iid == str(norm_target).lower():
                            qty += int(getattr(st, 'quantity', 0) or 0)
                    except Exception:
                        pass
            vts = self._get_vendor_trade_system(world)
            target_item, _ = vts._normalize_ids(world, vendor_eid, (item_id or 'wood'))
            price = vts._get_price(world, vendor_eid, target_item, op='buy') or 1
            lang = self.io.lang_for(world, vendor_eid, None)
            if lang == 'es':
                nice = 'madera' if target_item == 'wood' else target_item
                return f"Tengo {qty} de {nice} a {int(price)} oro la unidad."
            else:
                nice = 'wood' if target_item == 'wood' else target_item
                return f"I have {qty} of {nice} at {int(price)} gold each."
        except Exception:
            lang = self.io.lang_for(world, vendor_eid, None)
            return "Tengo stock a 1 oro la unidad." if lang == 'es' else "I have stock at 1 gold each."

    def vendor_gold(self, world: Any, vendor_eid: int) -> str:
        vts = self._get_vendor_trade_system(world)
        try:
            gold = int(vts.get_stock(world, vendor_eid, 'gold'))
        except Exception:
            gold = 0
        lang = self.io.lang_for(world, vendor_eid, None)
        return (f"Tengo {gold} de oro disponible para pagar." if lang == 'es' else f"I have {gold} gold available to pay.")

    def vendor_restock(self, world: Any, vendor_eid: int, item_id: str, qty: int) -> str:
        vts = self._get_vendor_trade_system(world)
        try:
            return vts.restock(world, vendor_eid, item_id, qty)
        except Exception as e:
            lang = self.io.lang_for(world, vendor_eid, None)
            return (f"No pude actualizar stock: {e}" if lang == 'es' else f"Couldn't update stock: {e}")

    # ---- Transactions -------------------------------------------------------
    def vendor_buy(self, world: Any, state: Any, vendor_eid: int, item_id: str, qty: int) -> None:
        vts = self._get_vendor_trade_system(world)
        try:
            result = vts.buy(world, vendor_eid, item_id, qty)
            state.chat_add_message('NPC', result)
            try:
                if self.io.mem_store is not None:
                    mem_key = self.io.memory_key(world, vendor_eid)
                    self.io.mem_store.append_ephemeral(mem_key, 'assistant', result)
                self.io.log_line(world, vendor_eid, 'NPC', result, role=None)
            except Exception:
                pass
            try:
                push_bubble(world, vendor_eid, result, color=(255, 235, 180), ttl_ms=3000)
            except Exception:
                pass
        except Exception as e:
            logger.exception("Vendor buy error")
            lang = self.io.lang_for(world, vendor_eid, state)
            text = self.io.tr(lang, f"No pude completar la compra: {e}", f"I couldn't complete the purchase: {e}")
            state.chat_add_message('NPC', text)
            try:
                push_bubble(world, vendor_eid, text, color=(255, 200, 200), ttl_ms=3000)
            except Exception:
                pass

    def vendor_sell(self, world: Any, state: Any, vendor_eid: int, item_id: str, qty: int) -> None:
        vts = self._get_vendor_trade_system(world)
        try:
            result = vts.sell(world, vendor_eid, item_id, qty)
            state.chat_add_message('NPC', result)
            try:
                if self.io.mem_store is not None:
                    mem_key = self.io.memory_key(world, vendor_eid)
                    self.io.mem_store.append_ephemeral(mem_key, 'assistant', result)
                self.io.log_line(world, vendor_eid, 'NPC', result, role=None)
            except Exception:
                pass
            try:
                push_bubble(world, vendor_eid, result, color=(255, 235, 180), ttl_ms=3000)
            except Exception:
                pass
        except Exception as e:
            logger.exception("Vendor sell error")
            lang = self.io.lang_for(world, vendor_eid, state)
            text = self.io.tr(lang, f"No pude completar la venta: {e}", f"I couldn't complete the sale: {e}")
            state.chat_add_message('NPC', text)
            try:
                push_bubble(world, vendor_eid, text, color=(255, 200, 200), ttl_ms=3000)
            except Exception:
                pass

    def ask_confirm(self, world: Any, state: Any, vendor_eid: int, *, op: str, item: str, qty: int, pending_confirms: dict[int, dict]) -> None:
        vts = self._get_vendor_trade_system(world)
        item_norm, _ = vts._normalize_ids(world, vendor_eid, (item or 'wood'))
        try:
            unit = vts._get_price(world, vendor_eid, item_norm, op=op) or 1
        except Exception:
            unit = 1
        lang = self.io.lang_for(world, vendor_eid, state)
        nice_es = 'madera' if item_norm == 'wood' else item_norm
        nice_en = 'wood' if item_norm == 'wood' else item_norm
        total = int(unit) * int(qty)
        if lang == 'es':
            verb = 'comprar' if op == 'buy' else 'vender'
            pre = f"Vas a {verb} {qty} de {nice_es} a {int(unit)} oro/u (total {total}). ¿Confirmas? (sí/no)"
        else:
            verb = 'buy' if op == 'buy' else 'sell'
            pre = f"You are going to {verb} {qty} of {nice_en} at {int(unit)} gold/ea (total {total}). Confirm? (yes/no)"
        pending_confirms[vendor_eid] = {'op': op, 'item': item_norm, 'qty': int(qty)}
        state.chat_add_message('NPC', pre)
        try:
            if self.io.mem_store is not None:
                mem_key = self.io.memory_key(world, vendor_eid)
                self.io.mem_store.append_ephemeral(mem_key, 'assistant', pre)
            self.io.log_line(world, vendor_eid, 'NPC', pre, role=None)
        except Exception:
            pass
        try:
            push_bubble(world, vendor_eid, pre, color=(255, 235, 180), ttl_ms=3200)
        except Exception:
            pass
