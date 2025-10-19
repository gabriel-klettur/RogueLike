import dataclasses

from roguelike_game.ecs.components.chat.chat_component import ChatComponent


def test_chat_component_defaults_and_mutable_fields():
    chat = ChatComponent()
    assert chat.chat_range == 10.0
    assert chat.role == "generic"
    assert chat.persona_id is None
    assert chat.greeting is None
    # recent_messages uses default_factory -> new list each instance
    a = ChatComponent()
    b = ChatComponent()
    assert a.recent_messages == []
    assert b.recent_messages == []
    assert a.recent_messages is not b.recent_messages


def test_chat_component_dataclass_serialization():
    chat = ChatComponent(chat_range=5.0, role="vendor", persona_id="shopkeeper")
    data = dataclasses.asdict(chat)
    assert data["chat_range"] == 5.0
    assert data["role"] == "vendor"
    assert data["persona_id"] == "shopkeeper"
    assert data["recent_messages"] == []
