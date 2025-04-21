
import pygame

BTN_W, BTN_H = 30, 30           #! MOVER A CONSTANTES
PANEL_W, PANEL_H = 120, 40      #! MOVER A CONSTANTES


class ZTool:
    """
    Panel flotante para editar la capa Z de un edificio.
    — target  : "bottom" | "top"
    — evita parpadeo cacheando la superficie base por factor de zoom
    """    

    def __init__(self, state, editor_state, *, target: str = "bottom"):
        self.state = state
        self.editor_state = editor_state
        self.target = target          # bottom | top

        self.font = pygame.font.SysFont("Arial", 16)

        # Caches -------------------------------------------------------
        self._panel_cache: dict[float, pygame.Surface] = {}   # {zoom: Surface}
        self._text_cache: dict[tuple[str, float], pygame.Surface] = {}  # {(char,zoom):Surf}

    
    # ------------------------------------------------------------------ #
    # MOUSE CLICK                                                        #
    # ------------------------------------------------------------------ #
    def handle_mouse_click(self, mouse_pos):
        for b in self.state.buildings:
            bnd = getattr(b, "_ztool_bounds", {}).get(self.target)
            if not bnd:
                continue
            px, py = bnd["panel_pos"]
            if bnd["minus_rect"].move(px, py).collidepoint(mouse_pos):
                self._update_z(b, -1)
                return
            if bnd["plus_rect"].move(px, py).collidepoint(mouse_pos):
                self._update_z(b, +1)
                return

    #! DONDE VA ESTO???????????????
    #! ------------------------------------------------------------------ #
    #! helpers                                                            #
    #! ------------------------------------------------------------------ #
    def _update_z(self, building, delta):
        if self.target == "bottom":
            building.z_bottom = max(0, building.z_bottom + delta)
            self.state.z_state.set(building, building.z_bottom)
            print(f"⬇️  Z‑bottom nuevo: {building.z_bottom}")
        else:
            building.z_top = max(0, building.z_top + delta)
            print(f"⬆️  Z‑top nuevo: {building.z_top}")
    
    # ---------- caché de superficies ---------------------------------- #
    def _get_cached_panel_base(self, zoom: float) -> pygame.Surface:
        if zoom in self._panel_cache:
            return self._panel_cache[zoom]

        surf = pygame.Surface((self.PANEL_W, self.PANEL_H), pygame.SRCALPHA, 32)
        surf = surf.convert_alpha()
        surf.fill((0, 0, 0, 190))
        pygame.draw.rect(surf, (255, 255, 255), surf.get_rect(), 2, border_radius=6)

        # botones
        minus_rect = pygame.Rect(5, 5, self.BTN_W, self.BTN_H)
        plus_rect = pygame.Rect(self.PANEL_W - 5 - self.BTN_W, 5,
                                self.BTN_W, self.BTN_H)
        pygame.draw.rect(surf, (50, 50, 50), minus_rect, border_radius=4)
        pygame.draw.rect(surf, (50, 50, 50), plus_rect,  border_radius=4)

        surf.blit(self._get_text("-", zoom), (minus_rect.x + 10, minus_rect.y + 5))
        surf.blit(self._get_text("+", zoom), (plus_rect.x + 10, plus_rect.y + 5))

        self._panel_cache[zoom] = surf
        return surf
    
    def _get_text(self, txt: str, zoom: float) -> pygame.Surface:
        key = (txt, zoom)
        if key not in self._text_cache:
            self._text_cache[key] = self.font.render(txt, True, (255, 255, 255))
        return self._text_cache[key]
