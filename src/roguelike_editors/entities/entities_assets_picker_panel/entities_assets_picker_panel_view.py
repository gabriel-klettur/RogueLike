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

