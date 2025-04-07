# roguelike_project/utils/debug_overlay.py

import pygame
from roguelike_project.config import FPS

_font_cache = None

def get_font():
    global _font_cache
    if _font_cache is None:
        _font_cache = pygame.font.SysFont("consolas", 18)
    return _font_cache

def render_debug_overlay(screen, perf_log, sample_size=60, position=(8, 130)):
    # Usar solo los últimos N datos
    def avg(key):
        data = perf_log.get(key, [])
        if not data:
            return 0.0
        return sum(data[-sample_size:]) / min(len(data), sample_size)

    avg_frame = avg("frame_times")
    avg_fps = 1 / avg_frame if avg_frame > 0 else 0

    debug_lines = [
        f"FPS: {avg_fps:.1f} (Objetivo: {FPS})",
        f"Eventos: {avg('handle_events'):.4f}s",
        f"Update : {avg('update'):.4f}s",
        f"Render : {avg('render'):.4f}s"
    ]

    font = get_font()
    width = 280
    height = 20 * len(debug_lines) + 10
    x, y = position

    # Fondo semitransparente
    overlay_surface = pygame.Surface((width, height), pygame.SRCALPHA)
    overlay_surface.fill((0, 0, 0, 180))
    screen.blit(overlay_surface, (x, y))

    for i, line in enumerate(debug_lines):
        text_surface = font.render(line, True, (255, 255, 0))
        screen.blit(text_surface, (x + 10, y + 5 + i * 20))

    # Limitar tamaño del buffer para que no crezca infinitamente
    for key in perf_log:
        perf_log[key] = perf_log[key][-sample_size:]
