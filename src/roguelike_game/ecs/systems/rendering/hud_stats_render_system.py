import pygame
import time
from typing import Optional

from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.combat.mana import Mana


class HUDStatsRenderSystem:
    """
    Renderiza en la esquina inferior izquierda valores de Vida (HP) y Maná (MP) del jugador, en formato actual/max.

    Diseño profesional y robusto:
    - Cachea la fuente y permite un modo de alto contraste mediante sombra.
    - Tolera ausencia de jugador o componentes sin lanzar excepciones.
    - Escalable: fácil de extender para más recursos (energía, stamina, etc.).
    """

    def __init__(self, perf_log=None, font_name: Optional[str] = None, font_size: int = 18):
        self.perf_log = perf_log
        pygame.font.init()
        # Intentar fuente monoespaciada para mejor alineación; fallback a default
        try:
            self.font = pygame.font.SysFont(font_name or "consolas", font_size)
        except Exception:
            self.font = pygame.font.SysFont(None, font_size)
        # Estilo
        self.text_color = (255, 255, 255)
        self.shadow_color = (0, 0, 0)
        self.shadow_offset = (1, 1)
        self.margin = 12
        self.line_spacing = 4

    def _find_player_eid(self, world) -> Optional[int]:
        # Preferir referencia directa si existe
        eid = getattr(world, 'player_entity', None)
        if isinstance(eid, int):
            return eid
        # Fallback: primer PlayerTagComponent
        try:
            players = world.components.get('PlayerTagComponent', {})
            if players:
                return next(iter(players.keys()))
        except Exception:
            pass
        return None

    def _fmt_pair(self, label: str, current: Optional[int | float], maximum: Optional[int | float]) -> str:
        try:
            c = int(current) if current is not None else 0
        except Exception:
            c = 0
        try:
            m = int(maximum) if maximum is not None else 0
        except Exception:
            m = 0
        return f"{label}: {c}/{m}"

    def _blit_text(self, screen: pygame.Surface, text: str, x: int, y: int, color: Optional[tuple[int,int,int]] = None) -> None:
        # Sombra
        try:
            shadow = self.font.render(text, True, self.shadow_color)
            screen.blit(shadow, (x + self.shadow_offset[0], y + self.shadow_offset[1]))
        except Exception:
            pass
        # Texto principal
        surf = self.font.render(text, True, color or self.text_color)
        screen.blit(surf, (x, y))

    def update(self, world, screen: pygame.Surface, camera):
        try:
            # No mostrar HUD cuando UI de menú/selector esté activa
            if bool(getattr(world, 'suppress_hud', False)):
                return
            player_eid = self._find_player_eid(world)
            if player_eid is None:
                return
            comps = world.components
            hp: Health = comps.get('Health', {}).get(player_eid)
            mp: Mana = comps.get('Mana', {}).get(player_eid)
            # Preparar strings
            hp_text = self._fmt_pair("HP", getattr(hp, 'current_hp', None), getattr(hp, 'max_hp', None)) if hp else "HP: -/-"
            mp_text = self._fmt_pair("MP", getattr(mp, 'current_mana', None), getattr(mp, 'max_mana', None)) if mp else "MP: -/-"
            # Posicionamiento
            sw, sh = screen.get_size()
            x = self.margin
            # Dibujar desde abajo hacia arriba
            mp_surf_h = self.font.get_height()
            hp_surf_h = self.font.get_height()
            mp_y = sh - self.margin - mp_surf_h
            hp_y = mp_y - self.line_spacing - hp_surf_h
            # Blits
            self._blit_text(screen, hp_text, x, hp_y)
            self._blit_text(screen, mp_text, x, mp_y)

            # Etiqueta temporal 'MP insuficiente' si hay flash de maná activo
            try:
                flash_store = getattr(world, '_mana_flash_until', None)
                active_until = None
                if isinstance(flash_store, dict):
                    active_until = flash_store.get(player_eid)
                if active_until and float(active_until) > time.time():
                    # Posicionar a la derecha del texto MP para ahorrar espacio vertical
                    mp_width, _ = self.font.size(mp_text)
                    warn_x = x + mp_width + 10
                    warn_y = mp_y
                    self._blit_text(screen, 'MP insuficiente', warn_x, warn_y, color=(255, 120, 120))
            except Exception:
                pass
        except Exception:
            # No reventar el loop de render por ningún error de HUD
            pass
