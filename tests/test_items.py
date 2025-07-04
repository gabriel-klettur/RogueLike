import json
import pytest
from roguelike_game.ecs.components.item_models import ItemModel, ItemStack


def load_items_data():
    with open('data/items.json', encoding='utf-8') as f:
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
    items = load_items('data/items.json')
    raw = load_items_data()
    assert isinstance(items, dict)
    assert set(items.keys()) == set(raw.keys())
    for key, data in raw.items():
        assert key in items
        assert isinstance(items[key], ItemModel)
