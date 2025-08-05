import pygame
from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.add_item.add_item_view import AddItemView
from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.delete.delete_view import DeleteView
from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.save.save_view import SaveView
from roguelike_editors.inventory.right_panel.inventory_items_panel.grid.grid_view import GridView
from roguelike_editors.inventory.right_panel.inventory_items_panel.tabs.tabs_view import TabsView

from roguelike_ui.ui_blocker import register_blocker
import logging
logger = logging.getLogger(__name__)

class InventoryItemsPanelView:
    """
    Vista principal que delega en subvistas especializadas:
    buttons, grid y tabs.
    """
    def __init__(self, font, slot_size, margin, button_size, get_item_image_func, images, logger):
        self.font = font
        self.slot_size = slot_size
        self.margin = margin
        self.button_size = button_size
        self.get_item_image = get_item_image_func
        self.images = images
        self.logger = logger
        
        # Subvistas especializadas
        self.add_view = AddItemView(font, button_size, margin)
        self.delete_view = DeleteView(font, button_size, margin)
        self.save_view = SaveView(font, button_size, margin)
        self.grid_view = GridView(font, slot_size, margin, get_item_image_func, logger)
        self.tabs_view = TabsView(font, button_size, margin)
        self._last_slots_repr = None
        self._last_category = None
        self._last_side = None
        
        # Rects de compatibilidad
        self.show_default_rect = None
        self.show_active_rect = None
        self.add_item_rect = None
        self.delete_item_rect = None
        self.save_rect = None
        
    
    # Propiedades de compatibilidad para acceso directo
    @property
    def delete_qty_input(self):
        return self.delete_view.delete_qty_input
    
    @property
    def delete_qty_input_rect(self):
        return self.delete_view.delete_qty_input_rect

    def draw(self, overlay, model, panel_rect):
        # Bloquear interacción bajo el panel
        if panel_rect:
            register_blocker(panel_rect)

        # [DEBUG][View] InventoryItemsPanelView.draw called. Category: %s, Editing side: %s
        # [DEBUG][View] slots_data: %s
        """
        Dibuja grid de inventario, botones de mostrar y botón de guardar.
        Delega en subvistas especializadas.
        Devuelve un dict con los rects:
          'show_default', 'show_active', 'save', 'add_item', 'delete_item'
        """
        # Obtener datos de slots y posición
        slots = self.tabs_view.get_slots_data(model)
        slots_repr = repr(slots)
        if slots_repr != self._last_slots_repr or model.current_category != self._last_category or model.editing_side != self._last_side:
            logger.debug(f"[DEBUG][View] InventoryItemsPanelView.draw new state: category={model.current_category}, side={model.editing_side}, slots={slots}")
            self._last_slots_repr = slots_repr
            self._last_category = model.current_category
            self._last_side = model.editing_side
        grid_origin_x, grid_origin_y = self._get_grid_origin(panel_rect)
        mx, my = pygame.mouse.get_pos()
        
        # Estados para las subvistas
        delete_mode_active = model.grid_model.show_delete_mode
        current_editing_side = model.editing_side
        
        rects = {}
        
        # Show Default/Active buttons (delegado a tabs_view)
        show_rects = self.tabs_view.draw_tabs(overlay, grid_origin_x, grid_origin_y, mx, my, current_editing_side, len(slots))
        # Rects de pestañas ya retornados        

        
        rects.update(show_rects)
        # Actualizar rects de compatibilidad
        self.show_default_rect = show_rects.get('show_default')
        self.show_active_rect = show_rects.get('show_active')
        
        # Grid de slots (delegado a grid_view)
        self.grid_view.draw_slots(
            overlay, slots, grid_origin_x, grid_origin_y, mx, my, delete_mode_active
        )
        
        # Add/Delete buttons (delegado a subviews)
        add_rects = self.add_view.draw(overlay, grid_origin_x, grid_origin_y, mx, my, len(slots))
        del_rects = self.delete_view.draw_button(overlay, grid_origin_x, grid_origin_y, mx, my, len(slots), delete_mode_active)
        manage_rects = {**add_rects, **del_rects}            

        
        rects.update(manage_rects)
        # Actualizar rects de compatibilidad
        self.add_item_rect = manage_rects.get('add_item')
        self.delete_item_rect = manage_rects.get('delete_item')
        
        # Delete quantity input (si está en modo delete)
        if delete_mode_active:
            self.delete_view.draw_input(overlay, grid_origin_x, grid_origin_y, mx, my, len(slots), self.add_item_rect, self.delete_item_rect)


            
        
        # Save button (delegado a save_view)
        save_rects = self.save_view.draw(overlay, grid_origin_x, grid_origin_y, mx, my, len(slots), delete_mode_active)


        
        rects.update(save_rects)
        # Actualizar rect de compatibilidad
        self.save_rect = save_rects.get('save')
        
        return rects

    # Método obsoleto - ahora delegado a tabs_view.get_slots_data()

    def _get_grid_origin(self, panel_rect):
        return panel_rect.x + panel_rect.width + self.margin, panel_rect.y

    # Método obsoleto - ahora delegado a grid_view.draw_slots()






    def get_slot_index(self, pos, panel_rect, count):
        """
        Retorna el índice de slot bajo la posición `pos`, o None.
        Delega en grid_view.
        """
        grid_origin_x, grid_origin_y = self._get_grid_origin(panel_rect)
        return self.grid_view.get_slot_index(pos, grid_origin_x, grid_origin_y, count)


