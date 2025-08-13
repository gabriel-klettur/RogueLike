import pygame
from roguelike_ui.ui_blocker import is_blocked

class ColliderScopeToolView:
    def __init__(self, state, editor_state):
        self.state = state
        self.editor_state = editor_state
        self.font_cache: dict[int, pygame.font.Font] = {}

    def _get_font(self, size: int) -> pygame.font.Font:
        if size not in self.font_cache:
            self.font_cache[size] = pygame.font.SysFont("arial", int(size * 0.6), bold=True)
        return self.font_cache[size]

    def render(self, screen, building, camera):
        # Compute rect bottom-right
        x, y = camera.apply((building.x, building.y))
        w, h = camera.scale(building.image.get_size())
        size = max(15, min(65, int(w * 0.10)))
        rect = pygame.Rect(x + w - size, y + h - size, size, size)

        # Suppress hover and button visuals if UI is blocking
        mx, my = pygame.mouse.get_pos()
        if is_blocked(mx, my):
            return rect

        scope = getattr(building, 'collider_scope', getattr(self.editor_state, 'collider_scope', 'CG'))
        is_cg = (scope == 'CG')
        # Colors
        bg = (60, 180, 75) if is_cg else (120, 120, 120)
        border = (0, 0, 0)
        text_color = (0, 0, 0) if is_cg else (255, 255, 255)
        hover = rect.collidepoint(pygame.mouse.get_pos())

        pygame.draw.rect(screen, bg, rect)
        pygame.draw.rect(screen, border, rect, 2)
        if hover:
            pygame.draw.rect(screen, (255, 255, 0), rect, 3)

        label = 'CG' if is_cg else 'CU'
        font = self._get_font(size)
        txt = font.render(label, True, text_color)
        screen.blit(txt, txt.get_rect(center=rect.center))

        # Tooltip explicativo al hacer hover
        if hover:
            tip_lines = [
                ("CG: Global", (220, 220, 220)),
                ("Pinta en todos con misma imagen", (200, 200, 200)),
                ("Se GUARDA en collisions.json", (200, 255, 200)),
                ("", (0,0,0)),
                ("CU: Único", (220, 220, 220)),
                ("Pinta solo esta instancia", (200, 200, 200)),
                ("NO se guarda globalmente", (255, 200, 200)),
            ]
            tip_font = pygame.font.SysFont("arial", 14)
            pad = 6
            tw = 0
            th = 0
            line_h = tip_font.get_height()
            for line, _ in tip_lines:
                w, _ = tip_font.size(line)
                tw = max(tw, w)
                th += line_h
            w_surf = tw + pad * 2
            h_surf = th + pad * 2
            tip_surf = pygame.Surface((w_surf, h_surf), pygame.SRCALPHA)
            tip_surf.fill((30, 30, 30, 220))
            yoff = pad
            for line, color in tip_lines:
                if line:
                    txt2 = tip_font.render(line, True, color)
                    tip_surf.blit(txt2, (pad, yoff))
                yoff += line_h
            # Posicionar por encima/derecha del botón si hay espacio
            tip_x = rect.left + rect.width - w_surf
            tip_y = rect.top - h_surf - 4
            screen.blit(tip_surf, (tip_x, tip_y))

        return rect
