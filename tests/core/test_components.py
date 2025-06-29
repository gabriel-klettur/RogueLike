import pytest
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.combat.combat_stats import CombatStats
from roguelike_game.ecs.components.combat.melee_weapon import MeleeWeapon
from roguelike_game.ecs.components.combat.hitbox import HitboxComponent


def test_position():
    p = Position(1, 2)
    assert p.x == 1 and p.y == 2


def test_velocity():
    v = Velocity()
    assert v.vx == 0 and v.vy == 0
    v2 = Velocity(3, 4)
    assert v2.vx == 3 and v2.vy == 4


def test_combat_stats():
    cs = CombatStats(current_hp=10, max_hp=20, power=5, defense=2)
    assert cs.current_hp == 10
    assert cs.max_hp == 20
    assert cs.power == 5
    assert cs.defense == 2


def test_melee_weapon():
    mw = MeleeWeapon(damage=3, cooldown=1.5)
    assert mw.damage == 3
    assert mw.cooldown == 1.5


def test_hitbox_component():
    hb = HitboxComponent(owner=1, offset=0, radius=10, arc_angle=1.57, direction=(1, 0), lifespan=5, damage=2)
    assert hb.owner == 1
    assert hb.offset == 0
    assert hb.radius == 10
    assert hb.arc_angle == 1.57
    assert hb.direction == (1, 0)
    assert hb.lifespan == 5
    assert hb.damage == 2
    assert isinstance(hb.hit_targets, set) and len(hb.hit_targets) == 0
