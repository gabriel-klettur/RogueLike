import json
import pytest
from roguelike_game.ecs.components.item_models import ItemModel, ItemStack

def load_items_data():
    with open('data/items/items.json', encoding='utf-8') as f:
        return json.load(f)

def test_item_model_instantiation():
    items = load_items_data()
    for key, data in items.items():
        item = ItemModel(**data)
        assert item.id == data['id']
        assert item.name == data.get('name')
        assert item.description == data.get('description')

def test_stackable_properties():
    items = load_items_data()
    # Test gold stackable rules
    gold_data = items['gold']
    gold = ItemModel(**gold_data)
    assert gold.stackable is True
    assert gold.max_stack == 999
    assert gold.threshold == 10

    # Test non-stackable orb properties
    orb_data = items['experience_orb']
    orb = ItemModel(**orb_data)
    assert orb.stackable is False
    assert orb.max_stack is None
    assert orb.threshold is None

def test_item_stack_class():
    stack = ItemStack('gold', 5)
    assert stack.item_id == 'gold'
    assert stack.quantity == 5

def test_load_items_function():
    from roguelike_game.ecs.components.item_models import load_items, ItemModel
    items = load_items('data/items/items.json')
    raw = load_items_data()
    assert isinstance(items, dict)
    assert set(items.keys()) == set(raw.keys())
    for key, data in raw.items():
        assert key in items
        assert isinstance(items[key], ItemModel)

def test_consumable_item_model_validation():
    raw = load_items_data()
    from roguelike_game.ecs.components.item_models import ConsumableItemModel
    potion_data = raw['health_potion']
    potion = ConsumableItemModel(**potion_data)
    assert potion.effect == potion_data['effect']
    with pytest.raises(ValueError):
        ConsumableItemModel(**raw['gold'])

def test_equipable_item_model_validation():
    raw = load_items_data()
    from roguelike_game.ecs.components.item_models import EquipableItemModel
    sword_data = raw['iron_sword']
    sword = EquipableItemModel(**sword_data)
    assert sword.durability == sword_data['durability']
    assert sword.equip_slot == sword_data['equip_slot']
    with pytest.raises(ValueError):
        EquipableItemModel(**raw['experience_orb'])

def test_quest_item_model_validation():
    raw = load_items_data()
    from roguelike_game.ecs.components.item_models import QuestItemModel
    relic_data = raw['ancient_relic_mask']
    relic = QuestItemModel(**relic_data)
    assert relic.quest_id == relic_data['quest_id']
    with pytest.raises(ValueError):
        QuestItemModel(**raw['wood'])


def test_load_items_instantiates_subclasses():
    from roguelike_game.ecs.components.item_models import load_items, ItemModel, ConsumableItemModel, EquipableItemModel, QuestItemModel
    items = load_items('data/items/items.json')
    # Consumable
    assert isinstance(items['health_potion'], ConsumableItemModel)
    # Equipable
    assert isinstance(items['iron_sword'], EquipableItemModel)
    # Quest item
    assert isinstance(items['ancient_relic_mask'], QuestItemModel)
    # Default type
    assert isinstance(items['gold'], ItemModel)
