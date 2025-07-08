import pygame
import os
from roguelike_editors.inventory.model.editor_model import InventoryEditorModel
from roguelike_game.ecs.components.item_models import load_items

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
        items_path = os.path.join(cwd, 'data', 'items.json')
        self.items = load_items(items_path)
        self.images = {}

    def draw(self, screen, model: InventoryEditorModel, world):
        if not model.visible or model.selected_eid is None:
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
        # Grid
        inv = world.components.get('InventoryComponent', {}).get(model.selected_eid)
        slots = inv.slots if inv else []
        origin_x, origin_y = self.grid_origin
        cols = min(len(slots), 10)
        for idx, stack in enumerate(slots):
            col = idx % cols
            row = idx // cols
            x = origin_x + col * (self.slot_size + self.margin)
            y = origin_y + row * (self.slot_size + self.margin) + 30
            rect = pygame.Rect(x, y, self.slot_size, self.slot_size)
            pygame.draw.rect(overlay, (100,100,100), rect)
            pygame.draw.rect(overlay, (255,255,255), rect, 2)
            if stack:
                item_id, qty = stack.item_id, stack.quantity
                img = self._get_item_image(item_id)
                if img:
                    iw, ih = img.get_size()
                    overlay.blit(img, (x + (self.slot_size - iw)//2, y + (self.slot_size - ih)//2))
                qty_text = self.font.render(str(qty), True, (255,255,0))
                overlay.blit(qty_text, (x + self.slot_size - qty_text.get_width() - 2, y + self.slot_size - qty_text.get_height() - 2))
        # Botones centrados bajo la grilla
        rows = (len(slots) + cols - 1) // cols if slots else 0
        grid_height = rows * (self.slot_size + self.margin)
        bx = origin_x
        by = origin_y + 30 + grid_height + 20
        # Botón guardar plantilla
        self.save_default_rect = pygame.Rect(bx, by, *self.button_size)
        pygame.draw.rect(overlay, (50,150,50), self.save_default_rect)
        save_txt = self.font.render("Guardar plantilla", True, (255,255,255))
        overlay.blit(save_txt, (bx + (self.button_size[0] - save_txt.get_width())//2, by + 5))
        # Botón aplicar cambios
        bx2 = bx + self.button_size[0] + 20
        self.save_active_rect = pygame.Rect(bx2, by, *self.button_size)
        pygame.draw.rect(overlay, (50,150,50), self.save_active_rect)
        apply_txt = self.font.render("Aplicar cambios", True, (255,255,255))
        overlay.blit(apply_txt, (bx2 + (self.button_size[0] - apply_txt.get_width())//2, by + 5))
        # Dragging
        if model.drag_item:
            mx, my = pygame.mouse.get_pos()
            item_id, qty = model.drag_item
            img = self._get_item_image(item_id)
            if img:
                iw, ih = img.get_size()
                overlay.blit(img, (mx - iw//2, my - ih//2))
        screen.blit(overlay, (0,0))

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
        except:
            return None
