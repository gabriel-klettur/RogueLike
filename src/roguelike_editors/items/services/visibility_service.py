from __future__ import annotations

from typing import Any


class ItemsVisibilityService:
    """Gestiona mostrar/ocultar/toggle del Items Editor y efectos asociados."""

    def __init__(self, controller: Any) -> None:
        self.c = controller

    def show(self) -> None:
        self.c.model.visible = True
        # El Picker se mantiene oculto hasta que se pulse 'items_on_map'
        self.c.picker_controller.model.visible = False
        self.c.instances_controller.model.visible = True
        # Refrescar datos de instancias al mostrar
        self.c.instances_controller.reload_data()
        # Toolbar principal visible por defecto, sub-toolbar oculto
        if hasattr(self.c, 'items_add_remove_model'):
            self.c.items_add_remove_model.visible = False
        if hasattr(self.c, 'items_toolbar_model'):
            self.c.items_toolbar_model.active_tool = None

    def hide(self) -> None:
        self.c.model.visible = False
        self.c.picker_controller.model.visible = False
        self.c.instances_controller.model.visible = False
        # Ocultar toolbars y limpiar selección
        if hasattr(self.c, 'items_add_remove_model'):
            self.c.items_add_remove_model.visible = False
            self.c.items_add_remove_model.active_tool = None
        if hasattr(self.c, 'items_toolbar_model'):
            self.c.items_toolbar_model.active_tool = None

    def toggle(self) -> None:
        if self.c.model.visible:
            self.hide()
        else:
            self.show()
