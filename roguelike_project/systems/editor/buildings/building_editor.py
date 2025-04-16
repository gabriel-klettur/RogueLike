import pygame

from roguelike_project.systems.editor.buildings.tools.resize_tool import ResizeTool
from roguelike_project.systems.editor.buildings.tools.default_tool import DefaultTool
from roguelike_project.systems.editor.buildings.tools.z_tool import ZTool

class BuildingEditor:
    def __init__(self, state, editor_state):
        self.state = state
        self.editor = editor_state
        self.resize_tool = ResizeTool(state, editor_state)
        self.default_tool = DefaultTool(state, editor_state)
        self.z_tool = ZTool(state, editor_state)

    def update(self):
        if self.editor.dragging and self.editor.selected_building:
            mx, my = pygame.mouse.get_pos()
            world_x = mx / self.state.camera.zoom + self.state.camera.offset_x
            world_y = my / self.state.camera.zoom + self.state.camera.offset_y

            b = self.editor.selected_building
            b.x = world_x - self.editor.offset_x
            b.y = world_y - self.editor.offset_y
            b.rect.topleft = (b.x, b.y)

        elif self.editor.resizing and self.editor.selected_building:
            self.resize_tool.update_resizing(pygame.mouse.get_pos())

    def handle_mouse_down(self, pos):
        mx, my = pos
        world_x = mx / self.state.camera.zoom + self.state.camera.offset_x
        world_y = my / self.state.camera.zoom + self.state.camera.offset_y

        for building in reversed(self.state.buildings):
            if self.resize_tool.check_resize_handle_click(mx, my, building):
                self.editor.selected_building = building
                self.editor.resizing = True
                self.editor.resize_origin = (mx, my)
                self.editor.initial_size = building.image.get_size()
                print(f"🔧 Iniciando resize de {building.image_path} desde {self.editor.initial_size}")
                return

            if self.default_tool.check_reset_handle_click(mx, my, building):
                self.default_tool.apply_reset(building)
                return

        for building in reversed(self.state.buildings):
            if building.rect.collidepoint(world_x, world_y):
                self.editor.selected_building = building
                self.editor.dragging = True
                self.editor.offset_x = world_x - building.x
                self.editor.offset_y = world_y - building.y
                print(f"🏗️ Edificio seleccionado: {building.image_path}")
                break

        # Detectar clic en botones + o - del panel Z
        self.z_tool.handle_mouse_click((mx, my))

    def handle_mouse_up(self):
        if self.editor.resizing:
            print("✅ Resize terminado.")
            self.editor.resizing = False

        self.editor.dragging = False
        self.editor.selected_building = None

    def check_resize_handle_click(self, mx, my, building):
        handle_w, handle_h = self.resize_handle_size, self.resize_handle_size
        bx, by = self.state.camera.apply((building.x, building.y))
        bw, bh = self.state.camera.scale(building.image.get_size())

        handle_rect = pygame.Rect(bx + bw - handle_w, by, handle_w, handle_h)
        return handle_rect.collidepoint(mx, my)

    def update_resizing(self, mouse_pos):
        b = self.editor.selected_building
        if not b:
            return

        mx, my = mouse_pos
        start_mx, start_my = self.editor.resize_origin
        dx = mx - start_mx
        dy = my - start_my

        delta = max(dx, dy)
        w0, h0 = self.editor.initial_size
        new_size = max(50, w0 + delta)

        b.resize(new_size, new_size)

    def render_selection_outline(self, screen):
        if self.editor.active:
            for building in self.state.buildings:
                x, y = self.state.camera.apply((building.x, building.y))
                w, h = self.state.camera.scale(building.image.get_size())
                rect = pygame.Rect(x, y, w, h)

                # Dibujar marco
                pygame.draw.rect(screen, (255, 255, 255), rect, 1)

                # Renderizar herramientas para todos
                self.default_tool.render_reset_handle(screen, building)
                self.resize_tool.render_resize_handle(screen, building)
                self.z_tool.render(screen, building)  # ✅ Mostrar en todos los edificios

