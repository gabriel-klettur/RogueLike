from dataclasses import dataclass, field
from typing import Dict, List
import pygame

from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_model import EntityPropertiesPanelModel

@dataclass
class EntitiesTypeAssetsModel:
    """Model for main 'properties'/'assets' tabs in EntityPropertiesPanel."""
    parent_model: EntityPropertiesPanelModel
    # Available main tabs
    type_tabs: List[str] = field(default_factory=lambda: ['properties', 'assets'])
    # Currently selected main tab
    active_type_tab: str = 'properties'
    # Hitboxes for main tabs
    type_tab_rects: Dict[str, pygame.Rect] = field(default_factory=dict)
