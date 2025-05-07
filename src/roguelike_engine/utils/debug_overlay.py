# Path: src/roguelike_engine/utils/debug_overlay.py
import pygame

def render_debug_overlay(screen, perf_log, extra_lines=None, position=(8, 8), font_size=18):
    font = pygame.font.SysFont("Consolas", font_size)
    lines = []

    # --- Calcular anchos para alinear texto ---
    label_width = 0
    value_width = 0
    formatted_data = []

    for key in perf_log.keys():
        samples = perf_log[key][-60:]
        if samples:
            avg_time_ms = sum(samples) / len(samples) * 1000
            label = f"{key:<18}"  # Campo fijo de 18 caracteres
            value = f"{avg_time_ms:>6.2f} ms"  # Tiempo por frame en milisegundos
            formatted_data.append((label, value))
            label_width = max(label_width, font.size(label)[0])
            value_width = max(value_width, font.size(value)[0])

    # ➕ Mostrar FPS reales y aclaración del frame time
    if extra_lines and isinstance(extra_lines, list) and hasattr(extra_lines[0], "clock"):
        state = extra_lines.pop(0)  # Extraemos el estado
        real_fps = state.clock.get_fps()
        lines.append(("____FPS (real):", f"{real_fps:>6.2f} FPS"))
        lines.append(("____Frame Time avg", f"{(1000/real_fps):>6.2f} ms" if real_fps > 0 else "--"))

    # --- Añadir líneas formateadas ---
    for label, value in formatted_data:
        lines.append((label, value))

    # --- Añadir líneas adicionales personalizadas ---
    if extra_lines:
        lines.append(("", ""))  # Espaciado
        for custom in extra_lines:
            lines.append((custom, ""))  # Texto plano sin valor a la derecha

    # --- Renderizado ---
    y = position[1]
    padding_x = 10
    padding_y = 4
    spacing = 4

    for left, right in lines:
        left_surf = font.render(str(left), True, (255, 255, 255))
        right_surf = pygame.font.Font(None, font_size).render(str(right), True, (200, 255, 200)) if right else None

        height = max(left_surf.get_height(), right_surf.get_height() if right_surf else 0)
        width = label_width + value_width + padding_x * 2

        bg_rect = pygame.Rect(position[0], y, width, height + padding_y * 2)
        bg_surface = pygame.Surface((bg_rect.width, bg_rect.height), pygame.SRCALPHA)
        bg_surface.fill((0, 0, 0, 180))  # Fondo semi-transparente

        screen.blit(bg_surface, bg_rect)
        screen.blit(left_surf, (bg_rect.x + padding_x, bg_rect.y + padding_y))

        if right_surf:
            screen.blit(right_surf, (bg_rect.x + label_width + padding_x + 8, bg_rect.y + padding_y))

        y += height + padding_y + spacing