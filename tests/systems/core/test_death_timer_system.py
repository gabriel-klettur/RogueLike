import time
import pytest
from roguelike_game.ecs.components.combat.death_timer import DeathTimer


def test_death_timer_creation_and_attributes():
    start = time.time()
    duration = 2.5
    dt = DeathTimer(start, duration)
    assert dt.start_time == start
    assert dt.duration == duration


def test_death_timer_expiration():
    past_start = time.time() - 5
    dt = DeathTimer(past_start, duration=1)
    assert (time.time() - dt.start_time) > dt.duration, "El temporizador debería haber expirado"
