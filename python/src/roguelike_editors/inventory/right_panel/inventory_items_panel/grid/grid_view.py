import pygame

class GridView:
    """
    Vista para renderizar la cuadrícula de slots de inventario.
    """
    def __init__(self, font, slot_size, margin, get_item_image_func, logger):
        self.font = font
        self.slot_size = slot_size
        self.margin = margin
        self.get_item_image = get_item_image_func
        self.logger = logger

    def draw_slots(self, overlay, slots, grid_origin_x, grid_origin_y, mx, my, delete_mode_active=False):
        """Dibuja todos los slots del inventario"""
        cols = 5
        for idx, slot in enumerate(slots):
            col = idx % cols
            row = idx // cols
            rx = grid_origin_x + col * (self.slot_size + self.margin)
            ry = grid_origin_y + row * (self.slot_size + self.margin)
            slot_rect = pygame.Rect(rx, ry, self.slot_size, self.slot_size)
            
            # Background
            pygame.draw.rect(overlay, (80, 80, 80), slot_rect)
            
            # Hover highlights
            if delete_mode_active and slot_rect.collidepoint(mx, my) and slot:
                # Delete mode hover - red highlight
                highlight = pygame.Surface((self.slot_size, self.slot_size), pygame.SRCALPHA)
                highlight.fill((255, 0, 0, 100))
                overlay.blit(highlight, (rx, ry))
                pygame.draw.rect(overlay, (255, 0, 0), slot_rect, 2)
            elif slot_rect.collidepoint(mx, my):
                # Normal hover - yellow highlight
                pygame.draw.rect(overlay, (255, 255, 0), slot_rect, 2)
            else:
                # Default border
                pygame.draw.rect(overlay, (200, 200, 200), slot_rect, 1)
            
            # Draw item and quantity
            if slot:
                self._draw_slot_content(overlay, slot, rx, ry)

    def _draw_slot_content(self, overlay, slot, rx, ry):
        """Dibuja el contenido de un slot (ítem y cantidad)"""
        # Draw item image
        try:
            img = self.get_item_image(slot.get('item'))
            
            if img:
                overlay.blit(img, (rx + 5, ry + 5))
        except Exception as e:
            self.logger.error(f"Error dibujando imagen de ítem: {e}")
        
        # Draw quantity
        qty = slot.get('quantity', 0)
        
        qty_surf = self.font.render(str(qty), True, (255, 255, 255))
        overlay.blit(qty_surf, qty_surf.get_rect(
            bottomright=(rx + self.slot_size - 5, ry + self.slot_size - 5)
        ))

    def get_slot_index(self, pos, grid_origin_x, grid_origin_y, count):
        """Retorna el índice de slot bajo la posición pos, o None"""
        for i in range(count):
            col = i % 5
            row = i // 5
            rx = grid_origin_x + col * (self.slot_size + self.margin)
            ry = grid_origin_y + row * (self.slot_size + self.margin)
            rect = pygame.Rect(rx, ry, self.slot_size, self.slot_size)
            if rect.collidepoint(pos):
                return i
        return None
