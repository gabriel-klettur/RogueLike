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

    # Estado de enfoque de cámara mientras se mantiene presionado sobre una coordenada
    holding_pos_focus: bool = False

    # Modos de edición (agregar/borrar en mapa)
    spawn_mode_active: bool = False
    delete_mode_active: bool = False
    # Ítem seleccionado para spawn (se define tras seleccionar en el picker)
    spawn_item_id: Optional[str] = None

