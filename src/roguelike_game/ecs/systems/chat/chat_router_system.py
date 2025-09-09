import re
import logging
from roguelike_game.ecs.systems.chat.chat_bubble_utils import push_bubble

logger = logging.getLogger(__name__)

class ChatRouterSystem:
    """
    Lee commits de entrada del ChatInputController y los enruta según el rol del NPC
    objetivo (definido por `ChatComponent.role`).

    Comandos soportados (inicial):
      - "buy N [item]" / "comprar N [item]"  (por defecto item="wood")
      - "sell N [item]" / "vender N [item]"  (por defecto item="wood")
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    def update(self, world, *args):
        state = getattr(world, 'state', None)
        if not state or not getattr(state, 'chat_open', False):
            return
        # Obtener commits del controlador
        ctrl = getattr(world, '_chat_input_ctrl', None)
        if ctrl is None:
            return
        commits = ctrl.get_commits()
        if not commits:
            return
        target = state.chat_target_eid
        if target is None:
            for msg in commits:
                text = 'No tengo a nadie al frente para hablar.'
                state.chat_add_message('NPC', text)
                try:
                    player_eid = getattr(world, 'player_entity', None)
                    if player_eid is not None:
                        push_bubble(world, player_eid, text, color=(255, 235, 180), ttl_ms=2600)
                except Exception:
                    pass
            return
        chat = world.components.get('ChatComponent', {}).get(target)
        role = getattr(chat, 'role', 'generic') if chat else 'generic'
        for msg in commits:
            self._route_message(world, state, role, target, msg)

    def _route_message(self, world, state, role, target_eid, msg: str):
        text = (msg or '').strip()
        if not text:
            return
        # Parse básico
        m = re.match(r'^(buy|comprar)\s+(\d+)(?:\s+(\w+))?$', text, re.IGNORECASE)
        if m:
            qty = int(m.group(2))
            item = (m.group(3) or 'wood').lower()
            if role == 'vendor':
                self._vendor_buy(world, state, target_eid, item, qty)
            else:
                text = 'No soy un vendedor.'
                state.chat_add_message('NPC', text)
                try:
                    push_bubble(world, target_eid, text, color=(255, 235, 180), ttl_ms=2600)
                except Exception:
                    pass
            return
        m = re.match(r'^(sell|vender)\s+(\d+)(?:\s+(\w+))?$', text, re.IGNORECASE)
        if m:
            qty = int(m.group(2))
            item = (m.group(3) or 'wood').lower()
            if role == 'vendor':
                self._vendor_sell(world, state, target_eid, item, qty)
            else:
                text = 'No soy un vendedor.'
                state.chat_add_message('NPC', text)
                try:
                    push_bubble(world, target_eid, text, color=(255, 235, 180), ttl_ms=2600)
                except Exception:
                    pass
            return
        # Comando no reconocido
        text = 'No entiendo. Usa "buy N [item]" o "sell N [item]".'
        state.chat_add_message('NPC', text)
        try:
            push_bubble(world, target_eid, text, color=(255, 235, 180), ttl_ms=2600)
        except Exception:
            pass

    def _vendor_buy(self, world, state, vendor_eid, item_id: str, qty: int):
        # Delegar a VendorTradeSystem
        vts = self._get_vendor_trade_system(world)
        try:
            result = vts.buy(world, vendor_eid, item_id, qty)
            state.chat_add_message('NPC', result)
            try:
                push_bubble(world, vendor_eid, result, color=(255, 235, 180), ttl_ms=3000)
            except Exception:
                pass
        except Exception as e:
            logger.exception("Vendor buy error")
            text = f"No pude completar la compra: {e}"
            state.chat_add_message('NPC', text)
            try:
                push_bubble(world, vendor_eid, text, color=(255, 200, 200), ttl_ms=3000)
            except Exception:
                pass

    def _vendor_sell(self, world, state, vendor_eid, item_id: str, qty: int):
        vts = self._get_vendor_trade_system(world)
        try:
            result = vts.sell(world, vendor_eid, item_id, qty)
            state.chat_add_message('NPC', result)
            try:
                push_bubble(world, vendor_eid, result, color=(255, 235, 180), ttl_ms=3000)
            except Exception:
                pass
        except Exception as e:
            logger.exception("Vendor sell error")
            text = f"No pude completar la venta: {e}"
            state.chat_add_message('NPC', text)
            try:
                push_bubble(world, vendor_eid, text, color=(255, 200, 200), ttl_ms=3000)
            except Exception:
                pass

    def _get_vendor_trade_system(self, world):
        for s in getattr(world, 'update_systems', []):
            if type(s).__name__ == 'VendorTradeSystem':
                return s
        # Fallback: crear uno ad-hoc
        from roguelike_game.ecs.systems.vendors.vendor_trade_system import VendorTradeSystem
        inst = VendorTradeSystem()
        world.update_systems.append(inst)
        return inst
