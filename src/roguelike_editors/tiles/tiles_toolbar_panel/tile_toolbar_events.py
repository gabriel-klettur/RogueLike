import pygame
from enum import Enum
from roguelike_editors.tiles.tiles_editor_config import TOOLS


class Tool(Enum):
    """
    Enumeración de herramientas disponibles en la toolbar.
    """
    DELETE = "delete"
    DEFAULT = "default"
    VIEW = "view"
    VIEW_LAYERS = "view_layers"
    VIEW_COLLISIONS = "view_collisions"
    BRUSH = "brush"
    SELECT = "select"
    # Agregar nuevas herramientas aquí si es necesario


class TileToolbarEventHandler:
    """
    Manejador de eventos para la barra de herramientas de tiles.
    Contiene lógica separada por herramienta para mayor claridad y mantenibilidad.
    """
    def __init__(self, toolbar_controller):
        self.controller = toolbar_controller
        # Mapear cada herramienta con su método handler
        self._click_handlers = {
            Tool.DELETE: self._handle_delete,
            Tool.DEFAULT: self._handle_default,
            Tool.VIEW: self._handle_view,
            Tool.VIEW_LAYERS: self._handle_view_layers,
            Tool.VIEW_COLLISIONS: self._handle_view_collisions,
            Tool.BRUSH: self._handle_brush,
            Tool.SELECT: self._handle_select,
        }

    def handle_click(self, event, map):
        """
        Procesa eventos de click izquierdo en la toolbar.
        Devuelve True si el evento fue consumido.
        """
        # Solo manejar clicks del botón izquierdo
        if event.type != pygame.MOUSEBUTTONDOWN or event.button != 1:
            return False

        mouse_pos = event.pos
        ts = self.controller.editor_state.toolbar_state

        # Buscar qué icono fue presionado
        for tool_name, rect in self.controller.icon_rects.items():
            if rect.collidepoint(mouse_pos):
                # Traducir string a enum (si es válido)
                try:
                    tool = Tool(tool_name)
                except ValueError:
                    tool = None

                # Obtener handler o usar selección por defecto
                handler = self._click_handlers.get(tool, self._handle_select)
                return handler(tool_name, map)

        return False

    def _handle_delete(self, tool_name, map):
        """Toggle delete tool; delete selected tile."""
        es = self.controller.editor_state
        # Toggle delete mode
        if es.current_tool != tool_name:
            es.current_tool = tool_name
        else:
            # Press again to return to select
            es.current_tool = "select"
        # Perform deletion
        self.controller.delete_tile(map)
        return True

    def _handle_default(self, tool_name, map):
        """Activate default tool; restore tile to default."""
        es = self.controller.editor_state
        # Activate default mode and deactivate other tools
        es.current_tool = tool_name
        self.controller.set_default(map)
        return True

    def _handle_view(self, tool_name, map):
        """Alternar la vista general de la toolbar."""
        ts = self.controller.editor_state.toolbar_state
        ts.view_active = not ts.view_active
        return True

    def _handle_view_layers(self, tool_name, map):
        """Alternar la vista de capas."""
        ts = self.controller.editor_state.toolbar_state
        ts.layers_view_open = not ts.layers_view_open
        return True

    def _handle_view_collisions(self, tool_name, map):
        """Cicla modos de colisión y abre/cierra el picker correspondiente."""
        ts = self.controller.editor_state.toolbar_state
        # Ciclar modos: off -> only -> overlay -> off
        if not ts.show_collisions and not ts.show_collisions_overlay:
            ts.show_collisions = True
            ts.show_collisions_overlay = False
        elif ts.show_collisions and not ts.show_collisions_overlay:
            ts.show_collisions_overlay = True
        else:
            ts.show_collisions = False
            ts.show_collisions_overlay = False

        active = ts.show_collisions or ts.show_collisions_overlay
        if active:
            self.controller.editor_state.current_tool = "brush"
            ts.collision_picker_open = True
            self.controller.editor_state.picker_state.open = False
        else:
            ts.collision_picker_open = False
            ts.collision_choice = None

        ts.layers_view_open = False
        return True

    def _handle_select(self, tool_name, map):
        """Selecciona la herramienta indicada y cierra el selector si es "select". Eyedropper mantiene Tiles View Panel."""
        ts = self.controller.editor_state.toolbar_state
        self.controller.editor_state.current_tool = tool_name
        if tool_name == "select":
            self.controller.editor_state.picker_state.open = False
        if tool_name == "eyedropper":
            ts.view_active = True
        return True

    def _handle_brush(self, tool_name, map):
        """Gestiona la lógica de la herramienta pincel (brush)."""
        ts = self.controller.editor_state.toolbar_state
        # Si hay colisiones activas, alternar el picker de colisiones
        if ts.show_collisions or ts.show_collisions_overlay:
            ts.collision_picker_open = not ts.collision_picker_open
            if ts.collision_picker_open:
                self.controller.editor_state.picker_state.open = False
        else:
            # Si venimos de otra herramienta, inicializar brush limpiamente
            if self.controller.editor_state.current_tool != tool_name:
                # Activar brush y mostrar paneles
                self.controller.editor_state.current_tool = tool_name
                self.controller.editor_controller.size_panel_controller.show()
                self.controller.editor_state.picker_state.open = True
                self.controller.editor_state.toolbar_state.view_active = True
            else:
                # Alternar panel de tamaño de pincel
                self.controller.editor_controller.size_panel_controller.toggle()
                visible = self.controller.editor_controller.size_panel_controller.state.visible
                # Sincronizar picker de tiles con el estado del panel
                self.controller.editor_state.picker_state.open = visible
                # Mantener brush o volver a select según visibilidad
                self.controller.editor_state.current_tool = tool_name if visible else "select"
        return True

    def handle_event(self, ev):
        """Drag & drop de la toolbar con el botón derecho."""
        ts = self.controller.editor_state.toolbar_state

        if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 3:
            return self._start_drag(ev.pos)

        if ev.type == pygame.MOUSEMOTION and ts.dragging:
            return self._drag(ev.pos)

        if ev.type == pygame.MOUSEBUTTONUP and ev.button == 3 and ts.dragging:
            return self._stop_drag()

        return False

    def _start_drag(self, mouse_pos):
        """Inicia el arrastre si el click derecho está sobre la toolbar."""
        ts = self.controller.editor_state.toolbar_state
        x0, y0 = ts.pos if ts.pos is not None else (self.controller.x, self.controller.y)
        panel_w = self.controller.size
        panel_h = len(TOOLS) * (self.controller.size + self.controller.padding) - self.controller.padding
        panel_rect = pygame.Rect(x0, y0, panel_w, panel_h)

        if panel_rect.collidepoint(mouse_pos):
            ts.dragging = True
            ts.drag_offset = (mouse_pos[0] - x0, mouse_pos[1] - y0)
            return True
        return False

    def _drag(self, mouse_pos):
        """Mueve la toolbar mientras se arrastra."""
        self.controller.drag(mouse_pos)
        return True

    def _stop_drag(self):
        """Detiene el arrastre al soltar el botón derecho."""
        self.controller.stop_drag()
        return True
