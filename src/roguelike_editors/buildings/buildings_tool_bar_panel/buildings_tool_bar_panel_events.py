"""
Manejador de eventos para la toolbar de Buildings.
"""

import pygame


class BuildingsToolBarPanelEventHandler:
    """
    Maneja eventos de la toolbar de Buildings.
    """
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model

    def handle_event(self, event) -> bool:
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            pos = event.pos
            toolbar_view = getattr(self.controller, 'view', None)
            widget = getattr(toolbar_view, 'widget', None)
            icon_rects = getattr(widget, 'icon_rects', {}) if widget else {}

            # Tutorial Building (toggle panel de tutorial)
            rect = icon_rects.get('tutorial_building')
            if rect and rect.collidepoint(pos):
                st = self.controller.editor_state
                tutorial = getattr(self.controller.editor_manager, 'tutorial', None)
                if getattr(self.model, 'active_tool', None) == 'tutorial_building':
                    # Apagar tutorial
                    self.model.active_tool = None
                    try:
                        if tutorial:
                            tutorial.deactivate()
                    except Exception:
                        pass
                else:
                    # Encender tutorial, desactivar otros modos que interfieran
                    self.model.active_tool = 'tutorial_building'
                    # Cerrar picker y Add/Remove
                    try:
                        st.picker_active = False
                        add_remove = getattr(self.controller.editor_manager, 'add_remove', None)
                        if add_remove and add_remove.is_active():
                            add_remove.deactivate()
                    except Exception:
                        pass
                    # Desactivar colliders si estuviera activo
                    try:
                        colliders = getattr(self.controller.editor_manager, 'colliders', None)
                        if colliders and colliders.is_active():
                            colliders.deactivate()
                    except Exception:
                        pass
                    # Activar tutorial
                    try:
                        if tutorial:
                            tutorial.activate()
                    except Exception:
                        pass
                return True

            # Undo
            rect = icon_rects.get('undo')
            if rect and rect.collidepoint(pos):
                # Placeholder: análogo a Items, aún sin pila dedicada
                try:
                    # Usar el handler del editor si existe
                    handler = getattr(self.controller.editor_manager, 'handler', None)
                    buildings = getattr(self.controller.editor_manager.game.buildings, 'buildings', None) if hasattr(self.controller.editor_manager, 'game') else None
                    if handler and buildings:
                        # _undo_delete espera la lista de buildings
                        handler._undo_delete(buildings)
                except Exception:
                    pass
                return True

            # Redo (placeholder)
            rect = icon_rects.get('redo')
            if rect and rect.collidepoint(pos):
                # A implementar en el futuro
                return True

            # Buildings manager (toggle picker de assets)
            rect = icon_rects.get('buildings_manager')
            if rect and rect.collidepoint(pos):
                st = self.controller.editor_state
                if getattr(self.model, 'active_tool', None) == 'buildings_manager':
                    self.model.active_tool = None
                    st.picker_active = False
                    # Cerrar panel Add/Remove si existe
                    try:
                        add_remove = getattr(self.controller.editor_manager, 'add_remove', None)
                        if add_remove and add_remove.is_active():
                            add_remove.deactivate()
                    except Exception:
                        pass
                else:
                    # Cambiar selección activa en la toolbar
                    self.model.active_tool = 'buildings_manager'
                    # Desactivar panel de colisiones si está activo
                    try:
                        colliders = getattr(self.controller.editor_manager, 'colliders', None)
                        if colliders and colliders.is_active():
                            colliders.deactivate()
                    except Exception:
                        pass
                    # Activar panel Add/Remove
                    try:
                        add_remove = getattr(self.controller.editor_manager, 'add_remove', None)
                        if add_remove:
                            add_remove.activate()
                    except Exception:
                        pass
                    # Abrir picker
                    st.picker_active = True
                return True

            # Buildings colliders (toggle panel especializado)
            rect = icon_rects.get('buildings_colliders')
            if rect and rect.collidepoint(pos):
                st = self.controller.editor_state
                colliders = getattr(self.controller.editor_manager, 'colliders', None)
                if getattr(self.model, 'active_tool', None) == 'buildings_colliders':
                    # Apagar panel de colisión
                    self.model.active_tool = None
                    try:
                        if colliders:
                            colliders.deactivate()
                    except Exception:
                        pass
                else:
                    # Encender panel de colisión
                    self.model.active_tool = 'buildings_colliders'
                    st.picker_active = False
                    # Desactivar Add/Remove si estuviese activo
                    try:
                        add_remove = getattr(self.controller.editor_manager, 'add_remove', None)
                        if add_remove and add_remove.is_active():
                            add_remove.deactivate()
                    except Exception:
                        pass
                    try:
                        if colliders:
                            colliders.activate()
                    except Exception:
                        pass
                return True

        return False

