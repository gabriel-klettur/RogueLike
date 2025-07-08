import os
import math
import pygame
from roguelike_game.ecs.components.item_models import load_items

class InventoryUISystem:
    """
    Sistema de UI para mostrar el inventario del jugador en pantalla.
    """
    def __init__(self, perf_log=None, items_path=None):
        self.perf_log = perf_log
        if items_path is None:
            items_path = os.path.join(os.getcwd(), 'data', 'items.json')
        self.items = load_items(items_path)
        self.visible = False
        self.panel_rect = None
        pygame.font.init()
        self.font = pygame.font.SysFont(None, 24)
        self.icon_surfaces = {}
        # Pre-cargar superficies de íconos
        for item_id, model in self.items.items():
            icon = getattr(model, 'icon_small', None) or getattr(model, 'icon', None)
            if isinstance(icon, list):
                icon = icon[0]
            if icon:
                path = os.path.join(os.getcwd(), icon)
                try:
                    surf = pygame.image.load(path).convert_alpha()
                except Exception:
                    surf = None
                self.icon_surfaces[item_id] = surf

    def update(self, world, screen, camera):
        # Reset panel_rect each frame
        self.panel_rect = None
        # Detectar toggle de inventario en el jugador
        player_eid = getattr(world, 'player_entity', None)
        if player_eid is None:
            return
        inp = world.components.get('InputComponent', {}).get(player_eid)
        if inp and getattr(inp, 'toggle_inventory', False):
            self.visible = not self.visible
        if not self.visible:
            return
        # Obtener inventario del jugador
        inv = world.components.get('InventoryComponent', {}).get(player_eid)
        if not inv:
            return
        slots = inv.slots
        cols = 5
        rows = math.ceil(len(slots) / cols)
        padding = 10
        slot_w, slot_h = 64, 64
        screen_w, screen_h = screen.get_size()
        panel_w = cols * slot_w + (cols + 1) * padding
        panel_h = rows * slot_h + (rows + 1) * padding
        panel_x = (screen_w - panel_w) // 2
        panel_y = (screen_h - panel_h) // 2
        panel_rect = pygame.Rect(panel_x, panel_y, panel_w, panel_h)
        # Expose panel_rect for drag systems
        self.panel_rect = panel_rect
        # Fondo y borde
        pygame.draw.rect(screen, (50, 50, 50), panel_rect)
        pygame.draw.rect(screen, (200, 200, 200), panel_rect, 2)
        # Renderizar cada slot
        for idx, stack in enumerate(slots):
            col = idx % cols
            row = idx // cols
            x = panel_x + padding + col * (slot_w + padding)
            y = panel_y + padding + row * (slot_h + padding)
            slot_rect = pygame.Rect(x, y, slot_w, slot_h)
            pygame.draw.rect(screen, (80, 80, 80), slot_rect)
            pygame.draw.rect(screen, (150, 150, 150), slot_rect, 1)
            if stack:
                surf = self.icon_surfaces.get(stack.item_id)
                if surf:
                    img = pygame.transform.scale(surf, (slot_w - 10, slot_h - 10))
                    screen.blit(img, (x + 5, y + 5))
                # Cantidad
                text = self.font.render(str(stack.quantity), True, (255, 255, 255))
                text_rect = text.get_rect(bottomright=(x + slot_w - 5, y + slot_h - 5))
                screen.blit(text, text_rect)
