import pygame
from roguelike_editors.entities.services.constants import UI_MARGIN


class ParticlesPropertiesPanelView:
    """Simple properties panel view to display selected particle instance info."""

    def __init__(self, font: pygame.font.Font | None):
        self.font = font or pygame.font.SysFont("consolas", 16)
        self.title_font = self.font
        self.panel_rect: pygame.Rect | None = None

    def draw(self, screen: pygame.Surface, model) -> None:
        if not getattr(model, "visible", False):
            return
        # Base panel rect
        x = int(getattr(model, "x", 0))
        y = int(getattr(model, "y", 0))
        w = int(getattr(model, "width", 260))
        pad = int(getattr(model, "padding", 8))
        # Compute height based on rows
        rows = [
            ("ID", str(getattr(model, "selected_id", ""))),
            ("Preset", str((model.entry or {}).get("preset_id", ""))),
            ("Zone", str((model.entry or {}).get("zone", ""))),
            ("rel_x", str((model.entry or {}).get("rel_x", ""))),
            ("rel_y", str((model.entry or {}).get("rel_y", ""))),
        ]
        row_h = self.font.get_height() + 4
        title_h = self.title_font.get_height() + 6
        h = title_h + UI_MARGIN + len(rows) * row_h + pad * 2
        panel = pygame.Rect(x, y, w, h)
        self.panel_rect = panel
        # Background and border
        pygame.draw.rect(screen, (20, 20, 22), panel, border_radius=8)
        pygame.draw.rect(screen, (70, 70, 80), panel, width=1, border_radius=8)
        # Title
        title = self.title_font.render("PARTICLE PROPERTIES", True, (230, 230, 240))
        screen.blit(title, (panel.x + pad, panel.y + pad))
        # Divider
        try:
            pygame.draw.line(
                screen, (90, 90, 100),
                (panel.x + pad, panel.y + pad + title_h),
                (panel.right - pad, panel.y + pad + title_h),
                width=1,
            )
        except Exception:
            pass
        # Rows
        y_cursor = panel.y + pad + title_h + UI_MARGIN // 2
        key_col = panel.x + pad
        val_col = panel.x + pad + 80
        for key, val in rows:
            ksurf = self.font.render(f"{key}:", True, (200, 200, 210))
            vsurf = self.font.render(str(val), True, (230, 230, 230))
            screen.blit(ksurf, (key_col, y_cursor))
            screen.blit(vsurf, (val_col, y_cursor))
            y_cursor += row_h
