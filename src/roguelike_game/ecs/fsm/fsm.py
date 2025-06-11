from .state import State
import time

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

    def change_state(self, new_state: State, entity):
        """
        Cambia al nuevo estado, llamando exit en el actual y enter en el nuevo.
        """
        old_state_name = self.current_state.__class__.__name__
        new_state_name = new_state.__class__.__name__
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