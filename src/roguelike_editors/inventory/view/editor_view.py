import pygame
import os
import logging
from roguelike_editors.inventory.model.editor_model import InventoryEditorModel
from roguelike_game.ecs.components.item_models import load_items
from roguelike_ui.widgets.scroll_panel import ScrollPanel
from roguelike_editors.inventory.view.inventory_grid_view import InventoryGridView

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
        # Botones de mostrar datos (default/active)
        self.show_default_rect = None
        self.show_active_rect = None
        # Cargar íconos de ítems
        cwd = os.getcwd()
        items_path = os.path.join(cwd, 'data', 'items', 'items.json')
        self.items = load_items(items_path)
        self.images = {}
        self.scroll_panel = ScrollPanel(self.font, margin=self.margin)
        # Rects para lista de ítems en flujo Add
        self.item_list_rects = []
        self.logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")
        # Instanciar vista de grid de inventario
        self.grid_view = InventoryGridView(
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
        # Overlay
        overlay = pygame.Surface((ow, oh), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 180))
        # Título
        title = f"Inv Editor - Eid {model.selected_eid}"
        text = self.font.render(title, True, (255,255,255))
        overlay.blit(text, (10,10))
        # Tabs
        self.tab_rects = self._draw_tabs(overlay, model)
        # ScrollPanel listing
        data = model.active_data.get(model.current_category, {})
        items = self._get_items_list(data, model.current_category)
        # Draw scroll panel on left
        panel_x = 10
        panel_y = 80
        cols = 5
        grid_w = self.slot_size * cols + self.margin * (cols - 1)
        panel_w = ow - grid_w - panel_x - 10
        panel_h = oh - panel_y - 10
        panel_rect = pygame.Rect(panel_x, panel_y, panel_w, panel_h)
        self.scroll_panel.set_items(items)
        self.scroll_panel.draw(overlay, panel_rect)
        # Persistent highlight for selected monster
        if model.current_category == 'monsters' and model.selected_eid is not None:
            items = self.scroll_panel.items
            mon_id = str(model.selected_eid)
            for idx, line in enumerate(items):
                if not line.startswith(' ') and line.strip().split(' ')[0] == mon_id:
                    line_h = self.font.get_linesize()
                    y0 = panel_rect.y - self.scroll_panel.scroll_offset
                    y_line = y0 + idx * line_h
                    sel_rect = pygame.Rect(panel_rect.x, y_line, panel_rect.width, line_h)
                    pygame.draw.rect(overlay, (255,255,0), sel_rect, 3)
                    break
        # Highlight monster ID on hover
        if model.current_category == 'monsters':
            mx, my = pygame.mouse.get_pos()
            if panel_rect.collidepoint(mx, my):
                line_h = self.font.get_linesize()
                idx = (my - panel_rect.y + self.scroll_panel.scroll_offset) // line_h
                items = self.scroll_panel.items
                if 0 <= idx < len(items) and not items[idx].startswith(' '):
                    y0 = panel_rect.y - self.scroll_panel.scroll_offset
                    y_line = y0 + idx * line_h
                    border_rect = pygame.Rect(panel_rect.x, y_line, panel_rect.width, line_h)
                    pygame.draw.rect(overlay, (255,255,0), border_rect, 2)
        # Grid de inventario (solo para player y monsters)
        if model.current_category in ('player', 'monsters'):
            self._draw_grid(overlay, model, panel_rect)
            # UI para flujo Add Item
            grid_model = model.grid_model
            if grid_model.show_item_list:
                # Panel de selección de ítemes
                panel_w = 200
                panel_h = min(len(grid_model.available_items), 10) * self.font.get_linesize() + 10
                panel_x = panel_rect.centerx - panel_w // 2
                panel_y = panel_rect.centery - panel_h // 2
                panel_rect_list = pygame.Rect(panel_x, panel_y, panel_w, panel_h)
                pygame.draw.rect(overlay, (50, 50, 50), panel_rect_list)
                pygame.draw.rect(overlay, (255, 255, 255), panel_rect_list, 2)
                self.item_list_rects = []
                for idx, item_id in enumerate(grid_model.available_items):
                    y = panel_y + 5 + idx * self.font.get_linesize()
                    txt = self.font.render(item_id, True, (255, 255, 255))
                    overlay.blit(txt, (panel_x + 5, y))
                    rect = pygame.Rect(panel_x + 5, y, txt.get_width(), self.font.get_linesize())
                    self.item_list_rects.append((rect, item_id))
            if grid_model.show_quantity_input:
                # Panel de input de cantidad
                input_w = 150
                input_h = self.font.get_linesize() + 10
                input_x = panel_rect.centerx - input_w // 2
                input_y = panel_rect.centery - input_h // 2
                input_rect = pygame.Rect(input_x, input_y, input_w, input_h)
                pygame.draw.rect(overlay, (50, 50, 50), input_rect)
                pygame.draw.rect(overlay, (255, 255, 255), input_rect, 2)
                qty_text = self.font.render(str(grid_model.quantity), True, (255, 255, 255))
                overlay.blit(qty_text, (input_x + 5, input_y + 5))
        return overlay

    def _draw_tabs(self, overlay, model):
        tab_rects = []
        tab_x = 10
        tab_y = 40
        for cat in model.categories:
            label = cat.capitalize()
            txt = self.font.render(label, True, (255,255,255))
            w, h = txt.get_size()
            padding = 10
            rect = pygame.Rect(tab_x, tab_y, w + padding*2, h + padding//2)
            if model.current_category == cat:
                color = (100,100,100)
            else:
                color = (50,50,50)
            pygame.draw.rect(overlay, color, rect)
            pygame.draw.rect(overlay, (255,255,255), rect, 2)
            if model.current_category == cat:
                pygame.draw.rect(overlay, (255,255,0), rect, 2)
            overlay.blit(txt, (tab_x + padding, tab_y + (rect.height - h)//2))
            tab_rects.append((rect, cat))
            tab_x += rect.width + 5
        return tab_rects

    def _get_items_list(self, data, category):
        items = []
        if category == 'player':
            for entry in data.values() if isinstance(data, dict) else []:
                for slot in entry.get('slots', []):
                    if slot:
                        items.append(f"{slot.get('item')} x{slot.get('quantity')}")
        elif category == 'monsters':
            for mon_id, entry in data.items() if isinstance(data, dict) else []:
                items.append(f"{mon_id} ({entry.get('template_id', '')})")
                for slot in entry.get('slots', []):
                    if slot:
                        items.append(f"  {slot.get('item')} x{slot.get('quantity')}")
        elif category == 'map':
            for entry in data.values() if isinstance(data, dict) else []:
                pos = entry.get('position', {})
                items.append(f"{entry.get('item_id')} x{entry.get('quantity')} @({pos.get('x'):.1f},{pos.get('y'):.1f})")
        return items      

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
        self.save_default_rect = rects.get('save_default')
        self.save_active_rect = rects.get('save_active')
        # Exponer rects de Add/Delete para manejo de eventos
        self.add_item_rect = rects.get('add_item')
        self.delete_item_rect = rects.get('delete_item')
        return
