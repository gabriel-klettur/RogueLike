# roguelike_project/systems/editor/buildings/tools/z_tool.py
import pygame


class ZTool:
    """
    Panel flotante para editar la capa Z de un edificio.
    — target  : "bottom" | "top"
    — evita parpadeo cacheando la superficie base por factor de zoom
    """

    BTN_W, BTN_H = 30, 30
    PANEL_W, PANEL_H = 120, 40

    def __init__(self, state, editor_state, *, target: str = "bottom"):
        self.state = state
        self.editor_state = editor_state
        self.target = target          # bottom | top

        self.font = pygame.font.SysFont("Arial", 16)

        # Caches -------------------------------------------------------
        self._panel_cache: dict[float, pygame.Surface] = {}   # {zoom: Surface}
        self._text_cache: dict[tuple[str, float], pygame.Surface] = {}  # {(char,zoom):Surf}

    # ------------------------------------------------------------------ #
    # RENDER                                                             #
    # ------------------------------------------------------------------ #
    def render(self, screen: pygame.Surface, building):
        cam = self.state.camera
        x_world, y_world = building.x, building.y
        w_scaled, h_scaled = cam.scale(building.image.get_size())
        x, y = cam.apply((x_world, y_world))

        # posición del panel
        panel_x = x + (w_scaled - self.PANEL_W) // 2
        panel_y = y + (h_scaled - 50 if self.target == "bottom" else 10)

        # superficie base cacheada por zoom
        zoom = round(cam.zoom, 2)
        base = self._get_cached_panel_base(zoom)

        # copiar y añadir el valor Z
        panel = base.copy()
        z_val = building.z_bottom if self.target == "bottom" else building.z_top
        txt = self._get_text(f"Z: {z_val}", zoom)
        panel.blit(txt, txt.get_rect(center=(self.PANEL_W // 2,
                                             self.PANEL_H // 2)))

        # blit final
        screen.blit(panel, (panel_x, panel_y))

        # ===== guardar bounds para detección de clic =====
        minus_rect = pygame.Rect(5, 5, self.BTN_W, self.BTN_H)
        plus_rect = pygame.Rect(self.PANEL_W - 5 - self.BTN_W, 5,
                                self.BTN_W, self.BTN_H)
        bounds = {
            "panel_pos": (panel_x, panel_y),
            "minus_rect": minus_rect,
            "plus_rect": plus_rect,
        }
        if not hasattr(building, "_ztool_bounds"):
            building._ztool_bounds = {}
        building._ztool_bounds[self.target] = bounds

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

    # ------------------------------------------------------------------ #
    # helpers                                                            #
    # ------------------------------------------------------------------ #
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
