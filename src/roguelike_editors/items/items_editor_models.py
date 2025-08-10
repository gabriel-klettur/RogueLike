from dataclasses import dataclass
from typing import Any, Dict, Optional


@dataclass
class ItemsEditorModel:
    """Estado global del Editor de Ítems (SSOT).

    Mantiene selección/hover y visibilidad compartida entre paneles.
    """

    items: Dict[str, Any]
    assets: Dict[str, Any]

    visible: bool = False
    selected_item_id: Optional[str] = None
    hovered_item_id: Optional[str] = None

    # Título (para TitleBar)
    title: str = "ITEMS EDITOR"

