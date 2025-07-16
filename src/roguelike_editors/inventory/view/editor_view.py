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

    def draw(self, screen, model: InventoryEditorModel, world):
        if not model.visible:
            return
        ow, oh = screen.get_size()
        # Overlay
        overlay = pygame.Surface((ow, oh), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 180))
        # Título
        title = f"Inv Editor - Eid {model.selected_eid}"
        text = self.font.render(title, True, (255,255,255))
        overlay.blit(text, (10,10))
        # Tabs
        self.tab_rects = []
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
            overlay.blit(txt, (tab_x + padding, tab_y + (rect.height - h)//2))
            self.tab_rects.append((rect, cat))
            tab_x += rect.width + 5
        # Instrucciones de navegación por teclado
        instr = "Press 1:Player 2:Monsters 3:Map"
        instr_surf = self.font.render(instr, True, (200,200,200))
        overlay.blit(instr_surf, (10, tab_y + 30))

        # Grid de inventario
        inv_data = model.active_data.get(model.current_category, {})
        inv_entry = inv_data.get(str(model.selected_eid), {})
        slots = inv_entry.get('slots', [])
        cols = 5
        for idx, slot in enumerate(slots):
            col = idx % cols
            row = idx // cols
            rx = self.grid_origin[0] + col * (self.slot_size + self.margin)
            ry = self.grid_origin[1] + row * (self.slot_size + self.margin)
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
        btn_x = self.grid_origin[0]
        btn_y = self.grid_origin[1] + ((len(slots) // cols) + 1) * (self.slot_size + self.margin) + 10
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
        # Dibuja ítem en arrastre
        if model.drag_item:
            mx, my = pygame.mouse.get_pos()
            img = self._get_item_image(model.drag_item[0])
            if img:
                overlay.blit(img, (mx - img.get_width()//2, my - img.get_height()//2))
        screen.blit(overlay, (0,0))
        return        

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
