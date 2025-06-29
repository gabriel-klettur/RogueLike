# Path: tests/systems/rendering/test_death_timer_bar_system.py
import time
import pytest
import pygame
from types import SimpleNamespace

import roguelike_engine.config.config as config
from roguelike_game.ecs.systems.rendering.death_timer_bar_system import DeathTimerBarSystem
from roguelike_game.ecs.components.combat.death_timer import DeathTimer
from roguelike_game.ecs.fsm.states.death_state import DeathState

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()

class DummyCamera:
    def apply(self, pos):
        return pos

class DummyWorld:
    def __init__(self, start, duration):
        eid = 1
        now = time.time()
        self.components = {}
        self.components['DeathTimer'] = {eid: DeathTimer(start, duration)}
        # NPCState with current_state = DeathState
        state = SimpleNamespace(fsm=SimpleNamespace(current_state=DeathState()))
        self.components['NPCState'] = {eid: state}
        # Position
        self.components['Position'] = {eid: SimpleNamespace(x=10, y=20)}
        # Sprite with image
        surf = pygame.Surface((40, 80))
        class Sprite:
            def __init__(self, image):
                self.image = image
        self.components['Sprite'] = {eid: Sprite(surf)}
        # Scale optional
        self.components['Scale'] = {}
        self.eid = eid

    def set_scale(self, scale):
        self.components['Scale'][self.eid] = SimpleNamespace(scale=scale)
        return self


def test_active_timers_filters_expired_and_active():
    now = time.time()
    # Active timer
    w1 = DummyWorld(now - 5, 10)
    sys = DeathTimerBarSystem(perf_log=None)
    active1 = sys._active_timers(w1, now)
    assert w1.eid in active1
    # Expired timer
    w2 = DummyWorld(now - 15, 10)
    active2 = sys._active_timers(w2, now)
    assert active2 == {}


def test_gather_draw_params_without_scale():
    now = time.time()
    w = DummyWorld(now - 3, 10)
    sys = DeathTimerBarSystem(perf_log=None, bar_height=5, offset=2)
    cam = DummyCamera()
    dt = w.components['DeathTimer'][w.eid]
    params = sys._gather_draw_params(w.eid, w, now, dt, cam)
    # Width equals sprite width
    assert params['width'] == 40
    assert params['height'] == 5
    assert pytest.approx(params['ratio'], rel=1e-3) == (10 - 3) / 10
    assert params['x'] == 10
    assert params['y'] == 20 - 2 - 5


def test_gather_draw_params_with_scale():
    now = time.time()
    w = DummyWorld(now - 2, 8).set_scale(2.0)
    sys = DeathTimerBarSystem(perf_log=None, bar_height=6, offset=3)
    cam = DummyCamera()
    dt = w.components['DeathTimer'][w.eid]
    params = sys._gather_draw_params(w.eid, w, now, dt, cam)
    # Width equals sprite width * scale
    assert params['width'] == 40 * 2
    assert params['height'] == 6
    assert pytest.approx(params['ratio'], rel=1e-3) == (8 - 2) / 8
    assert params['x'] == 10
    assert params['y'] == 20 - 3 - 6