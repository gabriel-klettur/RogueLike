import pygame
import pytest
from roguelike_game.ecs.fsm.fsm import FiniteStateMachine
from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.core.manager import World

# Ejemplo de un estado de prueba
def test_state_enter(entity):
    pass

class DummyState(State):
    def enter(self, entity):
        entity.world = entity.world  # no-op
    def execute(self, entity, dt):
        pass
    def exit(self, entity):
        pass

@pytest.mark.visual
def test_fsm_visual():
    pygame.init()
    screen = pygame.display.set_mode((800, 600))
    # Configuro estado inicial y FSM
    initial = DummyState()
    fsm = FiniteStateMachine(initial)
    # Simulo transiciones
    states = [DummyState() for _ in range(3)]
    actions = [(initial, states[0]), (states[0], states[1]), (states[1], states[2]), (states[2], initial)]
    
    clock = pygame.time.Clock()
    running = True
    idx = 0
    while running and idx < len(actions):
        for event in pygame.event.get():
            if event.type == pygame.QUIT:
                running = False
        old, new = actions[idx]
        fsm.change_state(new, entity=type('E', (), {'id': 1}))
        fsm.update(entity=type('E', (), {'id': 1}), dt=0)
        screen.fill((0, 0, 0))
        fsm.debug_draw(screen)
        pygame.display.flip()
        clock.tick(1)
        idx += 1
    pygame.quit()
