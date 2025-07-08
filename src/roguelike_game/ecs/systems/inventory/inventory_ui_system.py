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
        # Drag panel state
        self.dragging = False
        self.drag_offset_x = 0
        self.drag_offset_y = 0
        self.drag_start_mouse_x = 0
        self.drag_start_mouse_y = 0
        self.drag_start_offset_x = 0
        self.drag_start_offset_y = 0
        self.prev_right_pressed = False
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


        # Toggle inventory visibility
        player_eid = getattr(world, 'player_entity', None)
        if player_eid is None:
            return
        inp = world.components.get('InputComponent', {}).get(player_eid)
        if inp and getattr(inp, 'toggle_inventory', False):
            self.visible = not self.visible
            inp.toggle_inventory = False
            return
        if not self.visible:
            return

        # Get inventory component
        inv = world.components.get('InventoryComponent', {}).get(player_eid)
        if not inv:
            return
        slots = inv.slots

        # Panel dimensions
        cols = 5
        rows = math.ceil(len(slots) / cols)
        padding = 10
        slot_w, slot_h = 64, 64
        screen_w, screen_h = screen.get_size()
        panel_w = cols * slot_w + (cols + 1) * padding
        panel_h = rows * slot_h + (rows + 1) * padding
        center_x = (screen_w - panel_w) // 2
        center_y = (screen_h - panel_h) // 2

        # Handle right-click dragging
        mouse_buttons = pygame.mouse.get_pressed()
        mouse_x, mouse_y = pygame.mouse.get_pos()
        right_pressed = mouse_buttons[2]
        # Start drag
        if right_pressed and not self.prev_right_pressed and self.panel_rect and self.panel_rect.collidepoint(mouse_x, mouse_y):
            self.dragging = True
            print(f"[DEBUG] [InventoryUISystem] drag started at pos=({mouse_x},{mouse_y}), starting_offset=({self.drag_offset_x},{self.drag_offset_y})")
            self.drag_start_mouse_x = mouse_x
            self.drag_start_mouse_y = mouse_y
            self.drag_start_offset_x = self.drag_offset_x
            self.drag_start_offset_y = self.drag_offset_y
        # End drag
        if not right_pressed and self.prev_right_pressed and self.dragging:
            self.dragging = False
        # Update offset if dragging
        if self.dragging:

            self.drag_offset_x = self.drag_start_offset_x + (mouse_x - self.drag_start_mouse_x)
            self.drag_offset_y = self.drag_start_offset_y + (mouse_y - self.drag_start_mouse_y)
        self.prev_right_pressed = right_pressed

        # Compute panel position
        panel_x = center_x + self.drag_offset_x
        panel_y = center_y + self.drag_offset_y
        panel_rect = pygame.Rect(panel_x, panel_y, panel_w, panel_h)
        self.panel_rect = panel_rect

        # Draw panel background and border
        pygame.draw.rect(screen, (50, 50, 50), panel_rect)
        pygame.draw.rect(screen, (200, 200, 200), panel_rect, 2)
        # Close button
        close_size = 20
        close_x = panel_x + panel_w - close_size - padding
        close_y = panel_y + padding
        close_rect = pygame.Rect(close_x, close_y, close_size, close_size)
        pygame.draw.rect(screen, (200, 50, 50), close_rect)
        x_text = self.font.render('X', True, (255, 255, 255))
        x_text_rect = x_text.get_rect(center=close_rect.center)
        screen.blit(x_text, x_text_rect)
        # Handle close click
        if pygame.mouse.get_pressed()[0] and close_rect.collidepoint(pygame.mouse.get_pos()):
            self.visible = False

        # Render slots
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
                # Quantity
                text = self.font.render(str(stack.quantity), True, (255, 255, 255))
                text_rect = text.get_rect(bottomright=(x + slot_w - 5, y + slot_h - 5))
                screen.blit(text, text_rect)

        if player_eid is None:
            return
        inp = world.components.get('InputComponent', {}).get(player_eid)
        if inp and getattr(inp, 'toggle_inventory', False):
            self.visible = not self.visible
            inp.toggle_inventory = False
            return
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
        # Posición central con offset
        center_x = (screen_w - panel_w) // 2
        center_y = (screen_h - panel_h) // 2
        panel_x = center_x + self.drag_offset_x
        panel_y = center_y + self.drag_offset_y
        panel_rect = pygame.Rect(panel_x, panel_y, panel_w, panel_h)
        # Exponer panel_rect para sistemas de drag
        self.panel_rect = panel_rect
        # Manejo de drag con click derecho
        mouse_buttons = pygame.mouse.get_pressed()
        mouse_x, mouse_y = pygame.mouse.get_pos()
        right_pressed = mouse_buttons[2]
        # Inicio de drag
        if right_pressed and not self.prev_right_pressed and panel_rect.collidepoint(mouse_x, mouse_y):
            self.dragging = True
            self.drag_start_mouse_x = mouse_x
            self.drag_start_mouse_y = mouse_y
            self.drag_start_offset_x = self.drag_offset_x
            self.drag_start_offset_y = self.drag_offset_y
        # Fin de drag
        if not right_pressed and self.prev_right_pressed and self.dragging:
            self.dragging = False
        # Actualizar offset si drag activo
        if self.dragging:

            self.drag_offset_x = self.drag_start_offset_x + (mouse_x - self.drag_start_mouse_x)
            self.drag_offset_y = self.drag_start_offset_y + (mouse_y - self.drag_start_mouse_y)
        self.prev_right_pressed = right_pressed
        # Dibujar fondo y borde
        pygame.draw.rect(screen, (50, 50, 50), panel_rect)
        pygame.draw.rect(screen, (200, 200, 200), panel_rect, 2)
        # Close button
        close_size = 20
        close_x = panel_x + panel_w - close_size - padding
        close_y = panel_y + padding
        close_rect = pygame.Rect(close_x, close_y, close_size, close_size)
        pygame.draw.rect(screen, (200, 50, 50), close_rect)
        x_text = self.font.render('X', True, (255, 255, 255))
        x_text_rect = x_text.get_rect(center=close_rect.center)
        screen.blit(x_text, x_text_rect)
        # Handle close click
        if pygame.mouse.get_pressed()[0] and close_rect.collidepoint(pygame.mouse.get_pos()):
            self.visible = False
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
        # Right-click panel dragging
        mouse_buttons = pygame.mouse.get_pressed()
        mouse_x, mouse_y = pygame.mouse.get_pos()
        right_pressed = mouse_buttons[2]
        if right_pressed and not self.prev_right_pressed:
            if self.panel_rect and self.panel_rect.collidepoint(mouse_x, mouse_y):
                self.dragging = True
                self.drag_start_mouse_x = mouse_x
                self.drag_start_mouse_y = mouse_y
                self.drag_start_offset_x = self.drag_offset_x
                self.drag_start_offset_y = self.drag_offset_y
        if not right_pressed and self.prev_right_pressed and self.dragging:
            self.dragging = False
        if self.dragging:

            self.drag_offset_x = self.drag_start_offset_x + (mouse_x - self.drag_start_mouse_x)
            self.drag_offset_y = self.drag_start_offset_y + (mouse_y - self.drag_start_mouse_y)
        self.prev_right_pressed = right_pressed
        # Detectar toggle de inventario en el jugador
        player_eid = getattr(world, 'player_entity', None)
        if player_eid is None:
            return
        inp = world.components.get('InputComponent', {}).get(player_eid)
        if inp and getattr(inp, 'toggle_inventory', False):
            self.visible = not self.visible
            inp.toggle_inventory = False
            return
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
        center_x = (screen_w - panel_w) // 2
        center_y = (screen_h - panel_h) // 2
        panel_x = center_x + self.drag_offset_x
        panel_y = center_y + self.drag_offset_y
        panel_rect = pygame.Rect(panel_x, panel_y, panel_w, panel_h)
        # Expose panel_rect for drag systems
        self.panel_rect = panel_rect
        # Fondo y borde
        pygame.draw.rect(screen, (50, 50, 50), panel_rect)
        pygame.draw.rect(screen, (200, 200, 200), panel_rect, 2)
        # Close button
        close_size = 20
        close_x = panel_x + panel_w - close_size - padding
        close_y = panel_y + padding
        close_rect = pygame.Rect(close_x, close_y, close_size, close_size)
        pygame.draw.rect(screen, (200, 50, 50), close_rect)
        x_text = self.font.render('X', True, (255, 255, 255))
        x_text_rect = x_text.get_rect(center=close_rect.center)
        screen.blit(x_text, x_text_rect)
        # Handle close click
        if pygame.mouse.get_pressed()[0] and close_rect.collidepoint(pygame.mouse.get_pos()):
            self.visible = False
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
