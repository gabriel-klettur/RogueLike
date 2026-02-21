import os
import json


class SaveController:
    """
    Controlador para flujo de guardar inventario (Save Default/Active).
    """
    def __init__(self, editor_controller, parent_controller):
        self.editor_controller = editor_controller
        self.parent = parent_controller

    def save_default(self):
        """
        Guarda el inventario por defecto en JSON.
        """
        cat = self.editor_controller.model.current_category
        path = self.editor_controller.paths[cat]['default']
        try:
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path, 'w', encoding='utf-8') as f:
                json.dump(self.editor_controller.model.default_data.get(cat, {}), f, indent=2)
            self.editor_controller.logger.info(f"Default inventory for '{cat}' saved to {path}")
        except Exception as e:
            self.editor_controller.logger.error(f"Error saving default inventory for '{cat}' to {path}: {e}")

    def save_active(self):
        """
        Guarda el inventario activo en JSON.
        """
        cat = self.editor_controller.model.current_category
        path = self.editor_controller.paths[cat]['active']
        try:
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path, 'w', encoding='utf-8') as f:
                json.dump(self.editor_controller.model.active_data.get(cat, {}), f, indent=2)
            self.editor_controller.logger.info(f"Active inventory for '{cat}' saved to {path}")
        except Exception as e:
            self.editor_controller.logger.error(f"Error saving active inventory for '{cat}' to {path}: {e}")
