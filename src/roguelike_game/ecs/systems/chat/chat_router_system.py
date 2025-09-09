import re
import json
import logging
from pathlib import Path
from roguelike_game.ecs.systems.chat.chat_bubble_utils import push_bubble
from roguelike_game.chat.service.chat_service import ChatService, ChatJob
from roguelike_game.chat.service.chat_worker import ChatAsyncWorker

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
        try:
            # Resolver raíz del repo: .../src/roguelike_game/ecs/systems/chat/chat_router_system.py
            # parents: [0]=chat, [1]=systems, [2]=ecs, [3]=roguelike_game, [4]=src, [5]=<repo root>
            self._root = Path(__file__).resolve().parents[5]
        except Exception:
            self._root = Path('.')
        self._service = None  # deprecated cache; service se crea por mensaje
        # Worker asíncrono y tracking de trabajos
        self._worker = ChatAsyncWorker.instance()
        self._latest_job_for_target: dict[int, str] = {}
        self._job_meta: dict[str, dict] = {}

    def update(self, world, *args):
        state = getattr(world, 'state', None)
        if not state or not getattr(state, 'chat_open', False):
            return
        # 1) Procesar respuestas completadas sin bloquear
        self._drain_completed_jobs(world, state)
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
        persona_id = self._resolve_persona_id(world, target, chat)
        for msg in commits:
            self._route_message(world, state, role, persona_id, target, msg)

    def _route_message(self, world, state, role, persona_id, target_eid, msg: str):
        text = (msg or '').strip()
        if not text:
            return
        # Comandos directos (no IA) para vendor
        if role == 'vendor':
            # 1) Consulta de stock real (no pasa por IA)
            m_stock = re.match(r"^(?:!stock|muestra\s+stock|ver\s+stock|dime\s+stock)(?:\s+(?:de\s+)?(\w+))?$", text, flags=re.IGNORECASE)
            if m_stock:
                item = (m_stock.group(1) or 'wood').lower()
                txt = self._vendor_stock(world, target_eid, item)
                state.chat_add_message('NPC', txt)
                try:
                    push_bubble(world, target_eid, txt, color=(255,235,180), ttl_ms=2600)
                except Exception:
                    pass
                return
            m_stock_q = re.match(r"^(?:qu[eé]\s+stock\s+tienes\??|cu[aá]nt[oa]\s+(?:stock|madera|maderas)\s+(?:tienes|ten[ée]s)(?:\s+de\s+(\w+))?\??)$", text, flags=re.IGNORECASE)
            if m_stock_q:
                item = (m_stock_q.group(1) or 'wood').lower()
                txt = self._vendor_stock(world, target_eid, item)
                state.chat_add_message('NPC', txt)
                try:
                    push_bubble(world, target_eid, txt, color=(255,235,180), ttl_ms=2600)
                except Exception:
                    pass
                return
            # 1b) Consulta de oro disponible (no IA)
            m_gold = re.match(r"^(?:!gold|ver\s+oro|muestra\s+oro|cu[aá]nto\s+oro\s+(?:tienes|ten[ée]s)\??)$", text, flags=re.IGNORECASE)
            if m_gold:
                txt = self._vendor_gold(world, target_eid)
                state.chat_add_message('NPC', txt)
                try:
                    push_bubble(world, target_eid, txt, color=(255,235,180), ttl_ms=2600)
                except Exception:
                    pass
                return
            # 2) Comandos admin rápidos (no IA) para pruebas: restock
            m = re.match(r"^!restock\s+(\d+)\s*(\w+)?$", text, flags=re.IGNORECASE)
            if m:
                qty = int(m.group(1))
                item = (m.group(2) or 'wood').lower()
                out = self._vendor_restock(world, target_eid, item, qty)
                state.chat_add_message('NPC', out)
                try:
                    push_bubble(world, target_eid, out, color=(200, 240, 200), ttl_ms=2400)
                except Exception:
                    pass
                return
            m2 = re.match(r"^(agrega|añade|sumar)\s+(\d+)\s+(madera|wood|wooden)$", text, flags=re.IGNORECASE)
            if m2:
                qty = int(m2.group(2))
                out = self._vendor_restock(world, target_eid, 'wood', qty)
                state.chat_add_message('NPC', out)
                try:
                    push_bubble(world, target_eid, out, color=(200, 240, 200), ttl_ms=2400)
                except Exception:
                    pass
                return
        # Construir historial para el LLM (mapear 'Tú'->user, resto->assistant)
        history = []
        try:
            for sender, line in getattr(state, 'chat_messages', [])[-10:]:
                r = 'user' if str(sender).lower() in {'tú', 'tu', 'you'} else 'assistant'
                history.append({"role": r, "content": str(line)})
        except Exception:
            pass
        # Enviar a ChatService en background (no bloquear el loop)
        player_id = getattr(world, 'player_entity', None) or -1
        job = ChatJob(
            player_id=player_id,
            npc_id=target_eid,
            user_text=text,
            role=str(role),
            persona_id=str(persona_id or ''),
            history=history,
        )
        job_id = self._worker.submit(job)
        # Trackear último job por target y metadatos para aplicar luego
        self._latest_job_for_target[target_eid] = job_id
        self._job_meta[job_id] = {
            'target': target_eid,
            'role': role,
            'persona_id': persona_id,
        }
        # Indicar que el NPC está "escribiendo"
        try:
            state.chat_typing = True
            push_bubble(world, target_eid, '…', color=(220, 220, 220), ttl_ms=1000)
        except Exception:
            pass
        # La respuesta se aplicará en _drain_completed_jobs()

    def _resolve_persona_id(self, world, target_eid, chat_comp):
        pid = getattr(chat_comp, 'persona_id', None) if chat_comp else None
        if pid:
            return pid
        # Fallback: assignments.json
        try:
            ident = world.components.get('Identity', {}).get(target_eid)
            ent_key = getattr(ident, 'name', None) or getattr(ident, 'id', None)
            if not ent_key:
                return None
            root = getattr(self, '_root', Path('.'))
            ap = root / 'data' / 'chat' / 'assignments.json'
            with ap.open('r', encoding='utf-8') as f:
                data = json.load(f)
            node = data.get(str(ent_key)) or data.get(ent_key)
            if isinstance(node, dict):
                return node.get('persona_id')
        except Exception:
            return None
        return None

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

    def _vendor_stock(self, world, vendor_eid: int, item_id: str = 'wood') -> str:
        """Construye un mensaje de stock real del vendedor para `item_id` (por defecto madera)."""
        try:
            invs = world.components.get('InventoryComponent', {})
            inv = invs.get(vendor_eid)
            qty = 0
            if inv and hasattr(inv, 'slots'):
                for st in getattr(inv, 'slots', []) or []:
                    try:
                        iid = str(getattr(st, 'item_id', '')).lower()
                        target = (item_id or 'wood').lower()
                        if target in {'wooden', 'madera'}:
                            target = 'wood'
                        if st and iid == target:
                            qty += int(getattr(st, 'quantity', 0) or 0)
                    except Exception:
                        pass
            # Obtener precio actual
            vts = self._get_vendor_trade_system(world)
            target_item = (item_id or 'wood').lower()
            if target_item in {'wooden', 'madera'}:
                target_item = 'wood'
            price = vts._get_price(world, vendor_eid, target_item, op='buy') or 1
            # Nombre amigable
            nice = 'madera' if target_item == 'wood' else target_item
            return f"Tengo {qty} de {nice} a {int(price)} oro la unidad."
        except Exception:
            return "Tengo stock a 1 oro la unidad."

    def _vendor_restock(self, world, vendor_eid: int, item_id: str, qty: int) -> str:
        vts = self._get_vendor_trade_system(world)
        try:
            return vts.restock(world, vendor_eid, item_id, qty)
        except Exception as e:
            return f"No pude actualizar stock: {e}"

    def _vendor_gold(self, world, vendor_eid: int) -> str:
        """Devuelve el oro disponible del vendedor sin pasar por la IA."""
        vts = self._get_vendor_trade_system(world)
        try:
            gold = int(vts.get_stock(world, vendor_eid, 'gold'))
        except Exception:
            gold = 0
        return f"Tengo {gold} de oro disponible para pagar."

    def _drain_completed_jobs(self, world, state):
        """Procesa resultados completados del ChatAsyncWorker sin bloquear."""
        completed = self._worker.poll_completed(max_items=8)
        for job_id, result in completed:
            meta = self._job_meta.pop(job_id, None)
            if not meta:
                continue
            target_eid = meta.get('target')
            role = meta.get('role')
            # Evitar respuestas obsoletas si hubo un mensaje más nuevo para el mismo target
            if self._latest_job_for_target.get(target_eid) != job_id:
                continue
            try:
                state.chat_typing = False
            except Exception:
                pass

            responded = False
            # Ejecutar tool-calls con los sistemas reales (VendorTradeSystem) para efectos en ECS
            if getattr(result, 'tool_calls', None):
                for call in result.tool_calls:
                    name = getattr(call, 'name', '')
                    args = getattr(call, 'arguments', {}) or {}
                    if name == 'vendor.buy' and role == 'vendor':
                        qty = int(args.get('quantity', 1))
                        item = str(args.get('item', 'wood')).lower()
                        self._vendor_buy(world, state, target_eid, item, qty)
                        responded = True
                    elif name == 'vendor.sell' and role == 'vendor':
                        qty = int(args.get('quantity', 1))
                        item = str(args.get('item', 'wood')).lower()
                        self._vendor_sell(world, state, target_eid, item, qty)
                        responded = True
                    elif name == 'vendor.stock' and role == 'vendor':
                        txt = self._vendor_stock(world, target_eid, 'wood')
                        state.chat_add_message('NPC', txt)
                        try:
                            push_bubble(world, target_eid, txt, color=(255,235,180), ttl_ms=2600)
                        except Exception:
                            pass
                        responded = True
            # Si no hubo tool-calls o nada aplicable, usar el texto de respuesta
            if not responded:
                reply = (getattr(result, 'text', None) or '').strip()
                if not reply:
                    reply = 'No entiendo. Usa "buy N wood" o "sell N wood".'
                state.chat_add_message('NPC', reply)
                try:
                    push_bubble(world, target_eid, reply, color=(255, 235, 180), ttl_ms=2600)
                except Exception:
                    pass
            # Mensaje de depuración suave si hubo fallback offline
            try:
                if getattr(result, 'offline', False):
                    info = '(modo offline)'
                    state.chat_add_message('NPC', info)
            except Exception:
                pass
