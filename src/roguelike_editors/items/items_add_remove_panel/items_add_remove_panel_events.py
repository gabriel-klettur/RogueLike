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
                            # Tutorial pulse: add mode on
                            try:
                                setattr(self.controller.model, 'tutorial_add_mode_on_pulse', True)
                            except Exception:
                                pass
                        return True
                    if key == 'remove_item':
                        # Asegurar toolbar principal en 'items_on_map' y picker visible
                        try:
                            if tb_active != 'items_on_map' and tb_model is not None:
                                tb_model.active_tool = 'items_on_map'
                                # Mostrar picker explícitamente
                                try:
                                    self.controller.picker_controller.model.visible = True
                                except Exception:
                                    pass
                        except Exception:
                            pass
                        if getattr(self.controller.model, 'delete_mode_active', False):
                            # Toggle off
                            self.controller.exit_delete_mode()
                            self.model.active_tool = None
                        else:
                            self.model.active_tool = key
                            self.controller.enter_delete_mode()
                            # Tutorial pulse: remove mode on
                            try:
                                setattr(self.controller.model, 'tutorial_remove_mode_on_pulse', True)
                            except Exception:
                                pass
                        return True
                    if key == 'add_item_on_system':
                        # Toggle modo de añadir ítem al sistema
                        if self.model.active_tool == 'add_item_on_system':
                            # Cerrar modo
                            self.model.active_tool = None
                            try:
                                pp_model = self.controller.properties_controller.model
                                pp_model.show_add_system_selector = False
                                # Restaurar layout UI
                                if hasattr(self.controller, 'exit_add_items_on_system_mode'):
                                    self.controller.exit_add_items_on_system_mode()
                            except Exception:
                                pass
                        else:
                            # Asegurar que no estamos en modos que ocultan/alteran el flujo
                            if getattr(self.controller.model, 'spawn_mode_active', False):
                                self.controller.exit_spawn_mode()
                            if getattr(self.controller.model, 'delete_mode_active', False):
                                self.controller.exit_delete_mode()
                            self.model.active_tool = key
                            try:
                                # Mostrar selector en Properties Panel (si la vista lo usa)
                                pp_model = self.controller.properties_controller.model
                                pp_model.show_add_system_selector = True
                            except Exception:
                                pass
                            try:
                                if hasattr(self.controller, 'enter_add_items_on_system_mode'):
                                    self.controller.enter_add_items_on_system_mode()
                            except Exception:
                                pass
                            # Tutorial pulse: add item on system mode on
                            try:
                                setattr(self.controller.model, 'tutorial_add_system_mode_on_pulse', True)
                            except Exception:
                                pass
                        return True
            return False
        return False
