import types

from roguelike_game.ecs.systems.input import input_system as mod


def test_movement_keys_update_input_and_velocity(monkeypatch):
    # Monkeypatch InputConfig to control key bindings
    class FakeInputConfig:
        def __init__(self, config_path=None):
            pass
        def _load(self):
            pass
        def get_keys_for_action(self, action: str):
            # Map actions to synthetic key codes
            mapping = {
                'move_right': [101],
                'move_left': [102],
                'move_up': [103],
                'move_down': [104],
                'attack': [],
                'toggle_item_editor': [],
                'toggle_inventory': [],
                'interact': [],
            }
            return mapping.get(action, [])

    # Simulate pressed keys: right + up
    class FakeKeys:
        def __init__(self, pressed):
            self.pressed = set(pressed)
        def __getitem__(self, code):
            return 1 if code in self.pressed else 0

    pressed_codes = {101, 103}  # right + up

    # Monkeypatch pygame internals used
    monkeypatch.setattr(mod, 'InputConfig', FakeInputConfig, raising=True)
    monkeypatch.setattr(mod.pygame.key, 'get_pressed', lambda: FakeKeys(pressed_codes), raising=True)
    monkeypatch.setattr(mod.pygame.key, 'get_mods', lambda: 0, raising=True)
    monkeypatch.setattr(mod.pygame, 'K_F4', 9999, raising=False)
    monkeypatch.setattr(mod.pygame, 'KMOD_ALT', 0, raising=False)

    # Capture calls to set_velocity_from_input
    captured = {}
    def fake_set_velocity_from_input(vel, ms, mx, my):
        captured['args'] = (mx, my)
        if vel is not None:
            vel.vx = mx * (getattr(ms, 'speed', 1) if ms else 1)
            vel.vy = my * (getattr(ms, 'speed', 1) if ms else 1)

    monkeypatch.setattr(mod, 'set_velocity_from_input', fake_set_velocity_from_input, raising=True)

    sys = mod.InputSystem(perf_log=None, config_path=None)

    # Minimal world and components
    class Vel: vx = 0; vy = 0
    class Ms: speed = 2
    class Inp:
        move_x = 0
        move_y = 0
        attack = False
        toggle_editor = False
        toggle_inventory = False
        interact = False
        show_all_drops = False

    world = types.SimpleNamespace()
    world.components = {
        'InputComponent': {1: Inp()},
        'Velocity': {1: Vel()},
        'MovementSpeed': {1: Ms()},
        'PlayerTagComponent': {},  # not player to avoid FSM branch complexity
    }
    world.state = types.SimpleNamespace(buildings_editor_active=False, particles_editor_visible=False)

    # No global block
    monkeypatch.setattr(mod, 'block_reason', lambda world: None, raising=True)

    # Act
    sys.update(world)

    # Assert movement set and velocity computed with speed=2
    inp = world.components['InputComponent'][1]
    assert inp.move_x == 1  # right
    assert inp.move_y == -1  # up
    assert captured['args'] == (1, -1)
    vel = world.components['Velocity'][1]
    assert (vel.vx, vel.vy) == (2, -2)
