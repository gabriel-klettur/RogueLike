from roguelike_game.ecs.systems.chat.router.system import ChatRouterSystem


def test_chat_router_system_initial_configuration_objects_exist():
    sys_under_test = ChatRouterSystem()
    assert sys_under_test.io is not None
    assert sys_under_test.scheduler is not None
    assert sys_under_test.vendor is not None
    # _root should resolve to a Path-like string (we don't rely on exact value)
    assert hasattr(sys_under_test, "_root")
