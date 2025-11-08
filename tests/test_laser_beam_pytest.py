from __future__ import annotations

import sys
from pathlib import Path
import types
import pytest

# Ensure 'src' is importable
ROOT = Path(__file__).resolve().parents[1]
src_path = ROOT / 'src'
if str(src_path) not in sys.path:
    sys.path.insert(0, str(src_path))

from roguelike_game.ecs.components.transform.position import Position  # type: ignore
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent  # type: ignore
from roguelike_game.ecs.components.abilities.laser_beam_component import LaserBeamComponent  # type: ignore
from roguelike_game.ecs.systems.combat.spells.resolvers_pkg.beam import BeamResolver  # type: ignore
from roguelike_game.ecs.systems.particles.laser_beam_emitter_system import LaserBeamEmitterSystem  # type: ignore


@pytest.fixture()
def caster(world):
    eid = world.create_entity()
    world.components.setdefault('Position', {})[eid] = Position(100.0, 100.0)
    return eid


def _mock_mouse_tuple(v: bool, size: int = 5) -> tuple[bool, ...]:
    idx1 = 1  # middle button index
    arr = [False] * size
    arr[idx1] = bool(v)
    return tuple(arr)


def test_beam_resolver_registers_component(world, camera, caster, monkeypatch):
    resolver = BeamResolver()
    cfg = {
        'effect': { 'damage': 1.25, 'lifetime': 3 },
        'vfx': { 'sprite': { 'scale': 2.0 } },
        # particle params intentionally omitted -> defaults apply
    }
    # Mock position read to avoid reliance on real mouse
    import pygame  # type: ignore
    monkeypatch.setattr(pygame.mouse, 'get_pos', lambda: (200, 200))
    # Precondition: no beam
    assert len(world.components.get('LaserBeamComponent', {})) == 0

    resolver.resolve(world, caster, {}, cfg, camera)

    beams = world.components.get('LaserBeamComponent', {})
    assert caster in beams, 'BeamResolver debe registrar LaserBeamComponent para el caster'
    comp = beams[caster]
    # Sanity over key parameters
    assert isinstance(comp, LaserBeamComponent)
    assert pytest.approx(getattr(comp, 'damage', 0.0), rel=1e-6) == 1.25
    assert pytest.approx(getattr(comp, 'scale', 0.0), rel=1e-6) == 2.0
    # Lifespan stored on component (used as particle lifespan hint)
    assert float(getattr(comp, 'lifespan', 0.0)) >= 0.0


def test_emitter_emits_while_held_and_clears_on_release(world, camera, caster, monkeypatch):
    # Prepare a beam
    resolver = BeamResolver()
    cfg = { 'effect': { 'damage': 1.0, 'lifetime': 4 }, 'vfx': { 'sprite': { 'scale': 1.5 } } }
    resolver.resolve(world, caster, {}, cfg, camera)

    # Mock pygame mouse/key APIs to simulate hold and positions
    # Import pygame and replace used functions
    import pygame  # type: ignore
    monkeypatch.setattr(pygame.mouse, 'get_pressed', lambda n=5: _mock_mouse_tuple(True, n))
    monkeypatch.setattr(pygame.key, 'get_pressed', lambda: tuple([False] * 512))
    monkeypatch.setattr(pygame.mouse, 'get_pos', lambda: (200, 200))

    # Ensure emitter falls back (no InputSystem present)
    setattr(world, 'update_systems', [])

    emitter = LaserBeamEmitterSystem(perf_log=None)

    # 1) While held -> emits particles
    # Ensure component maps exist for emitter writes
    world.components.setdefault('Position', {})
    world.components.setdefault('ParticleComponent', {})
    # Provide minimal API expected by the emitter for damage pass
    # For this test, we don't assert damage, so no targets
    if not hasattr(world, 'get_entities_with'):
        import types as _types
        world.get_entities_with = _types.MethodType(lambda self, *names: [], world)  # type: ignore[attr-defined]
    before = len(world.components.get('ParticleComponent', {}))
    emitter.update(world, camera)
    after = len(world.components.get('ParticleComponent', {}))
    assert after > before, 'El emisor debe crear partículas cuando el botón está presionado'
    # Al menos una partícula tiene lifespan >= 2 (mínimo impuesto)
    (pid, part) = next(iter(world.components.get('ParticleComponent', {}).items()))
    assert int(getattr(part, 'lifespan', 0)) >= 2

    # 2) On release -> beams cleared
    monkeypatch.setattr(pygame.mouse, 'get_pressed', lambda n=5: _mock_mouse_tuple(False, n))
    emitter.update(world, camera)
    beams = world.components.get('LaserBeamComponent', {})
    assert len(beams) == 0, 'Al soltar, el emisor debe limpiar todos los láseres activos'
