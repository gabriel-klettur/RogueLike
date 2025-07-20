import pygame
from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.buttons_view import ButtonsView
from roguelike_editors.inventory.right_panel.inventory_items_panel.grid.grid_view import GridView
from roguelike_editors.inventory.right_panel.inventory_items_panel.tabs.tabs_view import TabsView

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
        self.buttons_view = ButtonsView(font, button_size, margin)
        self.grid_view = GridView(font, slot_size, margin, get_item_image_func, logger)
        self.tabs_view = TabsView(font, button_size, margin)
        
        # Rects de compatibilidad
        self.show_default_rect = None
        self.show_active_rect = None
        self.add_item_rect = None
        self.delete_item_rect = None
        self.save_rect = None
        
    
    # Propiedades de compatibilidad para acceso directo
    @property
    def delete_qty_input(self):
        return self.buttons_view.delete_qty_input
    
    @property
    def delete_qty_input_rect(self):
        return self.buttons_view.delete_qty_input_rect

    def draw(self, overlay, model, panel_rect):
        """
        Dibuja grid de inventario, botones de mostrar y botón de guardar.
        Delega en subvistas especializadas.
        Devuelve un dict con los rects:
          'show_default', 'show_active', 'save', 'add_item', 'delete_item'
        """
        # Obtener datos de slots y posición
        slots = self.tabs_view.get_slots_data(model)
        grid_origin_x, grid_origin_y = self._get_grid_origin(panel_rect)
        mx, my = pygame.mouse.get_pos()
        
        # Estados para las subvistas
        delete_mode_active = model.grid_model.show_delete_mode
        current_editing_side = model.editing_side
        
        rects = {}
        
        # Show Default/Active buttons (delegado a buttons_view)
        show_rects = self.buttons_view.draw_show_buttons(
            overlay, grid_origin_x, grid_origin_y, mx, my, 
            current_editing_side, len(slots)
        )
        rects.update(show_rects)
        # Actualizar rects de compatibilidad
        self.show_default_rect = show_rects.get('show_default')
        self.show_active_rect = show_rects.get('show_active')
        
        # Grid de slots (delegado a grid_view)
        self.grid_view.draw_slots(
            overlay, slots, grid_origin_x, grid_origin_y, mx, my, delete_mode_active
        )
        
        # Add/Delete buttons (delegado a buttons_view)
        manage_rects = self.buttons_view.draw_manage_buttons(
            overlay, grid_origin_x, grid_origin_y, mx, my, 
            delete_mode_active, len(slots)
        )
        rects.update(manage_rects)
        # Actualizar rects de compatibilidad
        self.add_item_rect = manage_rects.get('add_item')
        self.delete_item_rect = manage_rects.get('delete_item')
        
        # Delete quantity input (si está en modo delete)
        if delete_mode_active:
            self.buttons_view.draw_delete_quantity_input(
                overlay, grid_origin_x, grid_origin_y, mx, my, len(slots),
                self.add_item_rect, self.delete_item_rect
            )
        
        # Save button (delegado a buttons_view)
        save_rects = self.buttons_view.draw_save_button(
            overlay, grid_origin_x, grid_origin_y, mx, my, 
            len(slots), delete_mode_active
        )
        rects.update(save_rects)
        # Actualizar rect de compatibilidad
        self.save_rect = save_rects.get('save')
        
        return rects

    # Método obsoleto - ahora delegado a tabs_view.get_slots_data()

    def _get_grid_origin(self, panel_rect):
        return panel_rect.x + panel_rect.width + self.margin, panel_rect.y

    # Método obsoleto - ahora delegado a grid_view.draw_slots()

    # Método obsoleto - ahora delegado a buttons_view.draw_show_buttons()

    # Método obsoleto - ahora delegado a buttons_view.draw_save_button()


    def get_slot_index(self, pos, panel_rect, count):
        """
        Retorna el índice de slot bajo la posición `pos`, o None.
        Delega en grid_view.
        """
        grid_origin_x, grid_origin_y = self._get_grid_origin(panel_rect)
        return self.grid_view.get_slot_index(pos, grid_origin_x, grid_origin_y, count)

    # Método obsoleto - ahora delegado a buttons_view.draw_manage_buttons()
