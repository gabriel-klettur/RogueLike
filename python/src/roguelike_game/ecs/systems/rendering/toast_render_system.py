import pygame
import time
from typing import Optional, List, Dict, Any


class ToastRenderSystem:
    """
    Renderiza toasts (mensajes efímeros) en la esquina inferior derecha del HUD.
    Consume world.components['ToastQueue'] con items { 'text': str, 'until': epoch_seconds }.
    """
    def __init__(self, perf_log=None, font_name: Optional[str] = None, font_size: int = 18):
        self.perf_log = perf_log
        pygame.font.init()
        try:
            self.font = pygame.font.SysFont(font_name or "consolas", font_size)
        except Exception:
            self.font = pygame.font.SysFont(None, font_size)
        self.margin = 12
        self.padding_xy = (10, 6)
        self.text_color = (240, 240, 240)
        self.bg_color = (0, 0, 0, 180)  # negro semi-transparente
        self.shadow = True

    def _draw_toast(self, screen: pygame.Surface, text: str, bx: int, by: int) -> int:
        # Render texto y fondo con padding
        surf = self.font.render(text, True, self.text_color)
        tw, th = surf.get_size()
        pad_x, pad_y = self.padding_xy
        w, h = tw + pad_x * 2, th + pad_y * 2
        # Fondo con alpha
        bg = pygame.Surface((w, h), pygame.SRCALPHA)
        bg.fill(self.bg_color)
        screen.blit(bg, (bx - w, by - h))
        # Texto
        screen.blit(surf, (bx - w + pad_x, by - h + pad_y))
        return h

    def update(self, world, screen: pygame.Surface, camera):
        try:
            if bool(getattr(world, 'suppress_hud', False)):
                return
            comps = getattr(world, 'components', None)
            if not isinstance(comps, dict):
                return
            q: List[Dict[str, Any]] = comps.setdefault('ToastQueue', [])
            if not q:
                return
            now = time.time()
            # Filtrar expirados in-place
            kept = []
            for item in q:
                try:
                    until = float(item.get('until', 0))
                except Exception:
                    until = 0
                if until > now:
                    kept.append(item)
            if len(kept) != len(q):
                comps['ToastQueue'] = kept
            if not kept:
                return
            # Mostrar los N más recientes, apilados hacia arriba desde la esquina inferior derecha
            to_show = kept[-2:]  # como máximo 2
            sw, sh = screen.get_size()
            x = sw - self.margin
            y = sh - self.margin
            for item in reversed(to_show):
                txt = str(item.get('text') or '')
                used_h = self._draw_toast(screen, txt, x, y)
                y -= (used_h + 8)
        except Exception:
            # Nunca romper el render loop por errores de HUD
            pass
