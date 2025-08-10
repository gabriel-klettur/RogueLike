"""
Manejador de eventos para la toolbar de Items.
"""

import pygame


class ItemsToolBarPanelEventHandler:
    """
    Maneja eventos de la toolbar de Items.
    """
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model

    def handle_event(self, event):
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            pos = event.pos
            toolbar_view = getattr(self.controller, 'items_toolbar_view', None)
            widget = getattr(toolbar_view, 'widget', None)
            icon_rects = getattr(widget, 'icon_rects', {}) if widget else {}
            # Undo
            rect = icon_rects.get('undo')
            if rect and rect.collidepoint(pos):
                # Placeholder: se implementará luego
                return True
            # Redo
            rect = icon_rects.get('redo')
            if rect and rect.collidepoint(pos):
                # Placeholder: se implementará luego
                return True
            # Items toolbar principal
            rect = icon_rects.get('items_on_map')
            if rect and rect.collidepoint(pos):
                picker = getattr(getattr(self.controller, 'picker_controller', None), 'model', None)
                arm = getattr(self.controller, 'items_add_remove_model', None)
                if self.model.active_tool == 'items_on_map':
                    # Desactivar: ocultar picker y sub-toolbar
                    self.model.active_tool = None
                    if picker is not None:
                        picker.visible = False
                    if arm is not None:
                        arm.visible = False
                        # Limpiar herramienta activa y salir de modos
                        arm.active_tool = None
                    # Salir de modos si estaban activos
                    if getattr(self.controller.model, 'spawn_mode_active', False):
                        self.controller.exit_spawn_mode()
                    if getattr(self.controller.model, 'delete_mode_active', False):
                        self.controller.exit_delete_mode()
                else:
                    # Activar: mostrar picker y sub-toolbar
                    self.model.active_tool = 'items_on_map'
                    if picker is not None:
                        picker.visible = True
                    if arm is not None:
                        arm.visible = True
                        # No activar ningún modo por defecto
                        arm.active_tool = None
                return True
        return False

