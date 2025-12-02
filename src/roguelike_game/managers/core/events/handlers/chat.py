import pygame
from pathlib import Path
from roguelike_game.ecs.systems.chat.chat_input_controller import ChatInputController
from roguelike_game.ecs.systems.chat.chat_ui_system import handle_chat_ui_events
from roguelike_game.ecs.systems.chat.chat_bubble_utils import push_bubble
from roguelike_game.ecs.systems.vendors.vendor_ui_system import handle_vendor_ui_events
from roguelike_engine.chat.service.memory_store import MemoryStore
import re


def handle_chat_open(game, events) -> bool:
    try:
        world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
        state = getattr(world, 'state', None)
        if state is not None and bool(getattr(state, 'chat_open', False)):
            ctrl = getattr(world, '_chat_input_ctrl', None)
            if ctrl is None:
                ctrl = ChatInputController()
                setattr(world, '_chat_input_ctrl', ctrl)
            ctrl.ensure_open(world)
            try:
                ctrl.handle_events(world, events)
            except Exception:
                pass
            try:
                handle_chat_ui_events(world, events)
            except Exception:
                pass
            # Panel de comercio: manejar clicks/scroll cuando el chat está abierto
            try:
                handle_vendor_ui_events(world, events)
            except Exception:
                pass
            return True
    except Exception:
        pass
    return False


def handle_interact_open(game, events) -> bool:
    for event in events:
        if event.type == pygame.KEYDOWN and event.key == game.input_config.get_key('interact'):
            try:
                world = getattr(getattr(game, 'ecs', None), 'ecs_world', None)
                state = getattr(world, 'state', None)
                if world and state and not bool(getattr(state, 'chat_open', False)):
                    comps = getattr(world, 'components', {})
                    pos_map = comps.get('Position', {}) or {}
                    chat_map = comps.get('ChatComponent', {}) or {}
                    player_eid = getattr(world, 'player_entity', None)
                    player_pos = pos_map.get(player_eid)
                    target_eid = None
                    if player_pos and chat_map:
                        try:
                            px = float(getattr(player_pos, 'x', 0.0))
                            py = float(getattr(player_pos, 'y', 0.0))
                        except Exception:
                            px = py = 0.0
                        best_d2 = None
                        for eid, chat in list(chat_map.items()):
                            npc_pos = pos_map.get(eid)
                            if not npc_pos:
                                continue
                            try:
                                dx = float(getattr(npc_pos, 'x', 0.0)) - px
                                dy = float(getattr(npc_pos, 'y', 0.0)) - py
                                d2 = dx*dx + dy*dy
                                rng = float(getattr(chat, 'chat_range', 0.0) or 0.0)
                                if d2 <= (rng * rng):
                                    if best_d2 is None or d2 < best_d2:
                                        best_d2 = d2
                                        target_eid = eid
                            except Exception:
                                continue
                    state.chat_open = True
                    state.chat_input_buffer = ""
                    state.chat_bind_target(target_eid)
                    if target_eid is not None:
                        # Incrementar contador de visitas para este NPC en la memoria de chat
                        try:
                            def _find_repo_root() -> Path:
                                here = Path(__file__).resolve()
                                candidates = list(here.parents)
                                try:
                                    cwd = Path.cwd().resolve()
                                    candidates.append(cwd)
                                    candidates.extend(list(cwd.parents))
                                except Exception:
                                    pass
                                for p in candidates:
                                    if (p / 'data' / 'config' / 'chat.json').exists():
                                        return p
                                return here.parents[4] if len(here.parents) > 4 else Path('.')

                            def _mem_key(world, eid: int) -> str:
                                try:
                                    ident = world.components.get('Identity', {}).get(eid)
                                    if ident is not None:
                                        name = str(getattr(ident, 'name', '') or '').strip().lower()
                                        stable_id = getattr(ident, 'id', None)
                                        if stable_id is not None:
                                            slug = re.sub(r"[^a-z0-9]+", "-", name)
                                            slug = re.sub(r"-+", "-", slug).strip('-') or 'npc'
                                            return f"{slug}-{int(stable_id)}"
                                except Exception:
                                    pass
                                return str(eid)

                            root = _find_repo_root()
                            ms = MemoryStore(root)
                            ms.increment_visit(_mem_key(world, target_eid))
                        except Exception:
                            pass

                        greeting = getattr(chat_map.get(target_eid, None), 'greeting', None)
                        if greeting:
                            state.chat_add_message('NPC', str(greeting))
                            try:
                                push_bubble(world, target_eid, str(greeting), color=(255, 235, 180), ttl_ms=2600)
                            except Exception:
                                pass
                    return True
            except Exception:
                pass
    return False


def handle_class_selector(game, events) -> bool:
    if hasattr(game, 'class_selector') and getattr(game.class_selector, 'show', False):
        try:
            game.state.class_selector_open = True
        except Exception:
            pass
        for event in events:
            result = game.class_selector.handle_input(event)
            if result:
                try:
                    if hasattr(game, 'menu') and game.menu and hasattr(game.menu, 'finalize_new_game_with_class'):
                        game.menu.finalize_new_game_with_class(result)
                except Exception:
                    pass
                try:
                    if hasattr(game, 'menu') and game.menu:
                        game.menu.stop_music(fade_ms=500)
                except Exception:
                    pass
        try:
            game.state.class_selector_open = bool(getattr(game.class_selector, 'show', False))
        except Exception:
            pass
        if not getattr(game.class_selector, 'show', False):
            try:
                if hasattr(game, 'menu') and game.menu:
                    game.menu.stop_music(fade_ms=500)
            except Exception:
                pass
        return True
    return False
