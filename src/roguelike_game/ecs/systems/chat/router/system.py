from __future__ import annotations

import logging
from pathlib import Path
from typing import Any

from roguelike_game.ecs.systems.chat.chat_bubble_utils import push_bubble
from roguelike_engine.chat.service.chat_service import ChatJob
from roguelike_engine.chat.service.chat_worker import ChatAsyncWorker

from .io_utils import ChatIO
from .message_scheduler import MessageScheduler
from .vendor_service import VendorService
from .command_parser import parse_vendor_intent, is_affirmative, is_negative
from roguelike_game.ecs.systems.vendors.services import EconomyService

logger = logging.getLogger(__name__)


class ChatRouterSystem:
    """Routes chat input to vendor commands or LLM responses.

    Keeps external API stable. Internals are delegated to small collaborators
    for readability and testability.
    """

    def __init__(self, perf_log: Any | None = None) -> None:
        self.perf_log = perf_log
        try:
            # repo root: .../src/roguelike_game/ecs/systems/chat/router/system.py
            self._root = Path(__file__).resolve().parents[6]
        except Exception:
            self._root = Path('.')
        self.worker = ChatAsyncWorker.instance()
        self.latest_job_for_target: dict[int, str] = {}
        self.job_meta: dict[str, dict] = {}
        self.pending_confirms: dict[int, dict] = {}

        self.io = ChatIO(self._root)
        self.scheduler = MessageScheduler()
        self.vendor = VendorService(self.io)
        # Preloader para allowed ids por tipo desde SQLite
        self._economy_preloader = EconomyService()

    # ------------------------------------------------------------------
    def update(self, world: Any, *args: Any) -> None:
        state = getattr(world, 'state', None)
        if not state or not getattr(state, 'chat_open', False):
            return
        # Drain background results and scheduled items first
        self._drain_completed_jobs(world, state)
        self.scheduler.process(world, state)

        ctrl = getattr(world, '_chat_input_ctrl', None)
        if ctrl is None:
            return
        commits = ctrl.get_commits()
        if not commits:
            return

        target = state.chat_target_eid
        if target is None:
            for _ in commits:
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
        persona_id = self.io.resolve_persona_id(world, target, chat)
        # Precargar ids permitidos por tipo para vendors (consulta a SQLite)
        if role == 'vendor':
            try:
                self._economy_preloader.preload_allowed_ids(world, target)
            except Exception:
                pass
        for msg in commits:
            self._route_message(world, state, role, persona_id, target, msg)

    # ------------------------------------------------------------------
    def _route_message(self, world: Any, state: Any, role: str, persona_id: str | None, target_eid: int, msg: str) -> None:
        text = (msg or '').strip()
        if not text:
            return
        # Persist ephemeral user message + log
        try:
            if self.io.mem_store is not None and target_eid is not None:
                mem_key = self.io.memory_key(world, target_eid)
                self.io.mem_store.append_ephemeral(mem_key, 'user', text)
            self.io.log_line(world, target_eid, 'USER', text, role)
        except Exception:
            pass

        # Pending confirmations
        pend = self.pending_confirms.get(target_eid)
        if pend:
            if is_affirmative(text):
                op = pend.get('op')
                item = pend.get('item')
                qty = int(pend.get('qty') or 1)
                try:
                    self.pending_confirms.pop(target_eid, None)
                    if op == 'buy':
                        self.vendor.vendor_buy(world, state, target_eid, item, qty)
                    elif op == 'sell':
                        self.vendor.vendor_sell(world, state, target_eid, item, qty)
                    else:
                        msg2 = 'Operación no reconocida.'
                        state.chat_add_message('NPC', msg2)
                        try:
                            if self.io.mem_store is not None:
                                mem_key = self.io.memory_key(world, target_eid)
                                self.io.mem_store.append_ephemeral(mem_key, 'assistant', msg2)
                            self.io.log_line(world, target_eid, 'NPC', msg2, role)
                        except Exception:
                            pass
                except Exception:
                    pass
                return
            if is_negative(text):
                self.pending_confirms.pop(target_eid, None)
                lang = self.io.lang_for(world, target_eid, state)
                cancel_txt = self.io.tr(lang, 'Operación cancelada.', 'Operation cancelled.')
                state.chat_add_message('NPC', cancel_txt)
                try:
                    if self.io.mem_store is not None:
                        mem_key = self.io.memory_key(world, target_eid)
                        self.io.mem_store.append_ephemeral(mem_key, 'assistant', cancel_txt)
                    self.io.log_line(world, target_eid, 'NPC', cancel_txt, role)
                except Exception:
                    pass
                try:
                    push_bubble(world, target_eid, cancel_txt, color=(255, 200, 200), ttl_ms=2000)
                except Exception:
                    pass
                return
            lang = self.io.lang_for(world, target_eid, state)
            ask = self.io.tr(lang, 'Por favor responde "sí" para confirmar o "no" para cancelar.', 'Please answer "yes" to confirm or "no" to cancel.')
            state.chat_add_message('NPC', ask)
            try:
                if self.io.mem_store is not None:
                    mem_key = self.io.memory_key(world, target_eid)
                    self.io.mem_store.append_ephemeral(mem_key, 'assistant', ask)
                self.io.log_line(world, target_eid, 'NPC', ask, role)
            except Exception:
                pass
            try:
                push_bubble(world, target_eid, ask, color=(255, 235, 180), ttl_ms=2400)
            except Exception:
                pass
            return

        # Direct vendor commands
        if self.vendor.is_trader(world, target_eid):
            intent = parse_vendor_intent(text)
            if intent:
                typ, args = intent
                if typ in {'stock', 'stock_q'}:
                    item = (args[0] if args else 'wood')
                    txt = self.vendor.vendor_stock(world, target_eid, item)
                    state.chat_add_message('NPC', txt)
                    try:
                        if self.io.mem_store is not None:
                            mem_key = self.io.memory_key(world, target_eid)
                            self.io.mem_store.append_ephemeral(mem_key, 'assistant', txt)
                        self.io.log_line(world, target_eid, 'NPC', txt, role)
                    except Exception:
                        pass
                    try:
                        push_bubble(world, target_eid, txt, color=(255, 235, 180), ttl_ms=2600)
                    except Exception:
                        pass
                    return
                if typ == 'stock_list':
                    txt = self.vendor.vendor_list_stock(world, target_eid)
                    state.chat_add_message('NPC', txt)
                    try:
                        if self.io.mem_store is not None:
                            mem_key = self.io.memory_key(world, target_eid)
                            self.io.mem_store.append_ephemeral(mem_key, 'assistant', txt)
                        self.io.log_line(world, target_eid, 'NPC', txt, role)
                    except Exception:
                        pass
                    try:
                        push_bubble(world, target_eid, txt, color=(255, 235, 180), ttl_ms=3000)
                    except Exception:
                        pass
                    return
                if typ == 'gold':
                    txt = self.vendor.vendor_gold(world, target_eid)
                    state.chat_add_message('NPC', txt)
                    try:
                        if self.io.mem_store is not None:
                            mem_key = self.io.memory_key(world, target_eid)
                            self.io.mem_store.append_ephemeral(mem_key, 'assistant', txt)
                        self.io.log_line(world, target_eid, 'NPC', txt, role)
                    except Exception:
                        pass
                    try:
                        push_bubble(world, target_eid, txt, color=(255, 235, 180), ttl_ms=2600)
                    except Exception:
                        pass
                    return
                if typ == 'restock':
                    qty, item = args
                    out = self.vendor.vendor_restock(world, target_eid, item, qty)
                    state.chat_add_message('NPC', out)
                    try:
                        if self.io.mem_store is not None:
                            mem_key = self.io.memory_key(world, target_eid)
                            self.io.mem_store.append_ephemeral(mem_key, 'assistant', out)
                        self.io.log_line(world, target_eid, 'NPC', out, role)
                    except Exception:
                        pass
                    try:
                        push_bubble(world, target_eid, out, color=(200, 240, 200), ttl_ms=2400)
                    except Exception:
                        pass
                    return
                if typ == 'add_wood':
                    qty = int(args[0])
                    out = self.vendor.vendor_restock(world, target_eid, 'wood', qty)
                    state.chat_add_message('NPC', out)
                    try:
                        if self.io.mem_store is not None:
                            mem_key = self.io.memory_key(world, target_eid)
                            self.io.mem_store.append_ephemeral(mem_key, 'assistant', out)
                        self.io.log_line(world, target_eid, 'NPC', out, role)
                    except Exception:
                        pass
                    try:
                        push_bubble(world, target_eid, out, color=(200, 240, 200), ttl_ms=2400)
                    except Exception:
                        pass
                    return
                if typ == 'buy':
                    qty, item = args
                    if item in {'wooden', 'madera'}:
                        item = 'wood'
                    self.vendor.ask_confirm(world, state, target_eid, op='buy', item=item, qty=int(qty), pending_confirms=self.pending_confirms)
                    return
                if typ == 'sell':
                    qty, item = args
                    if item in {'wooden', 'madera'}:
                        item = 'wood'
                    self.vendor.ask_confirm(world, state, target_eid, op='sell', item=item, qty=int(qty), pending_confirms=self.pending_confirms)
                    return

        # Build LLM history
        history = []
        try:
            for sender, line in getattr(state, 'chat_messages', [])[-10:]:
                r = 'user' if str(sender).lower() in {'tú', 'tu', 'you'} else 'assistant'
                history.append({"role": r, "content": str(line)})
        except Exception:
            pass
        # Persist current UI language preference
        try:
            ui_lang = (getattr(state, 'chat_lang_preference', None) or '').strip().lower()
            if ui_lang in {'es', 'en'} and self.io.mem_store is not None:
                self.io.mem_store.set_language(self.io.memory_key(world, target_eid), ui_lang)
        except Exception:
            pass
        # Estimate online status for UI
        try:
            est = self.io.estimate_online_status()
            state.chat_llm_online_estimated = bool(est)
        except Exception:
            try:
                state.chat_llm_online_estimated = False
            except Exception:
                pass
        # Submit job to async worker
        player_id = getattr(world, 'player_entity', None) or -1
        job = ChatJob(
            player_id=player_id,
            npc_id=self.io.memory_key(world, target_eid),
            user_text=text,
            role=str(role),
            persona_id=str(persona_id or ''),
            history=history,
        )
        job_id = self.worker.submit(job)
        self.latest_job_for_target[target_eid] = job_id
        self.job_meta[job_id] = {'target': target_eid, 'role': role, 'persona_id': persona_id}
        # UI typing indicator
        try:
            state.chat_typing = True
            push_bubble(world, target_eid, '…', color=(220, 220, 220), ttl_ms=1000)
        except Exception:
            pass

    # ------------------------------------------------------------------
    def _drain_completed_jobs(self, world: Any, state: Any) -> None:
        completed = self.worker.poll_completed(max_items=8)
        for job_id, result in completed:
            meta = self.job_meta.pop(job_id, None)
            if not meta:
                continue
            target_eid = meta.get('target')
            role = meta.get('role')
            # Skip stale replies
            if self.latest_job_for_target.get(target_eid) != job_id:
                continue
            try:
                state.chat_typing = False
            except Exception:
                pass

            responded = False
            # Tool calls
            if getattr(result, 'tool_calls', None):
                for call in result.tool_calls:
                    name = getattr(call, 'name', '')
                    args = getattr(call, 'arguments', {}) or {}
                    if name == 'vendor.buy' and self.vendor.is_trader(world, target_eid):
                        qty = int(args.get('quantity', 1))
                        item = str(args.get('item', 'wood')).lower()
                        self.vendor.ask_confirm(world, state, target_eid, op='buy', item=item, qty=qty, pending_confirms=self.pending_confirms)
                        responded = True
                    elif name == 'vendor.sell' and self.vendor.is_trader(world, target_eid):
                        qty = int(args.get('quantity', 1))
                        item = str(args.get('item', 'wood')).lower()
                        self.vendor.ask_confirm(world, state, target_eid, op='sell', item=item, qty=qty, pending_confirms=self.pending_confirms)
                        responded = True
                    elif name == 'vendor.stock' and self.vendor.is_trader(world, target_eid):
                        txt = self.vendor.vendor_stock(world, target_eid, 'wood')
                        state.chat_add_message('NPC', txt)
                        try:
                            if self.io.mem_store is not None:
                                mem_key = self.io.memory_key(world, target_eid)
                                self.io.mem_store.append_ephemeral(mem_key, 'assistant', txt)
                            self.io.log_line(world, target_eid, 'NPC', txt, role)
                        except Exception:
                            pass
                        try:
                            push_bubble(world, target_eid, txt, color=(255, 235, 180), ttl_ms=2600)
                        except Exception:
                            pass
                        responded = True
            # Fallback to text reply
            if not responded:
                reply = (getattr(result, 'text', None) or '').strip()
                if not reply:
                    lang = self.io.lang_for(world, target_eid, state)
                    reply = self.io.tr(lang, 'No entiendo. Usa "buy N wood" o "sell N wood".', "I don't understand. Use \"buy N wood\" or \"sell N wood\".")
                try:
                    if self.io.mem_store is not None:
                        mem_key = self.io.memory_key(world, target_eid)
                        self.io.mem_store.append_ephemeral(mem_key, 'assistant', reply)
                    self.io.log_line(world, target_eid, 'NPC', reply, role)
                except Exception:
                    pass
                last_due, placeholder_idx = self.scheduler.schedule_reply_chunks(
                    world,
                    state,
                    target_eid,
                    reply,
                    color=(255, 235, 180),
                    words_per_chunk=8,
                    delay_ms=3000,
                    ttl_ms=2600,
                )
            # Online/offline flags and suffix
            try:
                if getattr(result, 'offline', False):
                    try:
                        state.chat_llm_online = False
                    except Exception:
                        pass
                    if 'last_due' in locals() and last_due is not None and 'placeholder_idx' in locals() and placeholder_idx is not None:
                        lang = self.io.lang_for(world, target_eid, state)
                        suffix = self.io.tr(lang, ' (modo offline)', ' (offline mode)')
                        self.scheduler.scheduled.append({'due': int(last_due), 'type': 'chat_append_suffix', 'data': {'idx': int(placeholder_idx), 'suffix': suffix, 'target': int(target_eid)}})
                else:
                    try:
                        state.chat_llm_online = True
                    except Exception:
                        pass
            except Exception:
                pass
