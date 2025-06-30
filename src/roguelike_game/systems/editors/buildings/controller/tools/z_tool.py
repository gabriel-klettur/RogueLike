
# Path: src/roguelike_game/systems/editor/buildings/controller/tools/z_tool.py
import pygame
class ZTool:
    """
    Panel flotante para editar la capa Z de un edificio.
    — target  : "bottom" | "top"    
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
    def handle_mouse_click(self, mouse_pos, buildings):
        for b in buildings:
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

    def _update_z(self, building, delta):
        if self.target == "bottom":
            building.z_bottom = max(0, building.z_bottom + delta)
            self.state.z_state.set(building, building.z_bottom)
            print(f"⬇️  Z‑bottom nuevo: {building.z_bottom}")
        else:
            building.z_top = max(0, building.z_top + delta)
            print(f"⬆️  Z‑top nuevo: {building.z_top}")
    