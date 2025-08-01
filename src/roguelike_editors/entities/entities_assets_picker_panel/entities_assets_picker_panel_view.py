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
        self.model.panel_rect = pygame.Rect(x, y, w, h)
        # Draw error message if present
        if self.model.error_message:
            # Render error label in bottom-right
            font = pygame.font.SysFont(None, 20)
            text_surf = font.render(self.model.error_message, True, (255, 0, 0))
            err_rect = text_surf.get_rect()
            err_x = self.model.panel_rect.right - err_rect.width - 5
            err_y = self.model.panel_rect.bottom - err_rect.height - 5
            surface.blit(text_surf, (err_x, err_y))

