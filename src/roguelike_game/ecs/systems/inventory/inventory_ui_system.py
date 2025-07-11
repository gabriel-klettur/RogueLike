import os

import pygame
import logging
from roguelike_game.ecs.components.item_models import load_items

class InventoryUISystem:
    """
    Sistema de UI para mostrar el inventario del jugador en pantalla.
    """
    # Constantes de estilo y layout
    BGCOLOR = (50, 50, 50)
    BORDER_COLOR = (200, 200, 200)
    CLOSE_BUTTON_COLOR = (200, 50, 50)
    SLOT_BG_COLOR = (80, 80, 80)
    SLOT_BORDER_COLOR = (150, 150, 150)
    TEXT_COLOR = (255, 255, 255)
    GRID_COLS = 5
    GRID_ROWS = 5
    PADDING = 10
    SLOT_SIZE = 64
    CLOSE_BUTTON_SIZE = 20

    def __init__(self, perf_log=None, items_path=None):
        """
        Inicializa InventoryUISystem, carga modelos de ítems y prepara fuentes e íconos.
        """
        self.logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")
        self.perf_log = perf_log
        if items_path is None:
            items_path = os.path.join(os.getcwd(), 'data', 'items', 'items.json')
        self.items = load_items(items_path)
        self.visible = False
        self.panel_rect = None
        # Estado de drag
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
        # Pre-cargar superficies de íconos
        self.icon_surfaces = {}
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

    def _get_player_input(self, world):
        """Obtiene player_entity e InputComponent."""
        player_eid = getattr(world, 'player_entity', None)
        if player_eid is None:
            return None, None
        inp = world.components.get('InputComponent', {}).get(player_eid)
        return player_eid, inp

    def _handle_toggle(self, world):
        """
        Maneja apertura/cierre del inventario.
        Retorna True si la UI debe mostrarse.
        """
        player_eid, inp = self._get_player_input(world)
        if player_eid is None:
            return False
        if inp and getattr(inp, 'toggle_inventory', False):
            self.visible = not self.visible
            inp.toggle_inventory = False
            self.logger.debug("Inventory visibility toggled: %s", self.visible)
            return False
        return self.visible

    def _get_slots(self, world):
        """Retorna la lista de slots del jugador o None si no hay inventario."""
        player_eid, _ = self._get_player_input(world)
        inv = world.components.get('InventoryComponent', {}).get(player_eid)
        if inv is None:
            return None
        return inv.slots

    def _compute_panel_rect(self, screen, num_slots):
        """Calcula y retorna el Rect del panel basado en número de slots y offset de drag."""
        cols = self.GRID_COLS
        rows = self.GRID_ROWS
        padding = self.PADDING
        size = self.SLOT_SIZE
        panel_w = cols * size + (cols + 1) * padding
        panel_h = rows * size + (rows + 1) * padding
        screen_w, screen_h = screen.get_size()
        center_x = (screen_w - panel_w) // 2
        center_y = (screen_h - panel_h) // 2
        x = center_x + self.drag_offset_x
        y = center_y + self.drag_offset_y
        return pygame.Rect(x, y, panel_w, panel_h)

    def _handle_drag(self, panel_rect):
        """
        Maneja arrastre del panel con click derecho.
        Debe llamarse antes de dibujar el panel.
        """
        mouse_buttons = pygame.mouse.get_pressed()
        mouse_x, mouse_y = pygame.mouse.get_pos()
        right_pressed = mouse_buttons[2]
        if right_pressed and not self.prev_right_pressed and panel_rect.collidepoint(mouse_x, mouse_y):
            self.dragging = True
            self.logger.debug(
                "Drag started at pos=(%d,%d), offset=(%d,%d)",
                mouse_x, mouse_y, self.drag_offset_x, self.drag_offset_y,
            )
            self.drag_start_mouse_x = mouse_x
            self.drag_start_mouse_y = mouse_y
            self.drag_start_offset_x = self.drag_offset_x
            self.drag_start_offset_y = self.drag_offset_y
        elif not right_pressed and self.prev_right_pressed and self.dragging:
            self.dragging = False
            self.logger.debug("Drag ended")
        if self.dragging:
            dx = mouse_x - self.drag_start_mouse_x
            dy = mouse_y - self.drag_start_mouse_y
            self.drag_offset_x = self.drag_start_offset_x + dx
            self.drag_offset_y = self.drag_start_offset_y + dy
        self.prev_right_pressed = right_pressed

    def _draw_panel(self, screen, panel_rect):
        """Dibuja background, borde y botón de cierre, maneja click de cierre."""
        pygame.draw.rect(screen, self.BGCOLOR, panel_rect)
        pygame.draw.rect(screen, self.BORDER_COLOR, panel_rect, 2)
        size = self.CLOSE_BUTTON_SIZE
        padding = self.PADDING
        x = panel_rect.x + panel_rect.width - size - padding
        y = panel_rect.y + padding
        close_rect = pygame.Rect(x, y, size, size)
        pygame.draw.rect(screen, self.CLOSE_BUTTON_COLOR, close_rect)
        text_surf = self.font.render("X", True, self.TEXT_COLOR)
        text_rect = text_surf.get_rect(center=close_rect.center)
        screen.blit(text_surf, text_rect)
        if pygame.mouse.get_pressed()[0] and close_rect.collidepoint(pygame.mouse.get_pos()):
            self.visible = False
            self.logger.debug("Inventory closed via close button")

    def _draw_slots(self, screen, panel_rect, slots):
        """Dibuja los slots dentro del panel."""
        cols = self.GRID_COLS
        padding = self.PADDING
        size = self.SLOT_SIZE
        rows = self.GRID_ROWS
        total_slots = cols * rows
        for idx in range(total_slots):
            stack = slots[idx] if idx < len(slots) else None
            col = idx % cols
            row = idx // cols
            x = panel_rect.x + padding + col * (size + padding)
            y = panel_rect.y + padding + row * (size + padding)
            slot_rect = pygame.Rect(x, y, size, size)
            pygame.draw.rect(screen, self.SLOT_BG_COLOR, slot_rect)
            pygame.draw.rect(screen, self.SLOT_BORDER_COLOR, slot_rect, 1)
            if stack:
                surf = self.icon_surfaces.get(stack.item_id)
                if surf:
                    img = pygame.transform.scale(surf, (size - 10, size - 10))
                    screen.blit(img, (x + 5, y + 5))
                qty_surf = self.font.render(str(stack.quantity), True, self.TEXT_COLOR)
                qty_rect = qty_surf.get_rect(bottomright=(x + size - 5, y + size - 5))
                screen.blit(qty_surf, qty_rect)

    def update(self, world, screen, camera):
        """
        Update de UI de inventario: toggle, arrastre y render.
        """
        if not self._handle_toggle(world):
            return
        slots = self._get_slots(world)
        if not slots:
            return
        initial_rect = self._compute_panel_rect(screen, len(slots))
        self._handle_drag(initial_rect)
        panel_rect = self._compute_panel_rect(screen, len(slots))
        self.panel_rect = panel_rect
        self._draw_panel(screen, panel_rect)
        self._draw_slots(screen, panel_rect, slots)
