from roguelike_editors.inventory.model.inventory_panel_model import InventoryPanelModel

class InventoryPanelController:
    """
    Controlador para la selección de entidades (tabs + listado).
    """
    def __init__(self, editor_controller, model: InventoryPanelModel):
        self.editor_controller = editor_controller
        self.model = model

    def change_category(self, category: str):
        # Cambiar categoría de listado
        self.model.current_category = category
        # Al cambiar categoría, resetear selección
        self.model.selected_eid = None
        self.editor_controller.model.current_category = category

    def select_entity(self, eid):
        # Seleccionar entidad (actualiza modelo de panel y modelo de editor)
        self.model.selected_eid = eid
        self.editor_controller.model.selected_eid = eid

    def get_items_list(self):
        # Construir lista de elementos para la categoría actual usando active_data
        ed_model = self.editor_controller.model
        data = ed_model.active_data.get(self.model.current_category, {})
        items = []
        if self.model.current_category == 'player':
            for entry in data.values() if isinstance(data, dict) else []:
                for slot in entry.get('slots', []):
                    if slot:
                        items.append(f"{slot.get('item')} x{slot.get('quantity')}")
        elif self.model.current_category == 'monsters':
            for mon_id, entry in data.items() if isinstance(data, dict) else []:
                items.append(f"{mon_id} ({entry.get('template_id', '')})")
                for slot in entry.get('slots', []):
                    if slot:
                        items.append(f"  {slot.get('item')} x{slot.get('quantity')}")
        else:
            for entry in data.values() if isinstance(data, dict) else []:
                pos = entry.get('position', {})
                items.append(f"{entry.get('item_id')} x{entry.get('quantity')} @({pos.get('x'):.1f},{pos.get('y'):.1f})")
        return items
