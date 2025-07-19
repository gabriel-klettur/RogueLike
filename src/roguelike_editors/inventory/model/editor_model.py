from dataclasses import dataclass, field
from typing import List, Optional, Dict, Any
from roguelike_editors.inventory.model.right_panel.inventory_items_panel.inventory_items_panel_model import InventoryitemsPanelModel
from roguelike_editors.inventory.model.left_panel.panel_model import InventoryPanelModel
from roguelike_editors.inventory.model.right_panel.item_selection_panel.item_selection_panel_model import ItemSelectionPanelModel

@dataclass
class InventoryEditorModel:
    """
    Model for the Inventory Editor MVC.
    """
    visible: bool = False
    # Categories and selection
    categories: List[str] = field(default_factory=lambda: ['player', 'monsters', 'map'])
    current_category: str = 'player'
    # JSON data: default templates and active inventories
    default_data: Dict[str, Any] = field(default_factory=dict)
    active_data: Dict[str, Any] = field(default_factory=dict)
    # Editing state
    editing_side: Optional[str] = None  # 'default' or 'active'
    editing_property: Optional[str] = None
    editing_index: Optional[int] = None
    # Live inventory drag/drop and selection
    entities: Optional[List[int]] = None
    selected_eid: Optional[int] = None
    drag_item: Optional[tuple] = None  # (item_id, quantity)
    drag_slot: Optional[int] = None
    prev_left: bool = False
    prev_right: bool = False
    # Scroll offset for vertical scrolling of lists
    scroll_offset: int = 0
    # Panel models
    left_panel_model: InventoryPanelModel = field(default_factory=InventoryPanelModel)
    items_panel_model: InventoryitemsPanelModel = field(default_factory=InventoryitemsPanelModel)
    item_selection_panel_model: ItemSelectionPanelModel = field(default_factory=ItemSelectionPanelModel)
    # Detached camera focus position when user double-clicks a monster
    camera_focus_target: Optional[Any] = None

    @property
    def grid_model(self) -> InventoryitemsPanelModel:
        return self.items_panel_model
