import pygame
from roguelike_ui.widgets.file_system_picker import FileSystemPickerView


class EntitiesAssetsPickerPanelView:
    """
    View for the entities assets picker panel.
    """
    def __init__(self, model):
        self.model = model
        self.fs_view = FileSystemPickerView(model.fs_model)
        self.entry_rects = []

    def draw(self, surface: pygame.Surface):
        if not self.model.visible:
            return
        x, y = self.model.pos
        # enforce width in number of columns
        # calculate cols so that width fits
        thumb, pad = self.fs_view.thumb_size, self.fs_view.pad
        self.fs_view.cols = max(1, (self.model.width - pad) // (thumb + pad))
        # draw file system picker
        hovered = self.fs_view.draw(surface, (x, y))
        # capture entry rects for interaction
        self.entry_rects = self.fs_view.entry_rects
        # store panel rectangle for nested pickers
        surf = self.fs_view.panel.surface
        w, h = surf.get_size()
        # Hide internal path label by overlaying bottom strip inside FS panel
        hide_h = 24
        overlay_rect = pygame.Rect(x, y + h - hide_h, w, hide_h)
        overlay_surf = pygame.Surface((w, hide_h), pygame.SRCALPHA)
        overlay_surf.fill((0, 0, 0, 200))
        surface.blit(overlay_surf, (overlay_rect.x, overlay_rect.y))
        # Footer area below FS panel for entity name (dynamic height)
        footer_font = pygame.font.SysFont(None, 22)
        footer_h = footer_font.get_height() + 10
        footer_rect = pygame.Rect(x, y + h, w, footer_h)
        footer_bg = pygame.Surface((w, footer_h), pygame.SRCALPHA)
        footer_bg.fill((0, 0, 0, 220))
        surface.blit(footer_bg, footer_rect.topleft)
        # Bottom label text from provider
        label_text = ""
        if self.model.label_provider:
            try:
                label_text = self.model.label_provider() or ""
            except Exception:
                label_text = ""
        if label_text:
            # Pretty formatting: replace underscores with spaces and Title-case
            pretty = label_text.replace("_", " ").title()
            text_surf = footer_font.render(pretty, True, (255, 230, 0))
            tx = x + (w - text_surf.get_width()) // 2
            ty = y + h + (footer_h - text_surf.get_height()) // 2
            surface.blit(text_surf, (tx, ty))
        # Update panel rect to include footer
        self.model.panel_rect = pygame.Rect(x, y, w, h + footer_h)
        # Draw error message if present
        if self.model.error_message:
            # Render error label in bottom-right
            font = pygame.font.SysFont(None, 20)
            text_surf = font.render(self.model.error_message, True, (255, 0, 0))
            err_rect = text_surf.get_rect()
            err_x = self.model.panel_rect.right - err_rect.width - 5
            err_y = self.model.panel_rect.bottom - err_rect.height - 5
            surface.blit(text_surf, (err_x, err_y))

