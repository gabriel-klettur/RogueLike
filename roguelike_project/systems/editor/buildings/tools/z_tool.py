# roguelike_project/systems/editor/buildings/tools/z_tool.py
import pygame


class ZTool:
    """
    Pequeño panel semitransparente con botones «– / +» para modificar la capa Z
    de un edificio.  Se instancia dos veces: una para la parte *bottom* y otra
    para la parte *top* del building.

    Parameters
    ----------
    target : str
        "bottom" o "top"  →  indica a qué mitad del edificio afecta.
    """

    def __init__(self, state, editor_state, *, target: str = "bottom"):
        self.state = state
        self.editor_state = editor_state

        self.target = target        # "bottom" | "top"
        self.button_size = (30, 30)
        self.font = pygame.font.SysFont("Arial", 16)

    # ------------------------------------------------------------------ #
    # Render                                                             #
    # ------------------------------------------------------------------ #
    def render(self, screen: pygame.Surface, building):
        cam = self.state.camera
        x, y = cam.apply((building.x, building.y))
        w, h = cam.scale(building.image.get_size())

        panel_width, panel_height = 120, 40
        if self.target == "bottom":
            panel_y = y + h - 50            # parte inferior del sprite
        else:
            panel_y = y + 10                # parte superior
        panel_x = x + (w - panel_width) // 2

        # --- dibujar panel ---
        panel = pygame.Surface((panel_width, panel_height), pygame.SRCALPHA)
        panel.fill((0, 0, 0, 190))
        pygame.draw.rect(panel, (255, 255, 255), panel.get_rect(), 2, border_radius=6)

        # botón «–»
        minus_rect = pygame.Rect(5, 5, *self.button_size)
        pygame.draw.rect(panel, (50, 50, 50), minus_rect, border_radius=4)
        panel.blit(self.font.render("-", True, (255, 255, 255)),
                   (minus_rect.x + 10, minus_rect.y + 5))

        # valor Z
        z_value = building.z_bottom if self.target == "bottom" else building.z_top
        txt = self.font.render(f"Z: {z_value}", True, (255, 255, 255))
        panel.blit(txt, txt.get_rect(center=(panel_width // 2, panel_height // 2)))

        # botón «+»
        plus_rect = pygame.Rect(panel_width - 5 - self.button_size[0], 5, *self.button_size)
        pygame.draw.rect(panel, (50, 50, 50), plus_rect, border_radius=4)
        panel.blit(self.font.render("+", True, (255, 255, 255)),
                   (plus_rect.x + 10, plus_rect.y + 5))

        # blit final
        screen.blit(panel, (panel_x, panel_y))

        # guardar bounds clicables
        if not hasattr(building, "_ztool_bounds"):
            building._ztool_bounds = {}
        building._ztool_bounds[self.target] = {
            "panel_pos": (panel_x, panel_y),
            "minus_rect": minus_rect,
            "plus_rect": plus_rect,
        }

    # ------------------------------------------------------------------ #
    # Eventos de mouse                                                   #
    # ------------------------------------------------------------------ #
    def handle_mouse_click(self, mouse_pos):
        for building in self.state.buildings:
            bounds_all = getattr(building, "_ztool_bounds", None)
            if not bounds_all or self.target not in bounds_all:
                continue

            bounds = bounds_all[self.target]
            panel_x, panel_y = bounds["panel_pos"]
            minus_rect = bounds["minus_rect"].move(panel_x, panel_y)
            plus_rect = bounds["plus_rect"].move(panel_x, panel_y)

            if minus_rect.collidepoint(mouse_pos):
                self._update_building_z(building, delta=-1)
                return
            if plus_rect.collidepoint(mouse_pos):
                self._update_building_z(building, delta=+1)
                return

    # ------------------------------------------------------------------ #
    # Interno                                                            #
    # ------------------------------------------------------------------ #
    def _update_building_z(self, building, *, delta: int):
        if self.target == "bottom":
            building.z_bottom = max(0, building.z_bottom + delta)
            # mantener sincronizado el z_state global (parte baja)
            self.state.z_state.set(building, building.z_bottom)
            print(f"⬇️  Z‑bottom nuevo: {building.z_bottom}")
        else:
            building.z_top = max(0, building.z_top + delta)
            print(f"⬆️  Z‑top nuevo: {building.z_top}")
