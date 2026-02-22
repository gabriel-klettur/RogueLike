import pygame

def draw_hover(surface, rect, color=(255,255,0,100)):
    """Draw hover overlay on rect."""
    hover_surf = pygame.Surface((rect.width, rect.height), pygame.SRCALPHA)
    hover_surf.fill(color)
    surface.blit(hover_surf, rect.topleft)

def draw_selection_border(surface, rect, color, thickness=3):
    """Draw border around rect."""
    pygame.draw.rect(surface, color, rect, thickness)
