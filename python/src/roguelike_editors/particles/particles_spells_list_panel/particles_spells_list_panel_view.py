import pygame
from roguelike_editors.entities.services.constants import UI_MARGIN


class ParticlesSpellsListPanelView:
    """View for the spells-usage list panel (read-only with collapse toggle)."""

    def __init__(self, font: pygame.font.Font | None):
        self.font = font or pygame.font.SysFont("consolas", 16)
        self.title_font = self.font
        self.panel_rect: pygame.Rect | None = None
        self.toggle_rect: pygame.Rect | None = None

    def draw(self, screen: pygame.Surface, model) -> None:
        if not getattr(model, "visible", False):
            self.panel_rect = None
            self.toggle_rect = None
            return
        # Base panel rect
        x = int(getattr(model, "x", 0))
        y = int(getattr(model, "y", 0))
        w = int(getattr(model, "width", 260))
        pad = int(getattr(model, "padding", 8))

        usages = list(getattr(model, "usages", []) or [])
        expanded = bool(getattr(model, "expanded", True))

        # Heights
        row_h = self.font.get_height() + 4
        title_h = self.title_font.get_height() + 6
        header_h = self.font.get_height() + 4

        # Compute dynamic height
        h = pad + title_h + UI_MARGIN // 2 + header_h
        if expanded:
            h += len(usages) * row_h
        h += pad
        panel = pygame.Rect(x, y, w, max(h, title_h + pad * 2))
        self.panel_rect = panel

        # Background and border
        pygame.draw.rect(screen, (20, 20, 22), panel, border_radius=8)
        pygame.draw.rect(screen, (70, 70, 80), panel, width=1, border_radius=8)

        # Title
        title = self.title_font.render("SPELLS USING THIS PRESET", True, (230, 230, 240))
        screen.blit(title, (panel.x + pad, panel.y + pad))

        # Divider under title
        try:
            pygame.draw.line(
                screen, (90, 90, 100),
                (panel.x + pad, panel.y + pad + title_h),
                (panel.right - pad, panel.y + pad + title_h),
                width=1,
            )
        except Exception:
            pass

        # Cursor for content
        y_cursor = panel.y + pad + title_h + UI_MARGIN // 2

        # Header with toggle
        tri = "▼" if expanded else "▶"
        hdr = self.font.render(f"{tri} Spells", True, (210, 210, 220))
        screen.blit(hdr, (panel.x + pad, y_cursor))
        # Compute clickable area for toggle: small square before header text
        tri_surf = self.font.render(tri, True, (210, 210, 220))
        tri_w, tri_h = tri_surf.get_size()
        self.toggle_rect = pygame.Rect(panel.x + pad, y_cursor, max(16, tri_w), tri_h)
        y_cursor += header_h

        if not expanded:
            return

        # Rows: list usages
        key_col = panel.x + pad
        path_col = panel.x + pad + 120
        for (spell_key, path) in usages:
            try:
                ksurf = self.font.render(str(spell_key), True, (200, 200, 210))
                vsurf = self.font.render(str(path), True, (160, 190, 255))
                screen.blit(ksurf, (key_col, y_cursor))
                screen.blit(vsurf, (path_col, y_cursor))
                y_cursor += row_h
            except Exception:
                pass

