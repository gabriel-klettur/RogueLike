def handle_console(game, events) -> bool:
    try:
        if getattr(game, 'console_state', None) is not None:
            try:
                world = getattr(game, 'ecs', None).ecs_world
                if world and hasattr(world, 'state'):
                    world.state.console_open = bool(game.console_state.is_open)
            except Exception:
                pass
            if bool(game.console_state.is_open):
                for event in events:
                    try:
                        game.console_events.process_event(event)
                    except Exception:
                        pass
                return True
    except Exception:
        pass
    return False
