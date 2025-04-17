# roguelike_project/systems/editor/buildings/building_editor.py
import pygame
from roguelike_project.systems.editor.buildings.tools.resize_tool import ResizeTool
from roguelike_project.systems.editor.buildings.tools.default_tool import DefaultTool
from roguelike_project.systems.editor.buildings.tools.z_tool      import ZTool
from roguelike_project.systems.editor.buildings.tools.split_tool  import SplitTool


class BuildingEditor:
    """Agrupa todas las herramientas y ofrece una API de eventos de mouse."""

    def __init__(self, state, editor_state):
        self.state = state
        self.editor = editor_state

        # Herramientas
        self.resize_tool = ResizeTool(state, editor_state)
        self.default_tool = DefaultTool(state, editor_state)
        self.split_tool = SplitTool(state, editor_state)
        self.z_tool_bottom = ZTool(state, editor_state, target="bottom")
        self.z_tool_top    = ZTool(state, editor_state, target="top")

    # =========================== EVENTOS ============================ #
    def on_mouse_down(self, pos, button):
        """button: 1 = izq, 3 = der"""
        mx, my = pos
        world_x = mx / self.state.camera.zoom + self.state.camera.offset_x
        world_y = my / self.state.camera.zoom + self.state.camera.offset_y

        # 1) Barra split (clic izq o der indistinto)
        for b in reversed(self.state.buildings):
            if self.split_tool.check_handle_click((mx, my), b):
                self.split_tool.start_drag(b)
                return

        # 2) Resize handle (clic der)
        if button == 3:
            for b in reversed(self.state.buildings):
                if self.resize_tool.check_resize_handle_click(mx, my, b):
                    self._start_resize(b, (mx, my))
                    return
                if self.default_tool.check_reset_handle_click(mx, my, b):
                    self.default_tool.apply_reset(b)
                    return

        # 3) Selección / drag de edificio (clic der)
        if button == 3:
            for b in reversed(self.state.buildings):
                if b.rect.collidepoint(world_x, world_y):
                    self._start_drag(b, world_x, world_y)
                    return

        # 4) Paneles Z (+ / –) (clic izq)
        if button == 1:
            self.z_tool_bottom.handle_mouse_click((mx, my))
            self.z_tool_top.handle_mouse_click((mx, my))

    def on_mouse_up(self, button):
        if self.editor.resizing:
            print("✅ Resize terminado.")
        if self.editor.split_dragging:
            print("✅ Split ratio fijado:",
                  round(self.editor.selected_building.split_ratio, 2))

        self.editor.dragging = False
        self.editor.resizing = False
        self.editor.split_dragging = False
        self.editor.selected_building = None

    def on_mouse_motion(self, pos):
        if self.editor.dragging or self.editor.resizing or self.editor.split_dragging:
            self.update()                       # reaprovecha la lógica existente

    # ======================== LÓGICA PRIVADA ======================== #
    def _start_resize(self, building, mouse_start):
        self.editor.selected_building = building
        self.editor.resizing = True
        self.editor.resize_origin = mouse_start
        self.editor.initial_size = building.image.get_size()
        print(f"🔧 Resize de {building.image_path} iniciado")

    def _start_drag(self, building, world_x, world_y):
        self.editor.selected_building = building
        self.editor.dragging = True
        self.editor.offset_x = world_x - building.x
        self.editor.offset_y = world_y - building.y
        print(f"🏗️ Arrastre de {building.image_path} iniciado")

    # ======================== ACTUALIZACIÓN ========================= #
    def update(self):
        if self.editor.dragging and self.editor.selected_building:
            mx, my = pygame.mouse.get_pos()
            wx = mx / self.state.camera.zoom + self.state.camera.offset_x
            wy = my / self.state.camera.zoom + self.state.camera.offset_y

            b = self.editor.selected_building
            b.x = wx - self.editor.offset_x
            b.y = wy - self.editor.offset_y
            b.rect.topleft = (b.x, b.y)

        elif self.editor.resizing and self.editor.selected_building:
            self.resize_tool.update_resizing(pygame.mouse.get_pos())

        elif self.editor.split_dragging:
            self.split_tool.update_drag(pygame.mouse.get_pos())

    # ========================== RENDER ============================== #
    def render_selection_outline(self, screen):
        if not self.editor.active:
            return
        cam = self.state.camera
        for b in self.state.buildings:
            x, y = cam.apply((b.x, b.y))
            w, h = cam.scale(b.image.get_size())
            pygame.draw.rect(screen, (255, 255, 255), pygame.Rect(x, y, w, h), 1)
            self.default_tool.render_reset_handle(screen, b)
            self.resize_tool.render_resize_handle(screen, b)
            self.split_tool.render(screen, b)
            self.z_tool_bottom.render(screen, b)
            self.z_tool_top.render(screen, b)
