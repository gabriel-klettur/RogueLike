import pygame
import os
from roguelike_game.config.spells_config import SPELLS
from roguelike_ui.services.json_persistence import load_from_json
from roguelike_engine.utils.loader import load_image
from roguelike_editors.spells.spells_editor_controller import SpellsEditorController

import logging
logger = logging.getLogger(__name__)

class SpellsEditorManager:
    """
    Manager for the Spell Editor: loads spell data and assets, delegates to SpellEditorController
    """
    def __init__(self, game):
        self.game = game
        # Load spells data (allow persistent changes)
        path = os.path.join(os.getcwd(), "data", "spells", "spells.json")
        root = load_from_json(path)
        # Merge root with default SPELLS to include any new defaults
        spells = SPELLS.copy()
        spells.update(root)
        # Load assets for spells
        assets = {}
        for sid, sdef in spells.items():
            sprite_path = sdef.get("sprite")
            if sprite_path:
                try:
                    assets[sid] = load_image(sprite_path)
                except Exception as e:
                    logger.error(f"Error loading sprite for spell {sid}: {e}")
        font = game.font
        # Use the new top-level SpellsEditorController which orchestrates all subpanels
        self.controller = SpellsEditorController(spells, assets, font)
        self.model = self.controller.model
        # Expose state globally
        game.state.spell_editor_state = self.model

    def handle_event(self, event: pygame.event.Event) -> None:
        self.controller.handle_event(event)

    def draw(self, screen: pygame.Surface) -> None:
        self.controller.draw(screen)
