from .state import State
import time
import pygame
import math

class FiniteStateMachine:
    """
    Maneja transiciones de estado para una entidad.
    """

    def __init__(self, initial_state: State):
        """
        Inicializa la FSM con un estado inicial.
        """
        self.current_state = initial_state
        initial_state.fsm = self
        # Debug tracking
        self._seen_states = {initial_state}
        self._history = []
        self.context = {}

    def change_state(self, new_state: State, entity):
        """
        Cambia al nuevo estado, llamando exit en el actual y enter en el nuevo.
        """
        old_state_name = self.current_state.__class__.__name__
        new_state_name = new_state.__class__.__name__
        # Track debug history
        self._seen_states.add(new_state)
        self._history.append((self.current_state, new_state))
        # Debug con tiempos y tipo de hechizo si aplica
        ctx = getattr(self, 'context', {}) or {}
        spell = ctx.get('spell', '')
        now = time.time()
        if old_state_name == 'PrepareSpellState':
            elapsed = now - ctx.get('prepare_start', now)
            print(f"[FSM DEBUG] Eid={entity.id} state {old_state_name} -> {new_state_name} (prepare {elapsed:.2f}s spell={spell})")
        elif old_state_name == 'ChannelSpellState':
            elapsed = now - ctx.get('channel_start', now)
            print(f"[FSM DEBUG] Eid={entity.id} state {old_state_name} -> {new_state_name} (channel {elapsed:.2f}s spell={spell})")
        elif old_state_name in ('CooldownState','PlayerSpellCooldownState'):
            elapsed = now - ctx.get('cooldown_start', now)
            print(f"[FSM DEBUG] Eid={entity.id} state {old_state_name} -> {new_state_name} (cooldown {elapsed:.2f}s spell={spell})")
        else:
            print(f"[FSM DEBUG] Eid={entity.id} state {old_state_name} -> {new_state_name}")
        self.current_state.exit(entity)
        self.current_state = new_state
        new_state.fsm = self
        self.current_state.enter(entity)

    def update(self, entity, dt):
        """
        Ejecuta la lógica del estado activo.
        """
        self.current_state.execute(entity, dt)

    def debug_draw(self, screen):
        '''Dibuja el grafo de estados y transiciones.'''
        states = list(self._seen_states)
        history = self._history
        w, h = screen.get_size()
        cx, cy = w // 2, h // 2
        n = len(states) or 1
        radius = min(w, h) // 3
        angles = [i * 2 * math.pi / n for i in range(n)]
        pos = {s: (cx + int(radius * math.cos(ang)), cy + int(radius * math.sin(ang))) for s, ang in zip(states, angles)}
        # Dibujar transiciones
        for old, new in history:
            pygame.draw.line(screen, (200,200,200), pos[old], pos[new], 2)
        # Dibujar nodos
        font = pygame.font.SysFont(None, 24)
        for s in states:
            x, y = pos[s]
            color = (0,255,0) if s == self.current_state else (100,100,100)
            pygame.draw.circle(screen, color, (x,y), 30)
            text = font.render(s.__class__.__name__, True, (255,255,255))
            rect = text.get_rect(center=(x,y))
            screen.blit(text, rect)