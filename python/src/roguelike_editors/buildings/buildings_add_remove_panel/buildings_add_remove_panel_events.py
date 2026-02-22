"""
Eventos del panel de Add/Remove del Buildings Editor.
"""

import pygame


class BuildingsAddRemovePanelEventHandler:
    def __init__(self, state, editor_state, controller, model):
        self.state = state
        self.editor = editor_state
        self.controller = controller
        self.model = model

    def handle(self, event, camera, buildings) -> bool:
        if not getattr(self.model, 'active', False):
            return False

        # Hover simple
        if event.type == pygame.MOUSEMOTION:
            pos = event.pos
            hovered = None
            for key, rect in self.model.icon_rects.items():
                if rect.collidepoint(pos):
                    hovered = key
                    break
            self.model.hovered_key = hovered
            return hovered is not None

        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            pos = event.pos
            # Chequear hit en iconos
            for key, rect in self.model.icon_rects.items():
                if rect.collidepoint(pos):
                    # Toggle de selección visual en el ToolbarView
                    if getattr(self.model, 'active_tool', None) == key:
                        self.model.active_tool = None
                        # Al desactivar la herramienta, apagar modo eliminar
                        try:
                            if key == 'remove_building':
                                self.editor.remove_mode_active = False
                        except Exception:
                            pass
                    else:
                        self.model.active_tool = key
                    if key == 'add_building':
                        # Abrir picker
                        self.editor.picker_active = True
                        # Cambiar a modo normal (no eliminar)
                        try:
                            self.editor.remove_mode_active = False
                        except Exception:
                            pass
                        return True
                    if key == 'remove_building':
                        # Activar modo eliminar; si hay edificio activo, eliminarlo inmediatamente
                        try:
                            self.editor.remove_mode_active = (self.model.active_tool == 'remove_building')
                        except Exception:
                            pass
                        if self.model.active_tool == 'remove_building':
                            try:
                                entities = getattr(self.controller.editor_manager, 'game', None)
                                ab = getattr(self.editor, 'active_building', None)
                                if entities is not None and ab is not None:
                                    # Usar lógica centralizada del editor para eliminar y registrar undo/pulsos
                                    self.controller.editor_manager.controller._delete_building(ab, entities.buildings)
                                    # Mantener modo eliminar activo para acciones subsecuentes
                                    return True
                            except Exception:
                                pass
                        return True
                    if key == 'add_building_on_system':
                        # Placeholder: podría abrir un diálogo o similar
                        # Por ahora, también abrimos el picker
                        self.editor.picker_active = True
                        # Cambiar a modo normal (no eliminar)
                        try:
                            self.editor.remove_mode_active = False
                        except Exception:
                            pass
                        return True
            # No hizo hit en iconos
            return False

        return False

