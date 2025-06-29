# Path: tests/visual/test_fsm_visual.py
import pygame
import pytest
from roguelike_game.ecs.fsm.fsm import FiniteStateMachine  # Máquina de estados finitos personalizada del juego
from roguelike_game.ecs.fsm.state import State              # Clase base para los estados

# Definición de un estado "dummy" para pruebas.
# Hereda de la clase base `State` pero no implementa lógica.
# Se usa para simular transiciones sin lógica interna.
class DummyState(State):
    def enter(self, entity):
        pass

    def execute(self, entity, dt):
        pass

    def exit(self, entity):
        pass


# Test marcado como visual: se espera validación manual a través de la ventana de Pygame.
@pytest.mark.visual
def test_fsm_visual():
    # Inicialización de Pygame y creación de ventana
    pygame.init()
    screen = pygame.display.set_mode((800, 600))

    # Instanciación del estado inicial y de la FSM
    initial = DummyState()
    fsm = FiniteStateMachine(initial)

    # Generación de una secuencia de estados ficticios para simular transiciones
    states = [DummyState() for _ in range(3)]
    actions = [
        (initial, states[0]),
        (states[0], states[1]),
        (states[1], states[2]),
        (states[2], initial)
    ]

    # Reloj para controlar la tasa de refresco y variables de control
    clock = pygame.time.Clock()
    running = True
    idx = 0

    # Bucle principal: procesa eventos y ejecuta transiciones visuales
    while running and idx < len(actions):
        # Manejo de eventos para permitir cierre de ventana
        for event in pygame.event.get():
            if event.type == pygame.QUIT:
                running = False

        # Cambio de estado en la FSM según la lista de acciones
        old, new = actions[idx]
        entity = type('E', (), {'id': 1})  # Mock de entidad con un ID único
        fsm.change_state(new, entity=entity)
        fsm.update(entity=entity, dt=0)   # Actualización sin avance de tiempo

        # Dibujo en pantalla
        screen.fill((0, 0, 0))             # Limpieza de pantalla con color negro
        fsm.debug_draw(screen)            # Dibujo visual del estado actual (requiere implementación)
        pygame.display.flip()             # Actualización del frame mostrado

        clock.tick(1)                     # Espera ~1 segundo entre transiciones
        idx += 1                          # Avanza a la siguiente transición

    # Cierre adecuado de Pygame tras finalizar las transiciones
    pygame.quit()