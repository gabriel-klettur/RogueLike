import pygame
from roguelike_editors.entities.entities_properties_panel.entities_assets_grid_panel.entities_assets_grid_panel_model import AssetsGridPanelModel
from roguelike_editors.entities.entities_properties_panel.entities_assets_grid_panel.entities_assets_grid_panel_view import AssetsGridPanelView
from roguelike_editors.entities.entities_properties_panel.entities_assets_grid_panel.entities_assets_grid_panel_events import AssetsGridPanelEventHandler

class AssetsGridPanelController:
    """Controller para el panel de cuadrícula de assets en el panel de propiedades."""
    def __init__(self, parent_controller, font: pygame.font.Font):
        # parent_controller es EntityPropertiesPanelController
        self.parent_controller = parent_controller
        self.parent_model = parent_controller.model
        self.model = AssetsGridPanelModel()
        self.view = AssetsGridPanelView(font)
        # Referencia al modelo principal para state tabs
        self.view.parent_model = self.parent_model
        self.event_handler = AssetsGridPanelEventHandler(self)

    def draw(self, screen: pygame.Surface, entity_data: dict, px: int, py: int, pad: int, font_h: int, panel_w: int) -> None:
        """Dibuja subtabs y grid de assets usando model y view."""
        # Initialize or update animators when entity or state tab changes
        ent_id = self.parent_model.selected_id
        state = self.parent_controller.state_tabs_controller.model.active_state_tab
        if ent_id and (ent_id != self.model.last_entity_id or state != self.model.last_state_tab):
            # Rebuild animators
            from roguelike_game.factories.player.loader import load_and_scale_sprites
            # Load all sprites scaled
            sprites_dict = load_and_scale_sprites(ent_id)
            # Map UI state to internal state
            state_map = {'chase': 'walk'}
            internal_state = state_map.get(state, state)
            # Direction mapping
            dir_map = {
                'nw': 'up_left', 'n': 'up', 'ne': 'up_right',
                'w': 'left', 'e': 'right', 'sw': 'down_left',
                's': 'down', 'se': 'down_right'
            }
            # Create animators per asset_key
            self.model.animators.clear()
            from roguelike_game.ecs.components.rendering.animator import Animator
            for grid_dir, sprite_dir in dir_map.items():
                frames = sprites_dict.get(sprite_dir, {}).get(internal_state, [])
                if frames:
                    asset_key = f"asset_{state}_{grid_dir}"
                    # animations dict for Animator requires list key same as state
                    anim = Animator(animations={internal_state: frames}, current_state=internal_state)
                    self.model.animators[asset_key] = anim
            # track last
            self.model.last_entity_id = ent_id
            self.model.last_state_tab = state
        # Delegate actual drawing
        self.view.draw(screen, self.model, entity_data, px, py, pad, font_h, panel_w)
        
    def handle_event(self, event: pygame.event.Event) -> bool:
        """Delegación de eventos relacionados al grid."""
        return self.event_handler.handle(event)
