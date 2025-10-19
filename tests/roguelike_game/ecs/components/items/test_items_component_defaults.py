from roguelike_game.ecs.components.items.item_component import ItemComponent
from roguelike_game.ecs.components.items.buff_component import BuffComponent
from roguelike_game.ecs.components.items.healing_component import HealingComponent
from roguelike_game.ecs.components.items.teleport_component import TeleportComponent as ItemTeleportComponent


def test_item_component_construction():
    item = ItemComponent(definition_id="potion_health_small")
    assert item.definition_id == "potion_health_small"


def test_buff_component_construction():
    buff = BuffComponent(stat="power", value=2.5, duration=3.0)
    assert buff.stat == "power"
    assert buff.value == 2.5
    assert buff.duration == 3.0


def test_healing_component_construction():
    heal = HealingComponent(amount=25)
    assert heal.amount == 25


def test_item_teleport_component_construction():
    tp = ItemTeleportComponent(dest_map="dungeon_01", dest_x=10, dest_y=20)
    assert tp.dest_map == "dungeon_01"
    assert tp.dest_x == 10
    assert tp.dest_y == 20
