from dataclasses import dataclass
from typing import Dict, Any, Optional

@dataclass
class ItemPickerPanelModel:
    """Estado del editor de ítems: visibilidad y scroll."""
    items: Dict[str, Any]
    assets: Dict[str, Any]
    visible: bool = False
    hovered_item_id: Optional[str] = None
    selected_item_id: Optional[str] = None
