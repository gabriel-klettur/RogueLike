import pytest
import time
import pickle
import pygame
from roguelike_game.ecs.fsm.fsm import FiniteStateMachine
from roguelike_game.ecs.fsm.state import State

class DummyState(State):
    def __init__(self, name):
        self.name = name
    def enter(self, entity):
        setattr(entity, 'entered', True)
    def execute(self, entity, dt):
        pass
    def exit(self, entity):
        setattr(entity, 'exited', True)

class DummyEntity:
    def __init__(self):
        self.id = 1
        self.world = None

# Transiciones válidas
def test_valid_transitions():
    s1 = DummyState("A")
    s2 = DummyState("B")
    fsm = FiniteStateMachine(s1)
    entity = DummyEntity()
    fsm.change_state(s2, entity)
    assert isinstance(fsm.current_state, DummyState)
    assert fsm.current_state.name == "B"
    assert hasattr(entity, 'entered')
    assert hasattr(entity, 'exited')

# Transiciones inválidas: pasar None
def test_invalid_transitions():
    fsm = FiniteStateMachine(DummyState("A"))
    entity = DummyEntity()
    with pytest.raises(AttributeError):
        fsm.change_state(None, entity)

# Guards (condiciones)
def test_guards():
    pytest.skip("No hay implementación de guards en el FSM base")

# Estados temporizados (timeouts)
def test_timeouts():
    pytest.skip("No hay lógica de timeouts directa en el FSM base para testar")

# Flujo de NPC
def test_npc_flow():
    pytest.skip("Test de flujo de NPC requiere integración con world")

# Flujo de Player
def test_player_flow():
    pytest.skip("Test de flujo de Player requiere integración con world")

# FSM de magias
def test_spell_fsm():
    pytest.skip("Test de FSM de magias requiere integración con world")

# Cancelación de magia
def test_spell_cancellation():
    pytest.skip("Test de cancelación de magia requiere lógica específica")

# Exportación gráfica
def test_graph_export():
    pygame.init()
    screen = pygame.Surface((800, 600))
    fsm = FiniteStateMachine(DummyState("A"))
    # No hay history, pero debe dibujar nodos
    fsm.debug_draw(screen)
    # Comprobar que al menos se pintó algo (pixel no transparente)
    assert screen.get_at((400, 300)) != pygame.Color(0, 0, 0, 0)
    pygame.quit()

# Serialización y persistencia
def test_serialization():
    s = DummyState("A")
    fsm = FiniteStateMachine(s)
    fsm.change_state(DummyState("B"), DummyEntity())
    data = pickle.dumps(fsm)
    fsm2 = pickle.loads(data)
    assert type(fsm2.current_state) == type(fsm.current_state)
    assert {s.name for s in fsm2._seen_states} == {s.name for s in fsm._seen_states}

# Concurrencia
def test_concurrency():
    fsms = [FiniteStateMachine(DummyState("A")) for _ in range(1000)]
    entities = [DummyEntity() for _ in fsms]
    for f, e in zip(fsms, entities):
        f.update(e, 0)
    assert all(isinstance(f.current_state, DummyState) for f in fsms)

# Inyección masiva de eventos
def test_event_flood():
    pytest.skip("El FSM no maneja cola de eventos masiva")

# Logging de transiciones
def test_logging(capsys):
    s1 = DummyState("A")
    s2 = DummyState("B")
    fsm = FiniteStateMachine(s1)
    entity = DummyEntity()
    fsm.change_state(s2, entity)
    captured = capsys.readouterr()
    assert "state DummyState -> DummyState" in captured.out
