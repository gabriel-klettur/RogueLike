# Path: src/roguelike_game/systems/editor/buildings/controller/building_editor_controller.py
import pygame
from roguelike_game.systems.editor.buildings.controller.tools.resize_tool import ResizeTool
from roguelike_game.systems.editor.buildings.controller.tools.default_tool import DefaultTool
from roguelike_game.systems.editor.buildings.controller.tools.z_tool      import ZTool
from roguelike_game.systems.editor.buildings.controller.tools.split_tool  import SplitTool
from roguelike_game.systems.editor.buildings.controller.tools.placer_tool  import PlacerTool
from roguelike_game.systems.editor.buildings.controller.tools.delete_tool  import DeleteTool

#! Picker
from roguelike_game.systems.editor.buildings.controller.picker.picker_controller import BuildingPickerController

class BuildingEditorController:
    """Agrupa todas las herramientas y ofrece una API de eventos de mouse."""

    def __init__(self, state, editor_state, buildings, camera):
        self.state = state
        self.editor = editor_state
        
        self.resize_tool = ResizeTool(state, editor_state)
        self.default_tool = DefaultTool(state, editor_state)
        from roguelike_game.systems.editor.buildings.view.tools.default_tool_view import DefaultToolView
        self.default_view = DefaultToolView(state, editor_state)
        self.split_tool = SplitTool(state, editor_state)
        self.z_tool_bottom = ZTool(state, editor_state, target="bottom")
        self.z_tool_top    = ZTool(state, editor_state, target="top")        
        self.placer_tool = PlacerTool(
            state, editor_state,
            building_class=type(buildings[0]),
            default_image="assets/buildings/others/portal.png",
            default_scale=(512, 824),
            default_solid=True,
        )
        self.delete_tool = DeleteTool(state, editor_state, camera)

        self.picker = BuildingPickerController(editor_state, self.placer_tool)

    # =========================== EVENTOS ============================ #
    def on_mouse_down(self, pos, button, camera, buildings):
        """button: 1 = izq, 3 = der"""
        mx, my = pos

        world_x = mx / camera.zoom + camera.offset_x
        world_y = my / camera.zoom + camera.offset_y


        # 1) Barra split (clic izq o der indistinto)
        for b in reversed(buildings):
            if self.split_tool.check_handle_click((mx, my), b, camera):
                self.split_tool.start_drag(b)
                return

        # 2) Botón eliminar (clic izq)
        if button == 1:
            # Usar la vista para el botón rojo
            if hasattr(self, 'default_view'):
                get_rect = self.default_view.get_delete_handle_rect
            else:
                # fallback por si acaso
                get_rect = lambda b, c: None
            for b in reversed(buildings):
                delete_rect = get_rect(b, camera)
                if delete_rect and delete_rect.collidepoint(mx, my):
                    self._delete_building(b, buildings)
                    return

        # 2b) Resize handle (clic der)
        if button == 3:
            for b in reversed(buildings):
                if self.resize_tool.check_resize_handle_click(mx, my, b, camera):
                    self._start_resize(b, (mx, my))
                    return
                if self.default_tool.check_reset_handle_click(mx, my, b, camera):
                    self.default_tool.apply_reset(b)
                    return


        # 3) Selección / drag de edificio (clic der)
        if button == 3:
            hovered = self.editor.hovered_building
            if hovered and hovered.rect.collidepoint(world_x, world_y):
                self._start_drag(hovered, world_x, world_y)
                return
            # Si no hay hovered_building, comportamiento clásico
            for b in reversed(buildings):
                if b.rect.collidepoint(world_x, world_y):
                    self._start_drag(b, world_x, world_y)
                    return

        # 4) Paneles Z (+ / –) (clic izq)
        if button == 1:
            self.z_tool_bottom.handle_mouse_click((mx, my), buildings)
            self.z_tool_top.handle_mouse_click((mx, my), buildings)

    def on_mouse_up(self, button, camera, buildings):

        # 2) Finalizar resize / split
        if self.editor.resizing:
            print("✅ Resize terminado.")
        if self.editor.split_dragging:
            print("✅ Split ratio fijado:", round(self.editor.selected_building.split_ratio, 2))

        # 3) Reset de flags de arrastre
        self.editor.dragging = False
        self.editor.resizing = False
        self.editor.split_dragging = False
        self.editor.selected_building = None

    def on_mouse_motion(self, pos, camera, buildings):
        # Si estamos arrastrando/redimensionando, solo actualiza
        if self.editor.dragging or self.editor.resizing or self.editor.split_dragging:
            self.update(camera)
            return
        # Detectar todos los edificios bajo el mouse (orden arriba-abajo)
        hovered_list = self._buildings_under_mouse(pos, camera, buildings)
        self.editor.hovered_buildings = hovered_list
        # Si el índice está fuera de rango, lo reiniciamos
        if self.editor.hovered_building_index >= len(hovered_list):
            self.editor.hovered_building_index = 0
        # hovered_building es el seleccionado por el índice
        if hovered_list:
            self.editor.hovered_building = hovered_list[self.editor.hovered_building_index]
        else:
            self.editor.hovered_building = None

    def _buildings_under_mouse(self, mouse_pos, camera, buildings):
        mx, my = mouse_pos
        wx = mx / camera.zoom + camera.offset_x
        wy = my / camera.zoom + camera.offset_y
        result = []
        for b in reversed(buildings):  # Reversed para priorizar el más arriba
            x, y = b.x, b.y
            w, h = b.image.get_size()
            rect = pygame.Rect(x, y, w, h)
            if rect.collidepoint(wx, wy):
                result.append(b)
        return result

    def _building_under_mouse(self, mouse_pos, camera, buildings):
        mx, my = mouse_pos
        wx = mx / camera.zoom + camera.offset_x
        wy = my / camera.zoom + camera.offset_y
        # buildings puede estar en self.state.entities.buildings o inyectarse, aquí usamos self.state.entities.buildings
        for b in reversed(buildings):  # Reversed para priorizar el más arriba (por si se solapan)
            x, y = b.x, b.y
            w, h = b.image.get_size()
            rect = pygame.Rect(x, y, w, h)
            if rect.collidepoint(wx, wy):
                return b
        return None

    def toggle_editor(self):
        """Activa/desactiva los handles del Building Editor, sin tocar el picker."""
        new_val = not self.editor.active
        self.editor.active = new_val
        print("🟩 Building Editor ON" if new_val else "🟥 Building Editor OFF")

    def toggle_picker(self):
        """Activa/desactiva solo el picker (listado de assets)."""
        new_val = not self.editor.picker_active
        self.editor.picker_active = new_val
        print("📂 Building Picker ON" if new_val else "📂 Building Picker OFF")


    # ======================== LÓGICA PRIVADA ======================== #
    def _delete_building(self, building, buildings):
        print(f"❌ Eliminando edificio: {building} en index {buildings.index(building)}")
        # Elimina el edificio y lo guarda para undo
        if not hasattr(self.editor, 'undo_stack'):
            self.editor.undo_stack = []
        idx = buildings.index(building)
        self.editor.undo_stack.append((building, idx))
        buildings.remove(building)
        # Limpia selección/hover si corresponde
        if self.editor.selected_building == building:
            self.editor.selected_building = None
        if self.editor.hovered_building == building:
            self.editor.hovered_building = None

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
    def update(self, camera):
        if self.editor.dragging and self.editor.selected_building:
            mx, my = pygame.mouse.get_pos()
            wx = mx / camera.zoom + camera.offset_x
            wy = my / camera.zoom + camera.offset_y

            b = self.editor.selected_building
            b.x = wx - self.editor.offset_x
            b.y = wy - self.editor.offset_y
            b.rect.topleft = (b.x, b.y)

        elif self.editor.resizing and self.editor.selected_building:
            self.resize_tool.update_resizing(pygame.mouse.get_pos())

        elif self.editor.split_dragging:
            self.split_tool.update_drag(pygame.mouse.get_pos(), camera)