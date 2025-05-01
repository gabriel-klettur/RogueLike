# Path: src/roguelike_engine/utils/debug.py
import pygame

def draw_player_aim_line(screen, camera, player):    

    # Coordenadas base del jugador (centro inferior del sprite)
    player_base = (
        player.x + player.sprite_size[0] // 2,
        player.y + player.sprite_size[1]
    )

    # Coordenadas del mouse en mundo
    mouse_x, mouse_y = pygame.mouse.get_pos()
    world_mouse_x = mouse_x / camera.zoom + camera.offset_x
    world_mouse_y = mouse_y / camera.zoom + camera.offset_y

    # Convertir ambas posiciones a pantalla
    start_pos = camera.apply(player_base)
    end_pos = camera.apply((world_mouse_x, world_mouse_y))

    # Dibujar línea de dirección
    pygame.draw.line(screen, (255, 255, 0), start_pos, end_pos, 3)