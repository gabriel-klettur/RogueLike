from types import SimpleNamespace

import roguelike_game.ecs.systems.chat.router.system as chat_router_mod
from roguelike_game.ecs.systems.chat.router.system import ChatRouterSystem


def test_chat_router_basic_flow_no_target_adds_npc_message(monkeypatch):
    # Avoid UI side-effects
    monkeypatch.setattr(chat_router_mod, "push_bubble", lambda *a, **k: None)

    sys_under_test = ChatRouterSystem()

    # Minimal state that collects messages
    msgs = []

    class State:
        def __init__(self):
            self.chat_open = True
            self.chat_lang_preference = "es"
            self.chat_messages = msgs
            self.chat_target_eid = None

        def chat_add_message(self, who, text):
            self.chat_messages.append((who, text))

    class Ctrl:
        def get_commits(self):
            return ["hola"]

    world = SimpleNamespace(
        components={},
        state=State(),
        _chat_input_ctrl=Ctrl(),
        player_entity=1,
    )

    sys_under_test.update(world)

    assert any(sender == "NPC" for sender, _ in msgs)
