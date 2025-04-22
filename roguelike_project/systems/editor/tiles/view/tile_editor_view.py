import pygame
from roguelike_project.config import TILE_SIZE
from roguelike_project.systems.editor.tiles.tiles_editor_config import OUTLINE_CHOICE, OUTLINE_SEL, OUTLINE_HOVER
from roguelike_project.utils.loader import load_image

# Importamos los render de toolbar y picker directamente del paquete tiles
from roguelike_project.systems.editor.tiles.view.tools.tile_toolbar_view import render as render_toolbar
from roguelike_project.systems.editor.tiles.view.tools.tile_picker_view import render as render_picker


class TileEditorControllerView:
    def __init__(self, controller, state, editor_state):
        self.controller = controller
        self.state      = state
        self.editor     = editor_state

        # Simplificamos el acceso
        self.toolbar = controller.toolbar
        self.picker  = controller.picker

    def render(self, screen):
        if not self.editor.active:
            return

        # 1) Toolbar y toggle view
        render_toolbar(self.toolbar, screen)

        # 2) Picker de tiles (si está abierta)
        render_picker(self.picker, screen)

        # 3) Panel de “vista previa” si está activo
        if self.editor.view_active:
            self._render_view_panel(screen)

        # 4) Contornos en el mundo (hover y seleccionado)
        cam = self.state.camera

        # Hover
        hover = self.controller._tile_under_mouse(pygame.mouse.get_pos())
        if hover:
            rect = pygame.Rect(
                cam.apply((hover.x, hover.y)),
                cam.scale((TILE_SIZE, TILE_SIZE))
            )
            pygame.draw.rect(screen, OUTLINE_HOVER, rect, 3)

        # Seleccionado
        sel = self.editor.selected_tile
        if sel:
            rect = pygame.Rect(
                cam.apply((sel.x, sel.y)),
                cam.scale((TILE_SIZE, TILE_SIZE))
            )
            pygame.draw.rect(screen, OUTLINE_SEL, rect, 3)

    def _render_view_panel(self, screen):
        panel_w = TILE_SIZE + 40
        panel_h = 3 * (TILE_SIZE + 30)
        x0 = self.toolbar.x + self.toolbar.size + 20
        y0 = self.toolbar.y

        panel = pygame.Surface((panel_w, panel_h), pygame.SRCALPHA)
        panel.fill((20, 20, 20, 200))
        font = pygame.font.SysFont("Arial", 14)
        mouse_pos = pygame.mouse.get_pos()

        items = [
            ("Hovered",  self.controller._tile_under_mouse(mouse_pos), OUTLINE_HOVER),
            ("Selected", self.editor.selected_tile,                     OUTLINE_SEL),
            ("Choice",   None,                                         OUTLINE_CHOICE),
        ]

        for idx, (label, tile, color) in enumerate(items):
            ty = idx * (TILE_SIZE + 30) + 10

            # Sprite a mostrar
            if label == "Choice" and self.editor.current_choice:
                sprite = load_image(self.editor.current_choice, (TILE_SIZE, TILE_SIZE))
            else:
                sprite = tile.sprite if tile else None

            # Pintar sprite y su borde
            if sprite:
                panel.blit(sprite, ((panel_w - TILE_SIZE)//2, ty))
            rect = pygame.Rect((panel_w - TILE_SIZE)//2, ty, TILE_SIZE, TILE_SIZE)
            pygame.draw.rect(panel, color, rect, 3)

            # Etiqueta
            text = font.render(label, True, (255, 255, 255))
            panel.blit(text, (5, ty + TILE_SIZE + 2))

        screen.blit(panel, (x0, y0))
