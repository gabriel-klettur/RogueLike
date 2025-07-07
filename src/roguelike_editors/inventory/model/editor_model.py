from dataclasses import dataclass
from typing import List, Optional

@dataclass
class InventoryEditorModel:
    """
    Model for the Inventory Editor MVC.
    """
    visible: bool = False
    entities: Optional[List[int]] = None
    selected_eid: Optional[int] = None
    drag_item: Optional[tuple] = None  # (item_id, quantity)
    drag_slot: Optional[int] = None
    prev_left: bool = False
    prev_right: bool = False
