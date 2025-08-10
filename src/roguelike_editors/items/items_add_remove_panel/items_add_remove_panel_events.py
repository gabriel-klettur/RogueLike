"""
Manejador de eventos para el sub-toolbar de añadir/eliminar Items.
"""

import pygame


class ItemsAddRemovePanelEventHandler:
    def __init__(self, controller, model):
        self.controller = controller  # InventoryEditorController
        self.model = model

    def handle_event(self, event):
        if not getattr(self.model, 'visible', False):
            return False
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            pos = event.pos
            view = getattr(self.controller, 'items_add_remove_view', None)
            widget = getattr(view, 'widget', None)
            icon_rects = getattr(widget, 'icon_rects', {}) if widget else {}
            for key in ('add_item', 'remove_item', 'add_item_on_system'):
                rect = icon_rects.get(key)
                if rect and rect.collidepoint(pos):
                    # Requiere que el toolbar principal esté en modo items_on_map
                    tb_model = getattr(self.controller, 'items_toolbar_model', None)
                    tb_active = getattr(tb_model, 'active_tool', None) if tb_model else None
                    if key == 'add_item' and tb_active == 'items_on_map':
                        if getattr(self.controller.model, 'spawn_mode_active', False):
                            # Toggle off
                            self.controller.exit_spawn_mode()
                            self.model.active_tool = None
                        else:
                            self.model.active_tool = key
                            self.controller.enter_spawn_mode()
                        return True
                    if key == 'remove_item' and tb_active == 'items_on_map':
                        if getattr(self.controller.model, 'delete_mode_active', False):
                            # Toggle off
                            self.controller.exit_delete_mode()
                            self.model.active_tool = None
                        else:
                            self.model.active_tool = key
                            self.controller.enter_delete_mode()
                        return True
                    if key == 'add_item_on_system':
                        # Placeholder: reservar para futuros flujos (p.ej. añadir definiciones/params)
                        self.model.active_tool = key
                        return True
        return False

