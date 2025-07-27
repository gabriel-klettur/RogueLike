import pygame


def draw_highlight_rect(screen, rect, color=(255, 255, 0), width=2):
    """
    Draw a rectangular highlight border on the screen.

    Args:
        screen: pygame.Surface to draw on.
        rect: pygame.Rect defining the area to highlight.
        color: RGB tuple for the border color.
        width: thickness of the border in pixels.
    """
    pygame.draw.rect(screen, color, rect, width=width)


def draw_tooltip(screen, x, y, lines, font=None, padd=4,
                 bg_color=(0, 0, 0, 200), border_color=(255, 255, 0),
                 text_color=(255, 255, 255)):
    """
    Render a tooltip near the given (x, y) position with the provided text lines.

    Args:
        screen: pygame.Surface to draw on.
        x, y: anchor coordinates (usually mouse position).
        lines: list of strings to display inside the tooltip.
        font: pygame.font.Font for rendering text. If None, a default font is used.
        padd: padding in pixels around text.
        bg_color: RGBA tuple for tooltip background.
        border_color: RGB tuple for border color.
        text_color: RGB tuple for text color.
    """
    if font is None:
        font = pygame.font.SysFont(None, 20)

    # Render text surfaces
    text_surfs = [font.render(line, True, text_color) for line in lines]
    width = max(s.get_width() for s in text_surfs)
    height = sum(s.get_height() for s in text_surfs)

    box_w = width + padd * 2
    box_h = height + padd * 2
    box_x = x + 10
    box_y = y + 10
    screen_w, screen_h = screen.get_size()

    # Adjust if goes off-screen
    if box_x + box_w > screen_w:
        box_x = x - box_w - 10
    if box_y + box_h > screen_h:
        box_y = y - box_h - 10

    # Draw background
    bg_surf = pygame.Surface((box_w, box_h), flags=pygame.SRCALPHA)
    bg_surf.fill(bg_color)
    screen.blit(bg_surf, (box_x, box_y))

    # Draw border
    pygame.draw.rect(screen, border_color, (box_x, box_y, box_w, box_h), width=1)

    # Blit text
    y_offset = box_y + padd
    for surf in text_surfs:
        screen.blit(surf, (box_x + padd, y_offset))
        y_offset += surf.get_height()
