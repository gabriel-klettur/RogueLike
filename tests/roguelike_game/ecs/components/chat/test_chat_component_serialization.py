import dataclasses

from roguelike_game.ecs.components.chat.chat_component import ChatComponent
from roguelike_game.ecs.components.chat.vendor_component import VendorComponent


def test_chat_component_asdict_serialization():
    chat = ChatComponent(chat_range=7.5, role="vendor", persona_id="npc_01", greeting="hi")
    chat.recent_messages.append("hello")
    data = dataclasses.asdict(chat)
    assert data["chat_range"] == 7.5
    assert data["role"] == "vendor"
    assert data["persona_id"] == "npc_01"
    assert data["greeting"] == "hi"
    assert data["recent_messages"] == ["hello"]


def test_vendor_component_default_prices_and_asdict():
    vendor = VendorComponent()
    data = dataclasses.asdict(vendor)
    assert data["prices"] == {"wood": 1}
    assert data["currency_item_id"] == "gold"
