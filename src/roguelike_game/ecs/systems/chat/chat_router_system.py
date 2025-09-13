import re
import json
import logging
from pathlib import Path
import os
import uuid
from datetime import datetime
import pygame
from roguelike_game.ecs.systems.chat.chat_bubble_utils import push_bubble
from roguelike_engine.chat.service.chat_service import ChatService, ChatJob
from roguelike_engine.chat.service.chat_worker import ChatAsyncWorker
from roguelike_engine.chat.service.memory_store import MemoryStore

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
        # Memory & session logging
        try:
            self._mem_store = MemoryStore(getattr(self, '_root', Path('.')))
        except Exception:
            self._mem_store = None
        try:
            self._session_id = uuid.uuid4().hex[:8]
            self._log_dir = getattr(self, '_root', Path('.')) / 'logs' / 'chat_sessions'
            os.makedirs(self._log_dir, exist_ok=True)
        except Exception:
            self._log_dir = None
        # Scheduler interno para mensajes troceados (chat + burbujas)
        # Cada item: {due:int(ms), type:'chat'|'bubble', data:{...}}
        self._scheduled: list[dict] = []
        # Confirmaciones pendientes por target_eid
        # Valor: {'op': 'buy'|'sell', 'item': str, 'qty': int}
        self._pending_confirms: dict[int, dict] = {}

    # --- Logging helper ------------------------------------------------------
    def _log_line(self, npc_eid: int, sender: str, text: str) -> None:
        try:
            if not self._log_dir:
                return
            # Un archivo por sesión y por NPC
            fname = f"sess-{self._session_id}_npc-{int(npc_eid)}.log"
            path = self._log_dir / fname
            with path.open('a', encoding='utf-8') as f:
                f.write(f"[{datetime.now().isoformat(timespec='seconds')}] {sender}: {text}\n")
        except Exception:
            pass

    def update(self, world, *args):
        state = getattr(world, 'state', None)
        if not state or not getattr(state, 'chat_open', False):
            return
        # 1) Procesar respuestas completadas sin bloquear
        self._drain_completed_jobs(world, state)
        # 1b) Disparar mensajes/burbujas programados (trozos)
        self._process_scheduled(world, state)
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
                # Mensaje sin target: localizar según preferencia global si existe
                lang = getattr(state, 'chat_lang_preference', 'es') or 'es'
                text = 'No tengo a nadie al frente para hablar.' if lang == 'es' else 'I have no one in front to talk to.'
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
        # Persistir historial efímero del usuario y log de sesión
        try:
            if self._mem_store is not None and target_eid is not None:
                self._mem_store.append_ephemeral(str(target_eid), 'user', text)
            self._log_line(target_eid, 'USER', text)
        except Exception:
            pass
        # 0) Si hay confirmación pendiente para este target, interpretar sí/no
        pend = self._pending_confirms.get(target_eid)
        if pend:
            if self._is_affirmative(text):
                # Ejecutar la operación confirmada
                op = pend.get('op')
                item = pend.get('item')
                qty = int(pend.get('qty') or 1)
                try:
                    # Limpia antes para evitar loops si hay errores
                    self._pending_confirms.pop(target_eid, None)
                    if op == 'buy':
                        self._vendor_buy(world, state, target_eid, item, qty)
                    elif op == 'sell':
                        self._vendor_sell(world, state, target_eid, item, qty)
                    else:
                        msg2 = 'Operación no reconocida.'
                        state.chat_add_message('NPC', msg2)
                        try:
                            if self._mem_store is not None:
                                self._mem_store.append_ephemeral(str(target_eid), 'assistant', msg2)
                            self._log_line(target_eid, 'NPC', msg2)
                        except Exception:
                            pass
                except Exception:
                    pass
                return
            if self._is_negative(text):
                self._pending_confirms.pop(target_eid, None)
                lang = self._lang_for(target_eid, state)
                cancel_txt = self._tr(lang, 'Operación cancelada.', 'Operation cancelled.')
                state.chat_add_message('NPC', cancel_txt)
                try:
                    if self._mem_store is not None:
                        self._mem_store.append_ephemeral(str(target_eid), 'assistant', cancel_txt)
                    self._log_line(target_eid, 'NPC', cancel_txt)
                except Exception:
                    pass
                try:
                    push_bubble(world, target_eid, cancel_txt, color=(255, 200, 200), ttl_ms=2000)
                except Exception:
                    pass
                return
            # Si no es sí/no, aclarar
            lang = self._lang_for(target_eid, state)
            ask = self._tr(lang, 'Por favor responde "sí" para confirmar o "no" para cancelar.', 'Please answer "yes" to confirm or "no" to cancel.')
            state.chat_add_message('NPC', ask)
            try:
                if self._mem_store is not None:
                    self._mem_store.append_ephemeral(str(target_eid), 'assistant', ask)
                self._log_line(target_eid, 'NPC', ask)
            except Exception:
                pass
            try:
                push_bubble(world, target_eid, ask, color=(255, 235, 180), ttl_ms=2400)
            except Exception:
                pass
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
                    if self._mem_store is not None:
                        self._mem_store.append_ephemeral(str(target_eid), 'assistant', txt)
                    self._log_line(target_eid, 'NPC', txt)
                except Exception:
                    pass
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
                    if self._mem_store is not None:
                        self._mem_store.append_ephemeral(str(target_eid), 'assistant', txt)
                    self._log_line(target_eid, 'NPC', txt)
                except Exception:
                    pass
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
                    if self._mem_store is not None:
                        self._mem_store.append_ephemeral(str(target_eid), 'assistant', txt)
                    self._log_line(target_eid, 'NPC', txt)
                except Exception:
                    pass
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
                    if self._mem_store is not None:
                        self._mem_store.append_ephemeral(str(target_eid), 'assistant', out)
                    self._log_line(target_eid, 'NPC', out)
                except Exception:
                    pass
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
                    if self._mem_store is not None:
                        self._mem_store.append_ephemeral(str(target_eid), 'assistant', out)
                    self._log_line(target_eid, 'NPC', out)
                except Exception:
                    pass
                try:
                    push_bubble(world, target_eid, out, color=(200, 240, 200), ttl_ms=2400)
                except Exception:
                    pass
                return
            # 3) Comandos de compra/venta con confirmación previa
            m_buy = re.match(r"^(?:buy|comprar|c[oó]mprame|c[oó]mprar)\s+(\d+)\s*(\w+)?$", text, flags=re.IGNORECASE)
            if m_buy:
                qty = int(m_buy.group(1))
                item = (m_buy.group(2) or 'wood').lower()
                if item in {'wooden', 'madera'}:
                    item = 'wood'
                self._ask_vendor_confirm(world, state, target_eid, op='buy', item=item, qty=qty)
                return
            m_sell = re.match(r"^(?:sell|vender|v[eé]ndeme|vende)\s+(\d+)\s*(\w+)?$", text, flags=re.IGNORECASE)
            if m_sell:
                qty = int(m_sell.group(1))
                item = (m_sell.group(2) or 'wood').lower()
                if item in {'wooden', 'madera'}:
                    item = 'wood'
                self._ask_vendor_confirm(world, state, target_eid, op='sell', item=item, qty=qty)
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
        # Persistir inmediatamente la preferencia de idioma actual del selector (si existe)
        try:
            ui_lang = (getattr(state, 'chat_lang_preference', None) or '').strip().lower()
            if ui_lang in {'es', 'en'}:
                ms = MemoryStore(getattr(self, '_root', Path('.')))
                ms.set_language(str(target_eid), ui_lang)
        except Exception:
            pass
        # Estimar status online/offline antes de enviar (para mostrar en el título inmediatamente)
        try:
            est = self._estimate_online_status()
            state.chat_llm_online_estimated = bool(est)
        except Exception:
            try:
                state.chat_llm_online_estimated = False
            except Exception:
                pass
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
                if self._mem_store is not None:
                    self._mem_store.append_ephemeral(str(vendor_eid), 'assistant', result)
                self._log_line(vendor_eid, 'NPC', result)
            except Exception:
                pass
            try:
                push_bubble(world, vendor_eid, result, color=(255, 235, 180), ttl_ms=3000)
            except Exception:
                pass
        except Exception as e:
            logger.exception("Vendor buy error")
            lang = self._lang_for(vendor_eid)
            text = self._tr(lang, f"No pude completar la compra: {e}", f"I couldn't complete the purchase: {e}")
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
                if self._mem_store is not None:
                    self._mem_store.append_ephemeral(str(vendor_eid), 'assistant', result)
                self._log_line(vendor_eid, 'NPC', result)
            except Exception:
                pass
            try:
                push_bubble(world, vendor_eid, result, color=(255, 235, 180), ttl_ms=3000)
            except Exception:
                pass
        except Exception as e:
            logger.exception("Vendor sell error")
            lang = self._lang_for(vendor_eid)
            text = self._tr(lang, f"No pude completar la venta: {e}", f"I couldn't complete the sale: {e}")
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
        """Construye un mensaje de stock real del vendedor para `item_id` (por defecto madera), localizado."""
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
            # Localización
            lang = self._lang_for(vendor_eid)
            if lang == 'es':
                nice = 'madera' if target_item == 'wood' else target_item
                return f"Tengo {qty} de {nice} a {int(price)} oro la unidad."
            else:
                nice = 'wood' if target_item == 'wood' else target_item
                return f"I have {qty} of {nice} at {int(price)} gold each."
        except Exception:
            lang = self._lang_for(vendor_eid)
            return "Tengo stock a 1 oro la unidad." if lang == 'es' else "I have stock at 1 gold each."

    def _vendor_restock(self, world, vendor_eid: int, item_id: str, qty: int) -> str:
        vts = self._get_vendor_trade_system(world)
        try:
            return vts.restock(world, vendor_eid, item_id, qty)
        except Exception as e:
            lang = self._lang_for(vendor_eid)
            return (f"No pude actualizar stock: {e}" if lang == 'es' else f"Couldn't update stock: {e}")

    def _vendor_gold(self, world, vendor_eid: int) -> str:
        """Devuelve el oro disponible del vendedor sin pasar por la IA, localizado."""
        vts = self._get_vendor_trade_system(world)
        try:
            gold = int(vts.get_stock(world, vendor_eid, 'gold'))
        except Exception:
            gold = 0
        lang = self._lang_for(vendor_eid)
        return (f"Tengo {gold} de oro disponible para pagar." if lang == 'es' else f"I have {gold} gold available to pay.")

    def _ask_vendor_confirm(self, world, state, vendor_eid: int, *, op: str, item: str, qty: int) -> None:
        """Pide confirmación antes de ejecutar buy/sell. Muestra precio unitario y total si es posible (localizado)."""
        vts = self._get_vendor_trade_system(world)
        item_norm = (item or 'wood').lower()
        if item_norm in {'wooden', 'madera'}:
            item_norm = 'wood'
        try:
            unit = vts._get_price(world, vendor_eid, item_norm, op=op) or 1
        except Exception:
            unit = 1
        lang = self._lang_for(vendor_eid)
        nice_es = 'madera' if item_norm == 'wood' else item_norm
        nice_en = 'wood' if item_norm == 'wood' else item_norm
        total = int(unit) * int(qty)
        if lang == 'es':
            verb = 'comprar' if op == 'buy' else 'vender'
            pre = f"Vas a {verb} {qty} de {nice_es} a {int(unit)} oro/u (total {total}). ¿Confirmas? (sí/no)"
        else:
            verb = 'buy' if op == 'buy' else 'sell'
            pre = f"You are going to {verb} {qty} of {nice_en} at {int(unit)} gold/ea (total {total}). Confirm? (yes/no)"
        # Registrar pendiente y preguntar
        self._pending_confirms[vendor_eid] = {'op': op, 'item': item_norm, 'qty': int(qty)}
        state.chat_add_message('NPC', pre)
        try:
            if self._mem_store is not None:
                self._mem_store.append_ephemeral(str(vendor_eid), 'assistant', pre)
            self._log_line(vendor_eid, 'NPC', pre)
        except Exception:
            pass
        try:
            push_bubble(world, vendor_eid, pre, color=(255, 235, 180), ttl_ms=3200)
        except Exception:
            pass

    def _is_affirmative(self, text: str) -> bool:
        return bool(re.match(r"^(?:s[ií]|si|yes|ok|vale|de\s*acuerdo|confirmo|acepto)$", text.strip(), flags=re.IGNORECASE))

    def _is_negative(self, text: str) -> bool:
        return bool(re.match(r"^(?:no|cancel[aá]r?|cancelo|mejor\s+no)$", text.strip(), flags=re.IGNORECASE))

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
                        self._ask_vendor_confirm(world, state, target_eid, op='buy', item=item, qty=qty)
                        responded = True
                    elif name == 'vendor.sell' and role == 'vendor':
                        qty = int(args.get('quantity', 1))
                        item = str(args.get('item', 'wood')).lower()
                        self._ask_vendor_confirm(world, state, target_eid, op='sell', item=item, qty=qty)
                        responded = True
                    elif name == 'vendor.stock' and role == 'vendor':
                        txt = self._vendor_stock(world, target_eid, 'wood')
                        state.chat_add_message('NPC', txt)
                        try:
                            if self._mem_store is not None:
                                self._mem_store.append_ephemeral(str(target_eid), 'assistant', txt)
                            self._log_line(target_eid, 'NPC', txt)
                        except Exception:
                            pass
                        try:
                            push_bubble(world, target_eid, txt, color=(255,235,180), ttl_ms=2600)
                        except Exception:
                            pass
                        responded = True
            # Si no hubo tool-calls o nada aplicable, usar el texto de respuesta
            if not responded:
                reply = (getattr(result, 'text', None) or '').strip()
                if not reply:
                    lang = self._lang_for(target_eid, state)
                    reply = self._tr(lang, 'No entiendo. Usa "buy N wood" o "sell N wood".', 'I don\'t understand. Use "buy N wood" or "sell N wood".')
                # Persistir historial efímero del asistente y log (mensaje completo)
                try:
                    if self._mem_store is not None:
                        self._mem_store.append_ephemeral(str(target_eid), 'assistant', reply)
                    self._log_line(target_eid, 'NPC', reply)
                except Exception:
                    pass
                # Programar respuesta en trozos de 8 palabras, con 3s entre partes
                last_due, placeholder_idx = self._schedule_reply_chunks(
                    world,
                    state,
                    target_eid,
                    reply,
                    color=(255, 235, 180),
                    words_per_chunk=8,
                    delay_ms=3000,
                    ttl_ms=2600,
                )
            # Mensaje de depuración suave si hubo fallback offline
            try:
                if getattr(result, 'offline', False):
                    # Confirmar estado offline para la UI
                    try:
                        state.chat_llm_online = False
                    except Exception:
                        pass
                    # Añadir sufijo en el mismo mensaje del panel al finalizar
                    if 'last_due' in locals() and last_due is not None and 'placeholder_idx' in locals() and placeholder_idx is not None:
                        lang = self._lang_for(target_eid, state)
                        suffix = self._tr(lang, ' (modo offline)', ' (offline mode)')
                        self._scheduled.append({
                            'due': int(last_due),
                            'type': 'chat_append_suffix',
                            'data': {'idx': int(placeholder_idx), 'suffix': suffix}
                        })
                else:
                    # Confirmar estado online para la UI
                    try:
                        state.chat_llm_online = True
                    except Exception:
                        pass
            except Exception:
                pass

    # --- Scheduler de trozos -------------------------------------------------
    def _process_scheduled(self, world, state) -> None:
        """Dispara elementos programados cuyo due <= now.

        Cada elemento puede ser:
          - type='chat': data={'sender': str, 'text': str}
          - type='bubble': data={'eid': int, 'text': str, 'color': (r,g,b), 'ttl': int}
        """
        if not self._scheduled:
            return
        try:
            now = pygame.time.get_ticks()
        except Exception:
            return
        remain: list[dict] = []
        for item in self._scheduled:
            try:
                due = int(item.get('due', 0) or 0)
                if now < due:
                    remain.append(item)
                    continue
                typ = item.get('type')
                data = item.get('data') or {}
                if typ == 'chat':
                    sender = data.get('sender', 'NPC')
                    text = data.get('text', '')
                    state.chat_add_message(str(sender), str(text))
                elif typ == 'chat_set':
                    # Establece/actualiza el texto de un mensaje existente (placeholder de respuesta)
                    idx = int(data.get('idx', -1))
                    sender = data.get('sender', 'NPC')
                    text = data.get('text', '')
                    try:
                        if 0 <= idx < len(state.chat_messages):
                            state.chat_messages[idx] = (str(sender), str(text))
                        else:
                            state.chat_add_message(str(sender), str(text))
                    except Exception:
                        state.chat_add_message(str(sender), str(text))
                elif typ == 'chat_append_suffix':
                    idx = int(data.get('idx', -1))
                    suffix = str(data.get('suffix', ''))
                    try:
                        if 0 <= idx < len(state.chat_messages):
                            sender, cur = state.chat_messages[idx]
                            state.chat_messages[idx] = (str(sender), str(cur) + suffix)
                    except Exception:
                        pass
                elif typ == 'bubble':
                    eid = data.get('eid')
                    text = data.get('text', '')
                    color = data.get('color') or (255, 255, 255)
                    ttl = int(data.get('ttl', 2500))
                    push_bubble(world, int(eid), str(text), color=tuple(color), ttl_ms=ttl)
                else:
                    # desconocido -> ignorar
                    pass
            except Exception:
                # Mantener robustez: descartar en error para no bloquear
                continue
        self._scheduled = remain

    def _schedule_reply_chunks(
        self,
        world,
        state,
        target_eid: int,
        text: str,
        *,
        color=(255, 235, 180),
        words_per_chunk: int = 12,
        delay_ms: int = 2000,
        ttl_ms: int = 2600,
    ) -> tuple[int, int | None]:
        """Trocea `text` por palabras y agenda cada parte para chat + burbuja.

        En el panel de chat, usa un ÚNICO mensaje que se va completando:
        - Inserta un placeholder inicial '…'
        - En cada trozo, actualiza ese mismo mensaje: acumulado + (' …' si no es el último)

        Devuelve (last_due_ms, placeholder_idx) para permitir añadir sufijos al final.
        """
        try:
            words = (text or '').split()
        except Exception:
            words = [text] if text else []
        if not words:
            # Programar vacío directo con placeholder
            now = pygame.time.get_ticks()
            # Crear placeholder en historial
            try:
                state.chat_add_message('NPC', '…')
                placeholder_idx = len(state.chat_messages) - 1
            except Exception:
                placeholder_idx = None
            # Asegurar un set inmediato a vacío (sin puntos)
            self._scheduled.append({'due': now, 'type': 'chat_set', 'data': {'idx': placeholder_idx if placeholder_idx is not None else -1, 'sender': 'NPC', 'text': ''}})
            return now, placeholder_idx
        chunks = []
        i = 0
        n = max(1, int(words_per_chunk))
        while i < len(words):
            chunk = ' '.join(words[i:i+n])
            chunks.append(chunk)
            i += n
        now = pygame.time.get_ticks()
        last_due = now
        # Crear placeholder en el historial
        try:
            state.chat_add_message('NPC', '…')
            placeholder_idx = len(state.chat_messages) - 1
        except Exception:
            placeholder_idx = None
        agg = ''
        for i, chunk in enumerate(chunks):
            due = now + i * int(delay_ms)
            last_due = due
            agg = (agg + ' ' + chunk).strip()
            display = agg if (i == len(chunks) - 1) else (agg + ' …')
            # Actualizar el mismo mensaje en el panel (placeholder)
            self._scheduled.append({
                'due': due,
                'type': 'chat_set',
                'data': {'idx': placeholder_idx if placeholder_idx is not None else -1, 'sender': 'NPC', 'text': display}
            })
            # Burbuja flotante encima del NPC (cada trozo)
            self._scheduled.append({
                'due': due,
                'type': 'bubble',
                'data': {'eid': int(target_eid), 'text': chunk, 'color': tuple(color), 'ttl': int(ttl_ms)}
            })
        return last_due, placeholder_idx

    # --- Localización sencilla ---------------------------------------------
    def _lang_for(self, npc_eid: int, state=None) -> str:
        """Determina el idioma actual para un NPC.

        Prioriza el idioma del selector en `state.chat_lang_preference` si está definido
        (para reflejar cambios mid-conversación). Si no, usa la preferencia persistida
        en MemoryStore. Devuelve siempre 'es' o 'en'.
        """
        try:
            if state is not None:
                ui_lang = (getattr(state, 'chat_lang_preference', None) or '').strip().lower()
                if ui_lang in {'es', 'en'}:
                    return ui_lang
        except Exception:
            pass
        try:
            ms = MemoryStore(getattr(self, '_root', Path('.')))
            code = (ms.get_language(str(npc_eid)) or 'es').lower()
            return 'en' if code == 'en' else 'es'
        except Exception:
            return 'es'

    def _tr(self, code: str, es_text: str, en_text: str) -> str:
        return es_text if (code or 'es') == 'es' else en_text

    # --- Online/offline estimation -----------------------------------------
    def _estimate_online_status(self) -> bool:
        """Estima si el proveedor estará online consultando chat.json y OPENAI_API_KEY.

        - Si provider == 'dummy' -> offline
        - Si falta OPENAI_API_KEY -> offline
        - En caso contrario -> online
        """
        try:
            root = getattr(self, '_root', Path('.'))
            cfg_path = root / 'data' / 'config' / 'chat.json'
            prov = 'dummy'
            if cfg_path.exists():
                with cfg_path.open('r', encoding='utf-8') as f:
                    obj = json.load(f)
                    prov = str(obj.get('provider', 'dummy')).lower()
            if prov == 'dummy':
                return False
            if not os.getenv('OPENAI_API_KEY'):
                return False
            return True
        except Exception:
            return False
