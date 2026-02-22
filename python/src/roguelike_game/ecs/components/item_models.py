from pydantic import BaseModel, model_validator, ConfigDict
from typing import Optional, List, Union, Dict


class ItemModel(BaseModel):
    model_config = ConfigDict(extra="allow", validate_assignment=True)
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
    damage: Optional[int] = None
    attack_speed: Optional[float] = None
    range: Optional[int] = None
    crit_chance: Optional[float] = None
    crit_multiplier: Optional[float] = None
    weight: Optional[float] = None
    value: Optional[int] = None
    rarity: Optional[str] = None
    level_requirement: Optional[int] = None
    # Escalado: factores de escala para diferentes vistas
    scale_editor: Optional[float] = 1.0
    scale_map: Optional[float] = 1.0
    scale_inventory: Optional[float] = 1.0
    # Despawn automático: segundos que permanece el drop en el suelo
    despawn_time: Optional[float] = None


class ItemStack:
    """Representa una pila de ítems en inventario"""
    def __init__(self, item_id: str, quantity: int):
        self.item_id = item_id
        self.quantity = quantity


class ConsumableItemModel(ItemModel):
    """Modelo para ítems consumibles: debe tener effect"""
    @model_validator(mode='after')
    def check_effect(cls, model):
        if model.effect is None:
            raise ValueError("Consumable items must have an effect")
        return model

class EquipableItemModel(ItemModel):
    """Modelo para ítems equipables: debe tener equip_slot y durability"""
    @model_validator(mode='after')
    def check_equipable(cls, model):
        if model.equip_slot is None or model.durability is None:
            raise ValueError("Equipable items must have equip_slot and durability")
        return model

class QuestItemModel(ItemModel):
    """Modelo para ítems de misión: debe tener quest_id"""
    @model_validator(mode='after')
    def check_quest_item(cls, model):
        if model.quest_id is None:
            raise ValueError("Quest items must have quest_id")
        return model

def load_items(path: str) -> Dict[str, ItemModel]:
    """Carga ítems desde JSON y retorna dict de instancias específicas según tipo"""
    import json
    with open(path, encoding='utf-8') as f:
        data = json.load(f)
    result: Dict[str, ItemModel] = {}
    for key, val in data.items():
        # Determinar subclase según campos específicos
        if val.get("effect") is not None:
            model_cls = ConsumableItemModel
        elif val.get("equip_slot") is not None or val.get("durability") is not None:
            model_cls = EquipableItemModel
        elif val.get("quest_id") is not None:
            model_cls = QuestItemModel
        else:
            model_cls = ItemModel
        result[key] = model_cls(**val)
    return result
