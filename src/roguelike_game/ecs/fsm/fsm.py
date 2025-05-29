from .state import State

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
        self.current_state.exit(entity)
        self.current_state = new_state
        new_state.fsm = self
        self.current_state.enter(entity)

    def update(self, entity, dt):
        """
        Ejecuta la lógica del estado activo.
        """
        self.current_state.execute(entity, dt)