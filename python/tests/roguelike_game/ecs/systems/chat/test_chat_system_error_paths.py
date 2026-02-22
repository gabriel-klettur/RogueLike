from types import SimpleNamespace

from roguelike_game.ecs.systems.chat.router.system import ChatRouterSystem


def test_chat_router_update_no_state_graceful():
    sys_under_test = ChatRouterSystem()
    world = SimpleNamespace(components={})
    # Should not raise when state is missing
    sys_under_test.update(world)


def test_chat_router_update_chat_open_but_no_controller():
    sys_under_test = ChatRouterSystem()
    state = SimpleNamespace(chat_open=True)
    world = SimpleNamespace(components={}, state=state)
    # Should not raise when _chat_input_ctrl is missing
    sys_under_test.update(world)


def test_chat_router_update_empty_commits_noop():
    class Ctrl:
        def get_commits(self):
            return []
    sys_under_test = ChatRouterSystem()
    state = SimpleNamespace(chat_open=True)
    world = SimpleNamespace(components={}, state=state, _chat_input_ctrl=Ctrl())
    # Should not raise and effectively do nothing
    sys_under_test.update(world)
