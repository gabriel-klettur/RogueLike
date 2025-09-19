import math
import pygame
from roguelike_game.ecs.components.transform.scale import Scale

class PlayerAimIndicatorRenderSystem:
    """
    Dibuja un indicador visual (flecha) que marca la dirección de mirada del jugador.
    - Usa el vector continuo de aim del stick derecho si supera el deadzone.
    - Si no hay aim del stick, cae al vector hacia el ratón (como fallback).
    - Estilo profesional: flecha con punta, color amarillo translúcido.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self.deadzone = 0.25
        self.color = (255, 220, 0)
        self.alpha = 185  # leve transparencia para integrarse mejor
        self.length_px = 28  # longitud base (se escala con zoom)
        self.head_len_px = 9
        self.head_width_px = 8
        # Persistencia de fuente dominante y último vector de stick por entidad
        self._last_source: dict[int, str] = {}  # 'stick' | 'mouse'
        self._last_stick_vec: dict[int, tuple[float, float]] = {}
        self._prev_mouse_pos: tuple[int, int] | None = None

    def _normalize(self, x: float, y: float) -> tuple[float, float, float]:
        m = math.hypot(x, y)
        if m <= 1e-6:
            return 0.0, 0.0, 0.0
        return x / m, y / m, m

    def update(self, world, screen, camera):
        player_eid = getattr(world, 'player_entity', None)
        if player_eid is None:
            return

        comps = world.components
        pos = comps.get('Position', {}).get(player_eid)
        sprite = comps.get('Sprite', {}).get(player_eid)
        inp = comps.get('InputComponent', {}).get(player_eid)
        if not pos or not sprite:
            return

        # Centro del sprite en pantalla (considerando zoom y Scale)
        scale = comps.get('Scale', {}).get(player_eid, Scale()).scale
        sx, sy = camera.apply((pos.x, pos.y))
        w, h = sprite.image.get_size()
        cx = sx + (w * scale * camera.zoom) / 2
        cy = sy + (h * scale * camera.zoom) / 2

        # Origen dominante del aim: stick vs ratón (último en moverse domina)
        mx, my = pygame.mouse.get_pos()
        mouse_moved = (self._prev_mouse_pos != (mx, my))
        self._prev_mouse_pos = (mx, my)

        ax = float(getattr(inp, 'aim_x', 0.0) or 0.0)
        ay = float(getattr(inp, 'aim_y', 0.0) or 0.0)
        stick_active = (ax * ax + ay * ay) >= (self.deadzone * self.deadzone)
        if stick_active:
            nx, ny, m = self._normalize(ax, ay)
            if m > 0:
                self._last_stick_vec[player_eid] = (nx, ny)
            self._last_source[player_eid] = 'stick'
        if mouse_moved:
            self._last_source[player_eid] = 'mouse'

        src = self._last_source.get(player_eid)
        if src == 'stick':
            nx, ny = self._last_stick_vec.get(player_eid, (0.0, 0.0))
            vx, vy = nx, ny
        else:
            # Ratón como fuente por defecto o dominante
            vx = (mx - cx)
            vy = (my - cy)

        nx, ny, mag = self._normalize(vx, vy)
        if mag <= 0.0:
            return

        # Longitud y geometría de la flecha según zoom de cámara
        length = max(18.0, self.length_px * camera.zoom)
        head_len = max(6.0, self.head_len_px * camera.zoom)
        head_w = max(5.0, self.head_width_px * camera.zoom)

        end_x = cx + nx * length
        end_y = cy + ny * length

        # Dibujar línea principal con Surface alpha
        surf = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        pygame.draw.line(surf, (*self.color, self.alpha), (cx, cy), (end_x, end_y), width=2)
        # Cabeza de flecha (triángulo)
        angle = math.atan2(ny, nx)
        left_angle = angle + math.radians(150)
        right_angle = angle - math.radians(150)
        tip = (end_x, end_y)
        left = (end_x - math.cos(left_angle) * head_len, end_y - math.sin(left_angle) * head_len)
        right = (end_x - math.cos(right_angle) * head_len, end_y - math.sin(right_angle) * head_len)
        pygame.draw.polygon(surf, (*self.color, self.alpha), [tip, left, right])
        # Blit overlay una vez
        screen.blit(surf, (0, 0))
