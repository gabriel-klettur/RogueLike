import pygame
from roguelike_editors.entities.entities_assets_picker_panel.entities_assets_picker_panel_model import EntitiesAssetsPickerPanelModel
from roguelike_editors.entities.entities_assets_picker_panel.entities_assets_picker_panel_view import EntitiesAssetsPickerPanelView
from roguelike_editors.entities.entities_assets_picker_panel.entities_assets_picker_panel_events import EntitiesAssetsPickerPanelEventHandler


class EntitiesAssetsPickerPanelController:
    """Controller para el panel de selección de assets de entidades."""
    def __init__(self):
        self.model = EntitiesAssetsPickerPanelModel()
        self.view = EntitiesAssetsPickerPanelView(self.model)
        self.event_handler = EntitiesAssetsPickerPanelEventHandler(self)

    def show(self, key: str, x: int, y: int, width: int, callback, label_provider=None):
        # Clear previous errors
        self.model.error_message = None
        self.model.error_timestamp = 0.0
        """
        Muestra el picker bajo la celda de assets.
        """
        self.model.key = key
        self.model.pos = (x, y)
        self.model.width = width
        self.model.on_asset_chosen = callback
        self.model.label_provider = label_provider
        self.model.visible = True
        # Reiniciar FS model
        # Reset to model's root Path
        self.model.fs_model.current_dir = self.model.fs_model.root_dir
        self.model.fs_model.scroll_offset = 0
        self.model.fs_model.load_entries()

    def hide(self):
        # Clear errors on hide
        self.model.error_message = None
        self.model.error_timestamp = 0.0
        """Oculta el picker."""
        self.model.label_provider = None
        self.model.visible = False

    def draw(self, screen: pygame.Surface):
        """Dibuja el picker si está visible."""
        self.view.draw(screen)

    def handle_event(self, event: pygame.event.Event) -> bool:
        """Delegar eventos al handler si está visible."""
        if not self.model.visible:
            return False
        return self.event_handler.handle(event)