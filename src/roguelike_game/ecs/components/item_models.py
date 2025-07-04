from pydantic import BaseModel
from typing import Optional, List, Union, Dict


class ItemModel(BaseModel):
    """Modelo de datos para ítems cargados del JSON"""
    id: str
    name: str
    description: str
    stackable: bool
    max_stack: Optional[int] = None
    icon: Optional[Union[str, List[str]]] = None
    icon_small: Optional[str] = None
    icon_large: Optional[str] = None
    threshold: Optional[int] = None
    experience: Optional[int] = None
    effect: Optional[str] = None
    equip_slot: Optional[str] = None
    durability: Optional[int] = None
    quest_id: Optional[str] = None


class ItemStack:
    """Representa una pila de ítems en inventario"""
    def __init__(self, item_id: str, quantity: int):
        self.item_id = item_id
        self.quantity = quantity


def load_items(path: str) -> Dict[str, ItemModel]:
    """Carga ítems desde JSON y retorna dict de instancias"""
    import json
    with open(path, encoding='utf-8') as f:
        data = json.load(f)
    return {key: ItemModel(**val) for key, val in data.items()}
