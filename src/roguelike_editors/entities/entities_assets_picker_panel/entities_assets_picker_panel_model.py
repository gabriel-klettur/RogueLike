import pygame
from roguelike_ui.widgets.file_system_picker import FileSystemPickerModel
from roguelike_engine.config.config import ASSETS_DIR
from typing import Optional


class EntitiesAssetsPickerPanelModel:
    """
    Model for the assets picker panel under the entities picker panel.
    """
    def __init__(self, root_dir: str = None):
        # Directory to browse
        self.root_dir = root_dir or ASSETS_DIR
        self.fs_model = FileSystemPickerModel(self.root_dir)
        # Panel visibility and position/size
        self.visible = False
        self.pos = (0, 0)
        self.width = 0
        # Asset cell key this picker is for (e.g. 'asset_idle_n')
        self.key = None
        # Callback when asset chosen: function(cell_key, path)
        self.on_asset_chosen = None
        # rectangle of the panel for positioning nested pickers
        self.panel_rect = None
        # Error message and timestamp for invalid selections
        self.error_message: Optional[str] = None
        self.error_timestamp: float = 0.0
