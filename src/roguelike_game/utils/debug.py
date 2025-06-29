# Path: src/roguelike_game/utils/debug.py
import pygame
from collections import defaultdict

def init_debug_log() -> dict:
    """
    Activa el cursor y retorna un dict para acumular logs de rendimiento.
    """
    pygame.mouse.set_visible(True)
    return defaultdict(list)