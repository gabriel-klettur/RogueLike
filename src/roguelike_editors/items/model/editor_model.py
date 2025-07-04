from dataclasses import dataclass
from typing import Dict, Any, Optional

@dataclass
class ItemEditorModel:
    """Estado del editor de ítems: visibilidad y scroll."""
    items: Dict[str, Any]
    assets: Dict[str, Any]
    visible: bool = False
    scroll_index: int = 0
    hovered_item_id: Optional[str] = None
    selected_item_id: Optional[str] = None
