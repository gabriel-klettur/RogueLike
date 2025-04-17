# roguelike_project/systems/editor/buildings/building_editor.py

import pygame

from roguelike_project.systems.editor.buildings.tools.resize_tool import ResizeTool
from roguelike_project.systems.editor.buildings.tools.default_tool import DefaultTool
from roguelike_project.systems.editor.buildings.tools.z_tool import ZTool
from roguelike_project.systems.editor.buildings.tools.split_tool import SplitTool


class BuildingEditor:
    """
    Controla todas las herramientas del editor de edificios:

    •  Arrastrar / soltar el edificio completo
    •  Redimensionar manteniendo proporción 1:1
    •  Resetear al tamaño original
    •  Barra “split” para dividir en bottom/top
    •  Dos Z‑tools independientes (bottom y top)
    """

    def __init__(self, state, editor_state):
        self.state = state
        self.editor = editor_state

        # Herramientas básicas
        self.resize_tool = ResizeTool(state, editor_state)
        self.default_tool = DefaultTool(state, editor_state)

        # Herramientas específicas para el nuevo sistema bipartito
        self.split_tool = SplitTool(state, editor_state)
        self.z_tool_bottom = ZTool(state, editor_state, target="bottom")
        self.z_tool_top = ZTool(state, editor_state, target="top")

        # ---- Alias para código legado (editor_events.py) -------------
        self.z_tool = self.z_tool_bottom



    # ------------------------------------------------------------------ #
    # UPDATE                                                             #
    # ------------------------------------------------------------------ #
    def update(self):
        # --- arrastrar edificio completo ---
        if self.editor.dragging and self.editor.selected_building:
            mx, my = pygame.mouse.get_pos()
            world_x = mx / self.state.camera.zoom + self.state.camera.offset_x
            world_y = my / self.state.camera.zoom + self.state.camera.offset_y

            b = self.editor.selected_building
            b.x = world_x - self.editor.offset_x
            b.y = world_y - self.editor.offset_y
            b.rect.topleft = (b.x, b.y)

        # --- redimensionar ---
        elif self.editor.resizing and self.editor.selected_building:
            self.resize_tool.update_resizing(pygame.mouse.get_pos())

        # --- barra split ---
        elif self.editor.split_dragging:
            self.split_tool.update_drag(pygame.mouse.get_pos())

    # ------------------------------------------------------------------ #
    # MOUSE DOWN                                                         #
    # ------------------------------------------------------------------ #
    def handle_mouse_down(self, pos):
        mx, my = pos
        world_x = mx / self.state.camera.zoom + self.state.camera.offset_x
        world_y = my / self.state.camera.zoom + self.state.camera.offset_y

        # ---------- 1) barra split ----------
        for b in reversed(self.state.buildings):
            if self.split_tool.check_handle_click((mx, my), b):
                self.split_tool.start_drag(b)
                return

        # ---------- 2) resize handle ----------
        for b in reversed(self.state.buildings):
            if self.resize_tool.check_resize_handle_click(mx, my, b):
                self.editor.selected_building = b
                self.editor.resizing = True
                self.editor.resize_origin = (mx, my)
                self.editor.initial_size = b.image.get_size()
                print(f"🔧 Iniciando resize de {b.image_path} desde {self.editor.initial_size}")
                return

            # ---------- 3) botón reset ----------
            if self.default_tool.check_reset_handle_click(mx, my, b):
                self.default_tool.apply_reset(b)
                return

        # ---------- 4) seleccionar para arrastrar ----------
        for b in reversed(self.state.buildings):
            if b.rect.collidepoint(world_x, world_y):
                self.editor.selected_building = b
                self.editor.dragging = True
                self.editor.offset_x = world_x - b.x
                self.editor.offset_y = world_y - b.y
                print(f"🏗️ Edificio seleccionado: {b.image_path}")
                break

        # ---------- 5) Z‑tools (+ / –) ----------
        self.z_tool_bottom.handle_mouse_click((mx, my))
        self.z_tool_top.handle_mouse_click((mx, my))

    # ------------------------------------------------------------------ #
    # MOUSE UP                                                           #
    # ------------------------------------------------------------------ #
    def handle_mouse_up(self):
        if self.editor.resizing:
            print("✅ Resize terminado.")
        if self.editor.split_dragging:
            print("✅ Split ratio fijado:", round(self.editor.selected_building.split_ratio, 2))

        self.editor.resizing = False
        self.editor.dragging = False
        self.editor.split_dragging = False
        self.editor.selected_building = None


    def update_resizing(self, mouse_pos):
        """Mantiene compatibilidad con editor_events.py."""
        self.resize_tool.update_resizing(mouse_pos)

    # ------------------------------------------------------------------ #
    # RENDER                                                             #
    # ------------------------------------------------------------------ #
    def render_selection_outline(self, screen):
        """
        Se llama en modo editor: pinta contorno, handles y paneles sobre TODOS
        los edificios visibles.
        """
        if not self.editor.active:
            return

        cam = self.state.camera

        for b in self.state.buildings:
            # contorno blanco
            x, y = cam.apply((b.x, b.y))
            w, h = cam.scale(b.image.get_size())
            pygame.draw.rect(screen, (255, 255, 255), pygame.Rect(x, y, w, h), 1)

            # herramientas
            self.default_tool.render_reset_handle(screen, b)
            self.resize_tool.render_resize_handle(screen, b)
            self.split_tool.render(screen, b)
            self.z_tool_bottom.render(screen, b)
            self.z_tool_top.render(screen, b)
