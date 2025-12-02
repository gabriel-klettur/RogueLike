import math
import time
from roguelike_engine.utils.benchmark.benchmark import benchmark
from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.components.abilities.chain_lightning_component import ChainLightningComponent
from roguelike_game.ecs.components.abilities.lightning_component import LightningComponent
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.systems.combat.spells.resolvers_pkg.utils import get_entity_center
from roguelike_game.ecs.utils.health_utils import is_neutral


class ChainLightningSystem:
    """
    Resuelve en cascada los impactos de ChainLightningComponent creando un LightningComponent
    visual por cada salto y aplicando daño decaído a entidades con Health en rango.
    Tras resolver todos los rebotes o quedar sin objetivos, elimina el componente.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    def _find_next_target(self, world, origin: tuple[float, float], owner: int | None, already_hit: set[int], rng: float):
        ox, oy = float(origin[0]), float(origin[1])
        best = None
        best_d2 = None
        hp_map = world.components.get('Health', {})
        dead_map = world.components.get('DeathTimer', {})
        dying_map = world.components.get('DyingTag', {})
        pos_map = world.components.get('Position', {})
        for eid in hp_map.keys():
            if eid == owner:
                continue
            if eid in already_hit:
                continue
            if eid in dead_map or eid in dying_map:
                continue
            if is_neutral(world, eid):
                continue
            pos = pos_map.get(eid)
            if pos is None:
                continue
            tx, ty = get_entity_center(world, eid)
            dx, dy = float(tx) - ox, float(ty) - oy
            d2 = dx*dx + dy*dy
            if d2 <= float(rng) * float(rng) + 1e-6:
                if best is None or d2 < best_d2:
                    best = eid
                    best_d2 = d2
        return best

    def _spawn_bolt_visual(self, world, start: tuple[float, float], end: tuple[float, float], cfg):
        # Config de rayo: leer desde cfg si existen, si no, defaults razonables
        try:
            # Usar defaults también cuando los valores presentes sean 0 o falsy
            raw_seg = cfg.get('segments') if hasattr(cfg, 'get') else None
            raw_off = cfg.get('offset') if hasattr(cfg, 'get') else None
            raw_life = cfg.get('lifetime') if hasattr(cfg, 'get') else None
            segments = int(raw_seg) if isinstance(raw_seg, (int, float)) and int(raw_seg) > 0 else 12
            offset = int(raw_off) if isinstance(raw_off, (int, float)) and int(raw_off) > 0 else 15
            lifetime = int(raw_life) if isinstance(raw_life, (int, float)) and int(raw_life) > 0 else 8
        except Exception:
            segments, offset, lifetime = 12, 15, 8
        eid = world.create_entity()
        world.components.setdefault('LightningComponent', {})[eid] = LightningComponent(
            start, end, segments, offset, lifetime,
            preset_id=(cfg.get('vfx') if isinstance(cfg.get('vfx'), str) else None)
        )
        # También registrar Position para algunos sistemas de partículas anclados si fuera necesario
        world.components.setdefault('Position', {})[eid] = Position(start[0], start[1])

    @benchmark(lambda self: self.perf_log, 'ChainLightningSystem.update')
    def update(self, world, camera=None):
        comps = world.components.get('ChainLightningComponent', {})
        if not comps:
            return
        # Procesar y limpiar en este mismo frame (rápido y visualmente consistente)
        to_remove = []
        for eid, comp in list(comps.items()):
            origin = tuple(comp.current_pos)
            owner = getattr(comp, 'owner', None)
            damage = float(getattr(comp, 'damage', 0.0))
            bounces = int(getattr(comp, 'bounces_left', 0))
            rng = float(getattr(comp, 'range', 0.0))
            decay = float(getattr(comp, 'damage_decay', 1.0))
            spell_key = getattr(comp, 'spell_key', '')
            cfg = SPELLS.get(spell_key) if spell_key else None
            # Resolver como mucho N rebotes por frame para evitar ciclos largos
            max_steps = max(1, bounces)
            steps = 0
            while steps < max_steps and bounces > 0 and damage > 0 and rng > 0:
                tgt = self._find_next_target(world, origin, owner, comp.already_hit, rng)
                if tgt is None:
                    break
                # Visual bolt
                tx, ty = get_entity_center(world, tgt)
                if cfg is not None:
                    self._spawn_bolt_visual(world, origin, (tx, ty), cfg)
                else:
                    # usar defaults si no hay cfg
                    self._spawn_bolt_visual(world, origin, (tx, ty), {})
                # Aplicar daño entero
                th = world.components.get('Health', {}).get(tgt)
                if th is not None:
                    try:
                        th.current_hp = max(0, int(th.current_hp) - int(max(1, round(damage))))
                    except Exception:
                        pass
                # Marcar impacto
                comp.already_hit.add(int(tgt))
                origin = (float(tx), float(ty))
                comp.current_pos = origin
                bounces -= 1
                comp.bounces_left = bounces
                damage = float(damage) * float(decay)
                steps += 1
            # Si no hubo objetivos cercanos, mostrar un rayo de apunte desde el caster hasta el punto inicial
            if steps == 0:
                try:
                    if owner is not None:
                        sx, sy = get_entity_center(world, owner)
                        self._spawn_bolt_visual(world, (sx, sy), origin, cfg or {})
                except Exception:
                    pass
            # Terminar si no quedan rebotes o no hubo objetivos
            if bounces <= 0 or steps == 0:
                to_remove.append(eid)
        for eid in to_remove:
            comps.pop(eid, None)
