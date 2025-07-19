import pygame
from roguelike_editors.inventory.model.right_panel.item_selection_panel_model import ItemSelectionPanelModel
from roguelike_editors.inventory.controller.right_panel.item_selection_panel_controller import ItemSelectionPanelController
from roguelike_editors.inventory.view.right_panel.item_selection_panel_view import ItemSelectionPanelView
import os
import logging
from roguelike_editors.inventory.model.editor_model import InventoryEditorModel
from roguelike_game.ecs.components.item_models import load_items

from roguelike_editors.inventory.model.left_panel.panel_model import InventoryPanelModel
from roguelike_editors.inventory.view.left_panel import PanelView
from roguelike_editors.inventory.view.right_panel.inventory_items_panel.inventory_items_panel_view import InventoryItemsPanelView

class InventoryEditorView:
    """
    Vista del editor de inventario (MVC): dibuja grid y botones.
    """
    def __init__(self, assets: dict, font: pygame.font.Font):
        self.assets = assets
        self.font = font
        self.slot_size = 50
        self.margin = 5
        self.grid_origin = (50, 50)
        self.button_size = (120, 30)
        self.tab_rects = []
        self.save_default_rect = None
        self.save_active_rect = None
        self.save_rect = None
        # Botones de mostrar datos (default/active)
        self.show_default_rect = None
        self.show_active_rect = None
        # Cargar íconos de ítems
        cwd = os.getcwd()
        items_path = os.path.join(cwd, 'data', 'items', 'items.json')
        self.items = load_items(items_path)
        self.images = {}

        # Paneles y botones para flujo Add Item
        self.logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")
        # Instanciar vista de grid de inventario
        # Panel MVC para item selection
        self.item_panel_model = ItemSelectionPanelModel()
        self.item_panel_controller = ItemSelectionPanelController(self.item_panel_model)
        self.item_panel_view = ItemSelectionPanelView(self.font, margin=self.margin, button_size=self.button_size)
        # Panel MVC para listado de entidades
        self.inventory_panel_model = InventoryPanelModel()
        self.inventory_panel_view = PanelView(self.font, margin=self.margin)

        # Instanciar vista de grid de inventario
        self.grid_view = InventoryItemsPanelView(
            font=self.font,
            slot_size=self.slot_size,
            margin=self.margin,
            button_size=self.button_size,
            get_item_image_func=self._get_item_image,
            images=self.images,
            logger=self.logger
        )
        
        # Preparar subcomponentes si es necesario

    def draw(self, screen, model: InventoryEditorModel, world):
        if not model.visible:
            return
        ow, oh = screen.get_size()
        overlay = self._draw_overlay(ow, oh, model)
        screen.blit(overlay, (0,0))
        return

    def _draw_overlay(self, ow, oh, model):
        # Overlay semitransparente
        overlay = pygame.Surface((ow, oh), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 180))
        # Título
        title = f"Inv Editor - Eid {model.selected_eid}"
        text = self.font.render(title, True, (255,255,255))
        overlay.blit(text, (10,10))
        # Panel izquierdo de listado (delegado a InventoryPanelView)
        panel_x, panel_y = 10, 80
        cols = 5
        grid_w = self.slot_size * cols + self.margin * (cols - 1)
        panel_w = ow - grid_w - panel_x - 10
        panel_h = oh - panel_y - 10
        panel_rect = pygame.Rect(panel_x, panel_y, panel_w, panel_h)
        # Guardar rectángulo del panel izquierdo para eventos de grid
        self.left_panel_rect = panel_rect
        # Obtener lista de elementos para panel
        items = self.inventory_panel_controller.get_items_list()
        # Dibujar panel mediante MVC
        panel_rects = self.inventory_panel_view.draw(overlay, self.inventory_panel_model, panel_rect, items)
        # Guardar rectángulos de pestañas y panel para eventos
        self.tab_rects = panel_rects.get('tab_rects')
        # Dibujar grid y flujo Add Item
        if model.current_category in ('player', 'monsters'):
            self._draw_grid(overlay, model, panel_rect)
            # Item selection panel: position just below the Save buttons of the inventory grid
            # Use grid width and Save buttons bottom as base for panel
            grid_origin_x = panel_rect.x + panel_rect.width + self.margin
            # Use unified Save button bottom
            save_bottom = self.save_rect.bottom if self.save_rect else panel_rect.y
            base_rect = pygame.Rect(grid_origin_x, save_bottom, grid_w, 0)
            rects = self.item_panel_view.draw(overlay, self.item_panel_model, base_rect)
            # Save panel rects for events
            self.item_list_panel_rect = rects.get('panel_rect')
            self.item_list_header_rect = rects.get('header_rect')
            self.add_to_inventory_button_rect = rects.get('add_button_rect')
        return overlay








      

    def get_slot_at_pos(self, pos, count):
        x, y = pos
        origin_x, origin_y = self.grid_origin
        y0 = origin_y + 30
        cols = min(count, 10)
        for idx in range(count):
            col = idx % cols
            row = idx // cols
            rx = origin_x + col * (self.slot_size + self.margin)
            ry = y0 + row * (self.slot_size + self.margin)
            rect = pygame.Rect(rx, ry, self.slot_size, self.slot_size)
            if rect.collidepoint(x, y):
                return idx
        return None

    def _get_item_image(self, item_id):
        if item_id in self.images:
            return self.images[item_id]
        model = self.items.get(item_id)
        if not model:
            return None
        icon = getattr(model, 'icon_small', None) or (model.icon[0] if isinstance(model.icon, list) else model.icon)
        if not icon:
            return None
        try:
            raw = pygame.image.load(os.path.join(os.getcwd(), icon)).convert_alpha()
            img = pygame.transform.scale(raw, (self.slot_size-10, self.slot_size-10))
            self.images[item_id] = img
            return img
        except Exception as e:
            self.logger.error(f"Error loading image for item '{item_id}': {e}")
            return None

    def _draw_grid(self, overlay, model, panel_rect):
        # Delegar renderizado del grid al InventoryGridView
        rects = self.grid_view.draw(overlay, model, panel_rect)
        # Asignar rects para manejo de eventos
        self.show_default_rect = rects.get('show_default')
        self.show_active_rect = rects.get('show_active')
        self.save_rect = rects.get('save')
        # Exponer rects de Add/Delete para manejo de eventos
        self.add_item_rect = rects.get('add_item')
        self.delete_item_rect = rects.get('delete_item')
        # Highlight Add Item button when panel open
        if self.item_panel_model.show_panel:
            pygame.draw.rect(overlay, (255,255,0), self.add_item_rect, 2)
        return
