import math
import pygame
from roguelike_ui.ui_blocker import is_blocked

import logging
logger = logging.getLogger(__name__)

class ChatProximitySystem:
    """
    Detecta clic izquierdo dentro del rango de chat para abrir la UI de chat
    con la entidad objetivo que posea ChatComponent.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self._prev_left = False

    def _get_mouse_click_edge(self):
        pressed = pygame.mouse.get_pressed(5)
        left = bool(pressed[0])
        edge = left and not self._prev_left
        self._prev_left = left
        return edge

    def update(self, world, *args):
        state = getattr(world, 'state', None)
        if state is None:
            return
        # Si ya está abierto, no intentar abrir otro
        if getattr(state, 'chat_open', False):
            return
        # Nuevo comportamiento: la apertura de chat por clic se gestiona en
        # managers/core/events.handle_events con detección precisa de halo.
        # Este sistema solo abrirá el chat si explícitamente se habilita una
        # bandera de override para depuración: state.chat_open_on_any_click.
        if not bool(getattr(state, 'chat_open_on_any_click', False)):
            return
        # Detectar flanco de clic izquierdo fuera de UI
        if not self._get_mouse_click_edge():
            return
        mx, my = pygame.mouse.get_pos()
        if is_blocked(mx, my):
            return
        # Necesitamos player y posiciones
        player_eid = getattr(world, 'player_entity', None)
        if player_eid is None:
            return
        pos_components = world.components.get('Position', {})
        player_pos = pos_components.get(player_eid)
        if player_pos is None:
            return
        # Buscar entidades con ChatComponent en rango
        chats = world.components.get('ChatComponent', {})
        if not chats:
            return
        opened = False
        for eid, chat in chats.items():
            npc_pos = pos_components.get(eid)
            if not npc_pos:
                continue
            dx = float(getattr(npc_pos, 'x', 0.0)) - float(getattr(player_pos, 'x', 0.0))
            dy = float(getattr(npc_pos, 'y', 0.0)) - float(getattr(player_pos, 'y', 0.0))
            dist = math.hypot(dx, dy)
            rng = float(getattr(chat, 'chat_range', 0.0) or 0.0)
            if dist <= rng:
                # Abrir chat con esta entidad (enlazar historial al target)
                state.chat_open = True
                state.chat_bind_target(eid)
                state.chat_input_buffer = ""
                try:
                    greeting = getattr(chat, 'greeting', None)
                    if greeting:
                        world.state.chat_add_message('NPC', str(greeting))
                except Exception:
                    pass
                logger.debug(f"[Chat] Abierto chat con eid={eid} a distancia={dist:.2f} (rango={rng})")
                opened = True
                break
        if not opened:
            logger.debug("[Chat] Clic detectado, pero ningún NPC con chat en rango")
