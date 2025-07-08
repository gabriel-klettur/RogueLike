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
        # Category-specific display
        data = model.active_data.get(model.current_category, {})
        y_offset = tab_y + 60
        line_height = self.font.get_linesize()
        if model.current_category == 'player':
            # Player items
            entries = data.values() if isinstance(data, dict) else data
            for entry in entries:
                slots = entry.get('slots', []) if isinstance(entry, dict) else []
                for slot in slots:
                    if slot:
                        item_id = slot.get('item')
                        qty = slot.get('quantity')
                        line = f"{item_id} x{qty}"
                        surf = self.font.render(line, True, (255,255,255))
                        overlay.blit(surf, (10, y_offset))
                        y_offset += line_height
        elif model.current_category == 'monsters':
            # Monster inventories
            entries = data.items() if isinstance(data, dict) else []
            for mon_id, entry in entries:
                # Monster label
                line = f"{mon_id}"
                surf = self.font.render(line, True, (200,200,255))
                overlay.blit(surf, (10, y_offset))
                y_offset += line_height
                slots = entry.get('slots', []) if isinstance(entry, dict) else []
                for slot in slots:
                    if slot:
                        item_id = slot.get('item')
                        qty = slot.get('quantity')
                        line = f"  {item_id} x{qty}"
                        surf = self.font.render(line, True, (255,255,255))
                        overlay.blit(surf, (20, y_offset))
                        y_offset += line_height
        elif model.current_category == 'map':
            # Floor items: JSON is a dict of id->entry
            entries = data.values() if isinstance(data, dict) else data
            for entry in entries:
                item_id = entry.get('item_id')
                qty = entry.get('quantity')
                pos = entry.get('position', {})
                x_coord = pos.get('x')
                y_coord = pos.get('y')
                line = f"{item_id} x{qty} @({x_coord:.1f},{y_coord:.1f})"
                surf = self.font.render(line, True, (255,255,255))
                overlay.blit(surf, (10, y_offset))
                y_offset += line_height

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
