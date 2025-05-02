# Path: src/roguelike_game/entities/player/controller/input_handler.py
import pygame

def handle_player_input(event, player_ctrl):
    """
    Traduce eventos de pygame en llamadas al modelo:
     - Movimiento (teclas WASD/←↑→↓)
     - Habilidades (teclas Q, E, etc.)
    """
    if event.type != pygame.KEYDOWN:
        return

    key = event.key
    m = player_ctrl.movement
    p = player_ctrl.model

    # Movimiento básico
    if key in (pygame.K_w, pygame.K_UP):
        m.move(0, -1, p.get_collision_mask(), player_ctrl.obstacles)
    elif key in (pygame.K_s, pygame.K_DOWN):
        m.move(0, 1, p.get_collision_mask(), player_ctrl.obstacles)
    elif key in (pygame.K_a, pygame.K_LEFT):
        m.move(-1, 0, p.get_collision_mask(), player_ctrl.obstacles)
    elif key in (pygame.K_d, pygame.K_RIGHT):
        m.move(1, 0, p.get_collision_mask(), player_ctrl.obstacles)

    # Habilidades / ataques
    elif key == pygame.K_E:
        player_ctrl.attack.perform_basic_attack()
    elif key == pygame.K_Q:
        player_ctrl.stats.restore_all()
    # … añade las demás (dash, shield, firework…) aquí