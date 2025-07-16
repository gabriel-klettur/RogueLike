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
        # Paneles y botones para flujo Add Item
        self.item_list_panel_rect = None
        self.quantity_panel_rect = None
        self.add_to_inventory_button_rect = None
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
        # Overlay semitransparente
        overlay = pygame.Surface((ow, oh), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 180))
        # Título
        title = f"Inv Editor - Eid {model.selected_eid}"
        text = self.font.render(title, True, (255,255,255))
        overlay.blit(text, (10,10))
        # Pestañas
        self.tab_rects = self._draw_tabs(overlay, model)
        # Panel izquierdo de listado
        data = model.active_data.get(model.current_category, {})
        items = self._get_items_list(data, model.current_category)
        panel_x, panel_y = 10, 80
        cols = 5
        grid_w = self.slot_size * cols + self.margin * (cols - 1)
        panel_w = ow - grid_w - panel_x - 10
        panel_h = oh - panel_y - 10
        panel_rect = pygame.Rect(panel_x, panel_y, panel_w, panel_h)
        # Dibujar listbox con scroll
        self.scroll_panel.set_items(items)
        self.scroll_panel.draw(overlay, panel_rect)
        # Highlight permanente de monster
        if model.current_category == 'monsters' and model.selected_eid is not None:
            line_h = self.font.get_linesize()
            y0 = panel_rect.y - self.scroll_panel.scroll_offset
            for idx, line in enumerate(self.scroll_panel.items):
                if not line.startswith(' ') and line.split()[0] == str(model.selected_eid):
                    y_line = y0 + idx * line_h
                    sel_r = pygame.Rect(panel_rect.x, y_line, panel_rect.width, line_h)
                    pygame.draw.rect(overlay, (255,255,0), sel_r, 3)
                    break
        # Highlight on hover
        if model.current_category == 'monsters':
            mx, my = pygame.mouse.get_pos()
            if panel_rect.collidepoint(mx, my):
                line_h = self.font.get_linesize()
                idx = (my - panel_rect.y + self.scroll_panel.scroll_offset) // line_h
                items = self.scroll_panel.items
                if 0 <= idx < len(items) and not items[idx].startswith(' '):
                    y0 = panel_rect.y - self.scroll_panel.scroll_offset
                    y_line = y0 + idx * line_h
                    border_r = pygame.Rect(panel_rect.x, y_line, panel_rect.width, line_h)
                    pygame.draw.rect(overlay, (255,255,0), border_r, 2)
        # Dibujar grid y flujo Add Item
        if model.current_category in ('player', 'monsters'):
            self._draw_grid(overlay, model, panel_rect)
            gm = model.grid_model
            if gm.show_item_list:
                self._draw_item_selection_panel(overlay, gm, panel_rect)
            if gm.show_quantity_input:
                self._draw_quantity_input_panel(overlay, gm, panel_rect)
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




    def _draw_item_selection_panel(self, overlay, grid_model, base_panel_rect):
        """
        Dibuja el panel de selección de ítems con scroll.
        """
        line_h = self.font.get_linesize()
        visible_count = min(len(grid_model.available_items), 10)
        panel_w = 200
        panel_h = visible_count * line_h + 2 * self.margin
        panel_x = base_panel_rect.centerx - panel_w // 2
        panel_y = base_panel_rect.centery - panel_h // 2
        panel_rect = pygame.Rect(panel_x, panel_y, panel_w, panel_h)
        pygame.draw.rect(overlay, (50, 50, 50), panel_rect)
        pygame.draw.rect(overlay, (255, 255, 255), panel_rect, 2)
        # ScrollPanel para ítems
        self.scroll_panel.set_items(grid_model.available_items)
        self.scroll_panel.draw(overlay, panel_rect)
        self.item_list_panel_rect = panel_rect

    def _draw_quantity_input_panel(self, overlay, grid_model, base_panel_rect):
        """
        Dibuja el panel de input de cantidad y botón Add.
        """
        if not self.item_list_panel_rect:
            return
        line_h = self.font.get_linesize()
        panel_x = self.item_list_panel_rect.x
        panel_w = self.item_list_panel_rect.width
        # calcular tamaño de panel: input + margen + botón + margen
        input_h = line_h + self.margin * 2
        button_h = self.button_size[1]
        panel_h = input_h + self.margin + button_h + self.margin
        panel_y = self.item_list_panel_rect.bottom + self.margin
        panel_rect = pygame.Rect(panel_x, panel_y, panel_w, panel_h)
        pygame.draw.rect(overlay, (50, 50, 50), panel_rect)
        pygame.draw.rect(overlay, (255, 255, 255), panel_rect, 2)
        # Input cantidad
        input_rect = pygame.Rect(panel_x + self.margin, panel_y + self.margin, panel_w - 2 * self.margin, line_h + self.margin)
        pygame.draw.rect(overlay, (30, 30, 30), input_rect)
        pygame.draw.rect(overlay, (255, 255, 255), input_rect, 1)
        qty_text = self.font.render(str(grid_model.quantity), True, (255, 255, 255))
        overlay.blit(qty_text, (input_rect.x + 5, input_rect.y + (input_rect.height - line_h) // 2))
        # Botón Add to Inventory
        btn_x = panel_x + self.margin
        btn_y = input_rect.bottom + self.margin
        btn_w = panel_w - 2 * self.margin
        btn_h = button_h
        btn_rect = pygame.Rect(btn_x, btn_y, btn_w, btn_h)
        pygame.draw.rect(overlay, (100, 100, 100), btn_rect)
        pygame.draw.rect(overlay, (255, 255, 255), btn_rect, 2)
        text = self.font.render("Add to Inventory", True, (255, 255, 255))
        overlay.blit(text, (btn_rect.x + (btn_w - text.get_width()) // 2, btn_rect.y + (btn_h - line_h) // 2))
        self.add_to_inventory_button_rect = btn_rect

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
