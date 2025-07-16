import pygame
import os
import logging
from roguelike_editors.inventory.model.editor_model import InventoryEditorModel
from roguelike_game.ecs.components.item_models import load_items
from roguelike_ui.widgets.scroll_panel import ScrollPanel

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
        # Cargar íconos de ítems
        cwd = os.getcwd()
        items_path = os.path.join(cwd, 'data', 'items', 'items.json')
        self.items = load_items(items_path)
        self.images = {}
        self.scroll_panel = ScrollPanel(self.font, margin=self.margin)
        self.logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")
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
                items.append(f"{mon_id}")
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
        """Dibuja grid de inventario y botones de guardar."""
        data = model.active_data.get(model.current_category, {})
        entry = data.get(str(model.selected_eid), {})
        slots = entry.get('slots', [])
        # Posicionar grid junto al panel
        grid_origin_x = panel_rect.x + panel_rect.width + self.margin
        grid_origin_y = panel_rect.y
        cols = 5
        for idx, slot in enumerate(slots):
            col = idx % cols
            row = idx // cols
            rx = grid_origin_x + col * (self.slot_size + self.margin)
            ry = grid_origin_y + row * (self.slot_size + self.margin)
            slot_rect = pygame.Rect(rx, ry, self.slot_size, self.slot_size)
            pygame.draw.rect(overlay, (80,80,80), slot_rect)
            pygame.draw.rect(overlay, (200,200,200), slot_rect, 1)
            if slot:
                img = self._get_item_image(slot.get('item'))
                if img:
                    overlay.blit(img, (rx + 5, ry + 5))
                qty = slot.get('quantity', 0)
                qty_surf = self.font.render(str(qty), True, (255,255,255))
                overlay.blit(qty_surf, qty_surf.get_rect(bottomright=(rx + self.slot_size - 5, ry + self.slot_size - 5)))
        # Botones de guardar
        btn_x = grid_origin_x
        btn_y = grid_origin_y + ((len(slots) // cols) + 1) * (self.slot_size + self.margin) + self.margin
        self.save_default_rect = pygame.Rect(btn_x, btn_y, *self.button_size)
        pygame.draw.rect(overlay, (100,100,100), self.save_default_rect)
        pygame.draw.rect(overlay, (255,255,255), self.save_default_rect, 2)
        txt_def = self.font.render("Save Default", True, (255,255,255))
        overlay.blit(txt_def, (btn_x + 10, btn_y + 5))
        self.save_active_rect = pygame.Rect(btn_x + self.button_size[0] + 10, btn_y, *self.button_size)
        pygame.draw.rect(overlay, (100,100,100), self.save_active_rect)
        pygame.draw.rect(overlay, (255,255,255), self.save_active_rect, 2)
        txt_act = self.font.render("Save Active", True, (255,255,255))
        overlay.blit(txt_act, (btn_x + self.button_size[0] + 20, btn_y + 5))
