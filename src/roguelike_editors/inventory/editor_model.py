from dataclasses import dataclass, field
from typing import List, Optional, Dict, Any
from roguelike_editors.inventory.right_panel.inventory_items_panel.inventory_items_panel_model import InventoryitemsPanelModel
from roguelike_editors.inventory.left_panel.panel_model import InventoryPanelModel
from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_model import ItemSelectionPanelModel
import logging
logger = logging.getLogger(__name__)

@dataclass
class InventoryEditorModel:
    """
    Model for the Inventory Editor MVC.
    """
    visible: bool = False
    # When True, inventory overlay is visually hidden but remains event-active (used for press-and-hold on Pos)
    overlay_hidden_while_hold: bool = False
    # Internal state: user is holding mouse on a 'Pos:' line to focus camera on a monster
    holding_pos_focus: bool = False
    # Categories and selection delegated to left_panel_model
    # categories and current_category are accessed via left_panel_model
    # JSON data: default templates and active inventories
    default_data: Dict[str, Any] = field(default_factory=dict)
    active_data: Dict[str, Any] = field(default_factory=dict)
    # Editing state delegated to items_panel_model.tabs.active_tab
    editing_property: Optional[str] = None
    editing_index: Optional[int] = None
    
    # Selected default monster template (by template_id) for editing in 'Show Default'
    selected_default_template_id: Optional[str] = None
    # Selected default player class for editing in 'Show Default'
    selected_default_player_class: Optional[str] = None
    
    # Live inventory drag/drop and selection
    entities: Optional[List[int]] = None
    
    drag_item: Optional[tuple] = None
    drag_slot: Optional[int] = None
    prev_left: bool = False
    prev_right: bool = False
    
    # Scroll offset for vertical scrolling of lists
    scroll_offset: int = 0
    
    # Panel models
    left_panel_model: InventoryPanelModel = field(default_factory=InventoryPanelModel)
    items_panel_model: InventoryitemsPanelModel = field(default_factory=InventoryitemsPanelModel)
    item_selection_panel_model: ItemSelectionPanelModel = field(default_factory=ItemSelectionPanelModel)


    @property
    def editing_side(self) -> str:
        return self.items_panel_model.tabs.active_tab

    @editing_side.setter
    def editing_side(self, value: str):
        self.items_panel_model.tabs.active_tab = value
        # Clear template selection when leaving default side
        if value != 'default':
            self.selected_default_template_id = None
            self.selected_default_player_class = None

    @property
    def grid_model(self) -> InventoryitemsPanelModel:
        return self.items_panel_model

    @property
    def categories(self) -> List[str]:
        return self.left_panel_model.categories

    @categories.setter
    def categories(self, value: List[str]):
        self.left_panel_model.categories = value

    @property
    def current_category(self) -> str:
        return self.left_panel_model.current_category

    @current_category.setter
    def current_category(self, value: str):
        self.left_panel_model.current_category = value
        logger.debug(f"[DEBUG][Model] InventoryEditorModel.current_category set to {value}")
        # Clear template selection when leaving monsters/hostile category
        if value not in ('monsters', 'hostile'):
            self.selected_default_template_id = None
        # Clear player class selection when leaving player category
        if value != 'player':
            self.selected_default_player_class = None

    @property
    def selected_eid(self) -> Optional[int]:
        return self.left_panel_model.selected_eid

    @selected_eid.setter
    def selected_eid(self, value: Optional[int]):
        self.left_panel_model.selected_eid = value
        logger.debug(f"[DEBUG][Model] InventoryEditorModel.selected_eid set to {value}")

    @property
    def camera_focus_target(self) -> Optional[Any]:
        return self.left_panel_model.camera_focus_target

    @camera_focus_target.setter
    def camera_focus_target(self, value: Optional[Any]):
        self.left_panel_model.camera_focus_target = value
